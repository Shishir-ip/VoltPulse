using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Calculations;
using VoltPulse.Core.Models;

namespace VoltPulse.Providers;

/// <summary>Aggregates one or more <see cref="IBatteryProvider"/>s; higher-priority providers win per field.</summary>
public interface IAggregatedBatteryProvider
{
    BatterySnapshot ReadSnapshot();

    /// <summary>Raised on a background thread when the number of visible batteries changed.</summary>
    event Action? BatterySetChanged;

    /// <summary>Asks all refreshable providers to drop cached handles (resume, device swap...).</summary>
    void Invalidate();
}

/// <summary>
/// Merges providers in priority order (first = highest). For each field the first provider that
/// returns a valid value wins, and the value remembers its source. Multiple physical batteries are
/// matched by enumeration index across providers.
/// </summary>
public sealed class AggregatedBatteryProvider : IAggregatedBatteryProvider
{
    private readonly IReadOnlyList<IBatteryProvider> _providers;
    private readonly SystemInfoProvider _systemInfo;
    private readonly Serilog.ILogger _logger;
    private readonly IBatteryProvider? _demoProvider;
    private readonly Func<bool>? _shouldUseDemoProvider;
    private readonly Func<string?>? _preferredProviderSelector;
    private readonly object _gate = new();
    private int _lastCount = -1;

    public event Action? BatterySetChanged;

    public AggregatedBatteryProvider(
        IEnumerable<IBatteryProvider> providers,
        SystemInfoProvider systemInfo,
        Serilog.ILogger logger,
        IBatteryProvider? demoProvider = null,
        Func<bool>? shouldUseDemoProvider = null,
        Func<string?>? preferredProviderSelector = null)
    {
        _providers = providers.ToArray();
        _systemInfo = systemInfo;
        _logger = logger.ForContext<AggregatedBatteryProvider>();
        _demoProvider = demoProvider;
        _shouldUseDemoProvider = shouldUseDemoProvider;
        _preferredProviderSelector = preferredProviderSelector;
    }

    public BatterySnapshot ReadSnapshot()
    {
        var statuses = new List<ProviderStatusInfo>();
        var perProvider = new List<IReadOnlyList<BatteryData>>();
        int maxCount = 0;

        IEnumerable<IBatteryProvider> ordered = _providers;
        string? preferred = _preferredProviderSelector?.Invoke();
        if (!string.IsNullOrEmpty(preferred))
        {
            ordered = _providers.OrderByDescending(p => string.Equals(p.Name, preferred, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        }

        foreach (var provider in ordered)
        {
            IReadOnlyList<BatteryData> list = [];
            try
            {
                list = provider.ReadAll();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Provider {Provider} failed", provider.Name);
            }
            statuses.Add(new ProviderStatusInfo(provider.Name, list.Count > 0, provider.LastError));
            perProvider.Add(list);
            maxCount = Math.Max(maxCount, list.Count);
        }

        var merged = new List<BatteryData>(maxCount);
        for (int i = 0; i < maxCount; i++)
        {
            var target = new BatteryData();
            foreach (var list in perProvider)
            {
                if (i >= list.Count) continue;
                if (string.IsNullOrEmpty(target.Id)) target.Id = list[i].Id;
                target.FillMissingFrom(list[i]);
            }
            if (string.IsNullOrWhiteSpace(target.Id)) target.Id = $"BAT{i + 1}";
            FillDerived(target);
            merged.Add(target);
        }

        if (merged.Count == 0 && _demoProvider is not null && _shouldUseDemoProvider?.Invoke() == true)
        {
            try
            {
                var demoList = _demoProvider.ReadAll();
                if (demoList.Count > 0)
                {
                    statuses.Add(new ProviderStatusInfo(_demoProvider.Name, true, null));
                    merged.AddRange(demoList);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Demo provider failed");
            }
        }

        var snapshot = new BatterySnapshot
        {
            Batteries = merged,
            Combined = Combine(merged),
            System = _systemInfo.Get(),
            ProviderStatuses = statuses,
        };

        lock (_gate)
        {
            if (_lastCount != merged.Count)
            {
                bool first = _lastCount < 0;
                _lastCount = merged.Count;
                if (!first)
                {
                    _logger.Information("Battery set changed: now {Count} battery(ies)", merged.Count);
                    BatterySetChanged?.Invoke();
                }
            }
        }

        return snapshot;
    }

    public void Invalidate()
    {
        foreach (var provider in _providers)
        {
            if (provider is not IRefreshable refreshable) continue;
            try
            {
                refreshable.Invalidate();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Invalidating {Provider} failed", provider.Name);
            }
        }
    }

    /// <summary>Derives obvious gaps from known fields so downstream UI works on partial drivers.</summary>
    private static void FillDerived(BatteryData b)
    {
        if (!b.CurrentCapacityMWh.HasValue && b.Percent.Value is { } pct)
        {
            uint? baseCapacity = b.FullChargeCapacityMWh.Value ?? b.DesignedCapacityMWh.Value;
            if (baseCapacity is > 0)
            {
                b.CurrentCapacityMWh = Sourced<uint?>.From((uint)Math.Round(baseCapacity.Value * pct / 100.0),
                    DataProviderSource.Derived, "Estimated from charge level and capacity");
            }
        }
        if (!b.Percent.HasValue && b.CurrentCapacityMWh.Value is { } cap && b.FullChargeCapacityMWh.Value is { } full && full > 0)
        {
            b.Percent = Sourced<double?>.From(Math.Clamp(cap * 100.0 / full, 0, 100),
                DataProviderSource.Derived, "Estimated from capacity");
        }
        if (!b.ChargeState.HasValue && b.AcOnline.Value is { } ac)
        {
            b.ChargeState = Sourced<ChargeState?>.From(
                BatteryMath.ResolveChargeState(ac, false, false, b.Percent.Value), DataProviderSource.Derived);
        }
    }

    private static BatteryData? Combine(List<BatteryData> list)
    {
        if (list.Count == 0) return null;
        if (list.Count == 1) return list[0].Clone();

        var combined = new BatteryData { Id = "COMBINED" };
        combined.DisplayName = Sourced<string?>.From("All batteries", DataProviderSource.Derived, "Combined across all batteries");

        double percentSum = 0, weightSum = 0;
        uint? design = 0, full = 0, current = 0;
        bool designAll = true, fullAll = true, currentAll = true;
        int? rate = 0;
        bool rateAll = true;
        double? maxTemp = null;
        bool? ac = null;

        foreach (var b in list)
        {
            if (b.Percent.Value is { } p)
            {
                double w = b.CurrentCapacityMWh.Value ?? 0;
                percentSum += p * (w > 0 ? w : 1);
                weightSum += w > 0 ? w : 1;
            }

            design = design is { } d && b.DesignedCapacityMWh.Value is { } dv ? d + dv : null;
            designAll &= b.DesignedCapacityMWh.HasValue;
            full = full is { } f && b.FullChargeCapacityMWh.Value is { } fv ? f + fv : null;
            fullAll &= b.FullChargeCapacityMWh.HasValue;
            current = current is { } c && b.CurrentCapacityMWh.Value is { } cv ? c + cv : null;
            currentAll &= b.CurrentCapacityMWh.HasValue;
            rate = rate is { } r && b.RateMilliwatts.Value is { } rv ? r + rv : null;
            rateAll &= b.RateMilliwatts.HasValue;
            if (b.TemperatureCelsius.Value is { } t) maxTemp = Math.Max(maxTemp ?? double.MinValue, t);
            if (b.AcOnline.Value is { } a) ac = ac is null ? a : ac.Value && a;
        }

        if (weightSum > 0) combined.Percent = Sourced<double?>.From(percentSum / weightSum, DataProviderSource.Derived);
        if (designAll) combined.DesignedCapacityMWh = Sourced<uint?>.From(design, DataProviderSource.Derived);
        if (fullAll) combined.FullChargeCapacityMWh = Sourced<uint?>.From(full, DataProviderSource.Derived);
        if (currentAll) combined.CurrentCapacityMWh = Sourced<uint?>.From(current, DataProviderSource.Derived);
        if (rateAll) combined.RateMilliwatts = Sourced<int?>.From(rate, DataProviderSource.Derived);
        if (maxTemp is { } mt) combined.TemperatureCelsius = Sourced<double?>.From(mt, DataProviderSource.Derived, "Hottest battery");
        if (ac is { } a2) combined.AcOnline = Sourced<bool?>.From(a2, DataProviderSource.Derived);

        var first = list[0];
        combined.ChargeState = first.ChargeState;
        combined.Critical = Sourced<bool?>.From(list.Any(b => b.Critical.Value == true), DataProviderSource.Derived);
        return combined;
    }
}
