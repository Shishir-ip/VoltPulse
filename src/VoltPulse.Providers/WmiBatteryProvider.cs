using System.Globalization;
using System.Management;
using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Calculations;
using VoltPulse.Core.Models;

namespace VoltPulse.Providers;

/// <summary>
/// Provider B: WMI (root\WMI battery classes + Win32_Battery in root\CIMV2). Used as a
/// fallback and cross-check for the IOCTL provider, and is the only source for cycle counts
/// and the manufacture date on many machines. Every property is read defensively - missing
/// classes or access-denied degrade to unknown values, never exceptions.
/// </summary>
public sealed class WmiBatteryProvider : IBatteryProvider, IRefreshable
{
    private const string WmiNamespace = @"root\WMI";
    private const string CimNamespace = @"root\CIMV2";
    private static readonly TimeSpan StaticsRefresh = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FullChargeRefresh = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CimRefresh = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);

    private readonly Serilog.ILogger _logger;
    private readonly object _gate = new();

    private ManagementScope? _scope;
    private DateTime _scopeFailedUntilUtc = DateTime.MinValue;

    private Dictionary<string, BatteryData>? _statics;      // keyed by instance name
    private Dictionary<string, uint?>? _fullCharge;         // keyed by instance name
    private DateTime _staticsAtUtc = DateTime.MinValue;
    private DateTime _fullChargeAtUtc = DateTime.MinValue;
    private Dictionary<string, (double? Percent, int? DesignVoltageMv, string? Chemistry, string? Manufacturer, string? Serial, long? EstimatedRuntime, string? Name)>? _cim;
    private DateTime _cimAtUtc = DateTime.MinValue;

    public WmiBatteryProvider(Serilog.ILogger logger) => _logger = logger.ForContext<WmiBatteryProvider>();

    public string Name => "WMI";
    public bool IsAvailable { get; private set; }
    public string? LastError { get; private set; }

    public IReadOnlyList<BatteryData> ReadAll()
    {
        lock (_gate)
        {
            var results = new List<BatteryData>();
            try
            {
                if (DateTime.UtcNow < _scopeFailedUntilUtc)
                {
                    return results;
                }
                if (_scope is null && !Connect())
                {
                    return results;
                }

                var status = QueryInstances("SELECT * FROM BatteryStatus");
                var temperatures = QueryInstances("SELECT InstanceName, Temperature FROM BatteryTemperature")
                    .ToDictionary(kv => kv.Key, kv => kv.Value);

                EnsureStatics(status.Keys);

                try
                {
                foreach (var (instanceName, statusRow) in status)
                {
                    var data = new BatteryData { Id = $"WMI:{instanceName}" };
                    const DataProviderSource src = DataProviderSource.Wmi;
                    data.DisplayName = Sourced<string?>.From(instanceName, src);

                    bool? online = statusRow.GetBool("PowerOnline");
                    bool charging = statusRow.GetBool("Charging") == true;
                    bool discharging = statusRow.GetBool("Discharging") == true;
                    data.AcOnline = Sourced<bool?>.From(online, src);
                    data.ChargeState = Sourced<ChargeState?>.From(
                        BatteryMath.ResolveChargeState(online, charging, discharging, null), src);
                    data.Critical = Sourced<bool?>.From(statusRow.GetBool("Critical"), src);

                    uint? chargeRate = statusRow.GetUint("ChargeRate");
                    uint? dischargeRate = statusRow.GetUint("DischargeRate");
                    int? rate = (charging, discharging) switch
                    {
                        (true, _) when chargeRate is { } cr && cr != uint.MaxValue => (int)cr,
                        (_, true) when dischargeRate is { } dr && dr != uint.MaxValue => -(int)dr,
                        _ => null,
                    };
                    if (rate is not null)
                    {
                        data.RateMilliwatts = Sourced<int?>.From(rate, src);
                    }

                    uint? voltage = statusRow.GetUint("Voltage");
                    if (voltage is { } v && BatteryNativeHelper.IsKnownVoltage(v))
                    {
                        data.VoltageMillivolts = Sourced<int?>.From((int)v, src);
                    }

                    if (temperatures.TryGetValue(instanceName, out var tempRow) && tempRow.GetUint("Temperature") is { } tenthsKelvin
                        && tenthsKelvin is > 0 and < 20000)
                    {
                        data.TemperatureCelsius = Sourced<double?>.From(Math.Round(tenthsKelvin / 10.0 - 273.15, 1), src);
                    }

                    if (_statics?.TryGetValue(instanceName, out var st) == true)
                    {
                        MergeStatics(data, st);
                    }
                    if (_fullCharge?.TryGetValue(instanceName, out var full) == true && full is { } f && f > 0)
                    {
                        data.FullChargeCapacityMWh = Sourced<uint?>.From(f, src);
                        if (data.Percent.Value is null && data.CurrentCapacityMWh.Value is { } cc && f > 0)
                        {
                            data.Percent = Sourced<double?>.From(Math.Clamp(cc * 100.0 / f, 0, 100), src);
                        }
                    }

                    if (TryGetCim(instanceName, results.Count == 1, out var cim))
                    {
                        MergeCim(data, cim);
                    }

                    results.Add(data);
                }
                }
                finally
                {
                    foreach (var row in status.Values) row.Dispose();
                    foreach (var row in temperatures.Values) row.Dispose();
                }

                IsAvailable = results.Count > 0;
                if (IsAvailable) LastError = null;
            }
            catch (Exception ex)
            {
                SetError(ex.Message);
                _logger.Debug(ex, "WMI read failed");
                _scopeFailedUntilUtc = DateTime.UtcNow + RetryAfterFailure;
                _scope = null;
            }
            return results;
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _scope = null;
            _statics = null;
            _fullCharge = null;
            _cim = null;
            _staticsAtUtc = _fullChargeAtUtc = _cimAtUtc = DateTime.MinValue;
        }
    }

    private static void MergeStatics(BatteryData data, BatteryData st)
    {
        data.FillMissingFrom(st);
        if (st.CycleCount.Value is { } cycles)
        {
            data.CycleCount = Sourced<int?>.From(cycles, DataProviderSource.Wmi);
            data.CycleCountEstimated = false;
        }
    }

    private bool TryGetCim(string wmiInstanceName, bool soleBattery, out (double? Percent, int? DesignVoltageMv, string? Chemistry, string? Manufacturer, string? Serial, long? EstimatedRuntime, string? Name) cim)
    {
        // root\WMI instance names and Win32_Battery keys never match directly; with a single
        // battery (the overwhelmingly common case) join by position, otherwise try the suffix.
        if (_cim is null)
        {
            cim = default;
            return false;
        }
        if (soleBattery && _cim.Count == 1)
        {
            cim = _cim.Values.First();
            return true;
        }
        return _cim.TryGetValue(InstanceKey(wmiInstanceName), out cim);
    }

    private static void MergeCim(
        BatteryData data,
        (double? Percent, int? DesignVoltageMv, string? Chemistry, string? Manufacturer, string? Serial, long? EstimatedRuntime, string? Name) cim)
    {
        const DataProviderSource src = DataProviderSource.Wmi;
        if (cim.Percent is { } p) data.Percent = Sourced<double?>.From(p, src);
        if (cim.DesignVoltageMv is { } dv) data.DesignedVoltageMillivolts = Sourced<int?>.From(dv, src);
        if (cim.Chemistry is { } ch) data.Chemistry = Sourced<string?>.From(ch, src);
        if (cim.Manufacturer is { } m) data.Manufacturer = Sourced<string?>.From(m, src);
        if (cim.Serial is { } s) data.SerialNumber = Sourced<string?>.From(s, src);
        if (cim.EstimatedRuntime is { } rt) data.EstimatedRunTimeSeconds = Sourced<long?>.From(rt, src);
        if (cim.Name is { } n && !data.DeviceName.HasValue) data.DeviceName = Sourced<string?>.From(n, src);
    }

    private bool Connect()
    {
        try
        {
            _scope = new ManagementScope(WmiNamespace);
            _scope.Connect();
            return true;
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            _scopeFailedUntilUtc = DateTime.UtcNow + RetryAfterFailure;
            _scope = null;
            return false;
        }
    }

    private void EnsureStatics(ICollection<string> instanceNames)
    {
        var now = DateTime.UtcNow;
        if (_statics is null || now - _staticsAtUtc > StaticsRefresh)
        {
            _statics = LoadStatics();
            _staticsAtUtc = now;
        }
        if (_fullCharge is null || now - _fullChargeAtUtc > FullChargeRefresh)
        {
            _fullCharge = LoadFullCharge();
            _fullChargeAtUtc = now;
        }
        if (_cim is null || now - _cimAtUtc > CimRefresh)
        {
            _cim = LoadCim();
            _cimAtUtc = now;
        }
    }

    private Dictionary<string, BatteryData> LoadStatics()
    {
        var result = new Dictionary<string, BatteryData>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, row) in QueryInstances("SELECT * FROM BatteryStaticData"))
        {
            var data = new BatteryData { Id = $"WMI:{name}" };
            const DataProviderSource src = DataProviderSource.Wmi;
            if (row.GetUint("DesignedCapacity") is { } design && design != uint.MaxValue)
            {
                data.DesignedCapacityMWh = Sourced<uint?>.From(design, src);
            }
            if (row.GetString("ManufactureName") is { } mfr) data.Manufacturer = Sourced<string?>.From(mfr, src);
            if (row.GetString("DeviceName") is { } dev) data.DeviceName = Sourced<string?>.From(dev, src);
            if (row.GetString("SerialNumber") is { } sn) data.SerialNumber = Sourced<string?>.From(sn, src);
            if (row.GetString("UniqueID") is { } uid) data.DisplayName = Sourced<string?>.From(uid, src);
            if (row.GetDateTime("ManufactureDate") is { } date && date.Year is >= 1990 and <= 2100)
            {
                data.ManufactureDate = Sourced<DateTime?>.From(date, src);
            }
            result[name] = data;
        }
        foreach (var (name, row) in QueryInstances("SELECT * FROM BatteryCycleCount"))
        {
            // 0 usually means "not reported by the firmware", not a real cycle count
            if (row.GetUint("CycleCount") is { } cycles && cycles is not (0 or uint.MaxValue))
            {
                if (!result.TryGetValue(name, out var data))
                {
                    data = new BatteryData { Id = $"WMI:{name}" };
                    result[name] = data;
                }
                data.CycleCount = Sourced<int?>.From((int)cycles, DataProviderSource.Wmi);
            }
        }
        return result;
    }

    private Dictionary<string, uint?> LoadFullCharge()
    {
        var result = new Dictionary<string, uint?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, row) in QueryInstances("SELECT * FROM BatteryFullChargedCapacity"))
        {
            result[name] = row.GetUint("FullChargedCapacity") is { } f && f != uint.MaxValue ? f : null;
        }
        return result;
    }

    private Dictionary<string, (double?, int?, string?, string?, string?, long?, string?)> LoadCim()
    {
        var result = new Dictionary<string, (double?, int?, string?, string?, string?, long?, string?)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(CimNamespace, "SELECT * FROM Win32_Battery");
            foreach (var o in searcher.Get())
            {
                using (o)
                {
                    double? percent = o.GetDouble("EstimatedChargeRemaining");
                    int? designVoltage = o.GetUint("DesignVoltage") is { } dv && dv > 0 ? (int)dv : null;
                    string? chemistry = ChemistryName(o.GetUint("Chemistry"));
                    string? manufacturer = o.GetString("Manufacturer") is { Length: > 0 } m ? m : null;
                    string? serial = o.GetString("SerialNumber") is { Length: > 0 } s ? s : null;
                    uint? runtime = o.GetUint("EstimatedRunTime");
                    long? estimated = runtime is { } r && BatteryNativeHelper.IsKnownRunTime(r) ? r : null;
                    string? name = o.GetString("Name") is { Length: > 0 } n ? n : null;
                    string key = o.GetString("InstanceName") is { Length: > 0 } inst ? inst : o.GetString("Name") ?? $"CIM{result.Count}";
                    result[key] = (percent, designVoltage, chemistry, manufacturer, serial, estimated, name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Win32_Battery query failed");
        }
        return result;
    }

    private static string InstanceKey(string wmiInstanceName)
    {
        // Win32_Battery instance names and root\WMI InstanceNames differ; join on a
        // normalized prefix (first token) which matches in the common single-battery case.
        var parts = wmiInstanceName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[^1] : wmiInstanceName;
    }

    private Dictionary<string, ManagementBaseObject> QueryInstances(string query)
    {
        var result = new Dictionary<string, ManagementBaseObject>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(_scope!, new ObjectQuery(query));
            foreach (var o in searcher.Get())
            {
                string? instanceName = null;
                try
                {
                    instanceName = o["InstanceName"]?.ToString();
                }
                catch
                {
                    instanceName = null;
                }
                if (instanceName is null)
                {
                    using (o)
                    {
                    }
                    continue;
                }
                if (result.TryGetValue(instanceName, out var existing))
                {
                    using (existing)
                    using (o)
                    {
                    }
                    continue;
                }
                result[instanceName] = o;
            }
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.InvalidQuery)
        {
            // class not implemented on this machine - fine
        }
        return result;
    }

    private static string? ChemistryName(uint? code) => code switch
    {
        1 => "Other",
        2 => "Unknown",
        3 => "Lead Acid",
        4 => "NiCd",
        5 => "NiMH",
        6 => "Li-ion",
        7 => "Zinc air",
        8 => "Li-poly",
        _ => null,
    };

    private void SetError(string message)
    {
        if (LastError != message)
        {
            _logger.Debug("WMI provider: {Message}", message);
        }
        LastError = message;
        IsAvailable = false;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _scope = null;
            _statics = null;
            _fullCharge = null;
            _cim = null;
        }
    }
}

/// <summary>Defensive property readers for WMI objects; anything missing or alien becomes null.</summary>
internal static class WmiReaderExtensions
{
    public static string? GetString(this ManagementBaseObject o, string property)
    {
        try
        {
            return o[property]?.ToString() is { Length: > 0 } s ? s : null;
        }
        catch
        {
            return null;
        }
    }

    public static uint? GetUint(this ManagementBaseObject o, string property)
    {
        try
        {
            object? value = o[property];
            if (value is null) return null;
            return Convert.ToUInt64(value, CultureInfo.InvariantCulture) is { } v && v <= uint.MaxValue ? (uint)v : null;
        }
        catch
        {
            return null;
        }
    }

    public static double? GetDouble(this ManagementBaseObject o, string property)
    {
        try
        {
            object? value = o[property];
            return value is null ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    public static bool? GetBool(this ManagementBaseObject o, string property)
    {
        try
        {
            object? value = o[property];
            return value is null ? null : Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    public static DateTime? GetDateTime(this ManagementBaseObject o, string property)
    {
        try
        {
            object? value = o[property];
            if (value is null) return null;
            return ManagementDateTimeConverter.ToDateTime(value.ToString()!);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Sentinel helpers reused by the WMI provider without leaking the internal native type.</summary>
internal static class BatteryNativeHelper
{
    public static bool IsKnownVoltage(uint voltageMv) => voltageMv != 0xFFFFFFFF && voltageMv != 0;
    public static bool IsKnownRunTime(long seconds) => seconds != 0x7FFFFFFF && seconds > 0;
}
