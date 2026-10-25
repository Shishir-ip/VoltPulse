using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoltPulse.Core.Calculations;
using VoltPulse.Core.Formatting;
using VoltPulse.Core.Models;
using VoltPulse.Providers;
using VoltPulse.Services;

namespace VoltPulse.App.ViewModels;

#pragma warning disable CA1305 // share text only

/// <summary>Drives the Live screen: big reading, pills, min/max and the key-value fact list.</summary>
public sealed partial class LiveViewModel : ObservableObject
{
    private const int WarmupSamples = 3;

    private readonly SamplerService _sampler;
    private readonly SettingsService _settings;
    private readonly MockBatteryProvider _mock;
    private readonly Serilog.ILogger _logger;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _animTimer;

    private MovingAverage _currentAverage = new(3);
    private readonly SpikeFilter _spikeFilter = new();
    private double _displayedCurrent;
    private bool _hasDisplayedCurrent;
    private double _animFrom;
    private double _animTo;
    private int _animElapsedMs;
    private double _observedMaxMa = 5000;
    private int _samplesTaken;
    private double? _sessionMin;
    private double? _sessionMax;
    private BatteryData? _lastBattery;
    private BatterySnapshot? _lastSnapshot;

    public ObservableCollection<FactItem> Facts { get; } = [];

    public ObservableCollection<PillItem> Pills { get; } =
        new(Enumerable.Range(0, 5).Select(_ => new PillItem()));

    [ObservableProperty]
    private string? _bigValue;

    [ObservableProperty]
    private string _bigUnit = "mA";

    [ObservableProperty]
    private bool _bigValueVisible;

    [ObservableProperty]
    private bool _measuring = true;

    [ObservableProperty]
    private BatteryLiveState _liveState = BatteryLiveState.Unknown;

    [ObservableProperty]
    private string _liveBrushKey = "BrushStateUnknown";

    [ObservableProperty]
    private string _pauseMenuText = "Pause logging";

    [ObservableProperty]
    private string _plugMenuText = "Simulate plug in";

    [ObservableProperty]
    private string _demoMenuText = "Enable demo mode";

    public static string OnboardingText => VoltPulse.App.Services.Strings.T("OnboardingTip");
    public static string MeasuringText => VoltPulse.App.Services.Strings.T("Measuring");

    [ObservableProperty]
    private string _minText = ValueFormatter.Measuring;

    [ObservableProperty]
    private string _maxText = ValueFormatter.Measuring;

    [ObservableProperty]
    private bool _onboardingVisible;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _noBattery;

    public bool HasMock => _mock is not null;

    public LiveViewModel(SamplerService sampler, SettingsService settings, MockBatteryProvider mock, Serilog.ILogger logger)
    {
        _sampler = sampler;
        _settings = settings;
        _mock = mock;
        _logger = logger.ForContext<LiveViewModel>();
        _dispatcher = Dispatcher.CurrentDispatcher;

        OnboardingVisible = !settings.Current.OnboardingShown;
        ApplySmoothingWindow();

        _animTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _animTimer.Tick += AnimTick;

        _sampler.SnapshotReady += OnSnapshotReady;
        _settings.Changed += OnSettingsChanged;
    }

    private void OnSnapshotReady(BatterySnapshot snapshot) =>
        _dispatcher.BeginInvoke(() => ApplySnapshot(snapshot));

    private void OnSettingsChanged(AppSettings settings) =>
        _dispatcher.BeginInvoke(() =>
        {
            ApplySmoothingWindow();
            UpdateLiveBrush(settings);
            if (!settings.OnboardingShown) OnboardingVisible = true;
        });

    private void UpdateLiveBrush(AppSettings? settings = null)
    {
        settings ??= _settings.Current;
        LiveBrushKey = settings.UseStateColors
            ? LiveState switch
            {
                BatteryLiveState.Charging => "BrushStateCharging",
                BatteryLiveState.Discharging => "BrushStateDischarging",
                BatteryLiveState.Full => "BrushStateFull",
                BatteryLiveState.Critical => "BrushStateCritical",
                _ => "BrushStateUnknown",
            }
            : "BrushAccent";
    }

    private void ApplySmoothingWindow()
    {
        if (_currentAverage.Window != _settings.Current.SmoothingSamples)
        {
            _currentAverage = new MovingAverage(_settings.Current.SmoothingSamples);
        }
    }

    private void ApplySnapshot(BatterySnapshot snapshot)
    {
        try
        {
            var settings = _settings.Current;
            var battery = snapshot.Combined ?? (snapshot.Batteries.Count > 0 ? snapshot.Batteries[0] : null);
            if (battery is null)
            {
                NoBattery = true;
                Measuring = false;
                BigValue = "No battery";
                BigUnit = string.Empty;
                BigValueVisible = true;
                return;
            }
            NoBattery = false;
            _lastBattery = battery;
            _lastSnapshot = snapshot;

            LiveState = battery.LiveState();
            UpdateLiveBrush(settings);

            double? raw = battery.CurrentMilliamps(settings.InvertSignConvention);
            double? filtered = raw is null ? null : _spikeFilter.Push(raw);
            double? smoothed = _currentAverage.Push(filtered);

            if (smoothed is null)
            {
                // No rate available: idle-on-AC reads as "Idle", otherwise measure briefly then give up.
                if (battery.ChargeState.Value is ChargeState.FullyCharged or ChargeState.IdleOnAc)
                {
                    Measuring = false;
                    BigValue = "Idle";
                    BigUnit = string.Empty;
                    BigValueVisible = true;
                }
                else if (_samplesTaken < WarmupSamples)
                {
                    Measuring = true;
                }
                else
                {
                    Measuring = false;
                    BigValue = ValueFormatter.NotSupported;
                    BigUnit = string.Empty;
                    BigValueVisible = true;
                }
            }
            else
            {
                _samplesTaken++;
                Measuring = _samplesTaken < WarmupSamples;
                AnimateCurrentTo(smoothed.Value);
                UpdateMinMax(smoothed.Value);
                UpdatePills(Math.Abs(smoothed.Value));
            }

            UpdateFacts(battery, snapshot, settings);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to apply snapshot");
        }
    }

    private void AnimateCurrentTo(double target)
    {
        _animFrom = _hasDisplayedCurrent ? _displayedCurrent : target;
        _animTo = target;
        _animElapsedMs = 0;
        if (!_hasDisplayedCurrent)
        {
            _displayedCurrent = target;
            _hasDisplayedCurrent = true;
        }
        _animTimer.Start();
    }

    private void AnimTick(object? sender, EventArgs e)
    {
        _animElapsedMs += 16;
        double t = Math.Min(1, _animElapsedMs / 300.0);
        double ease = 1 - Math.Pow(1 - t, 3);
        _displayedCurrent = _animFrom + (_animTo - _animFrom) * ease;

        var (value, unit) = FormatBig(_displayedCurrent);
        BigValue = value;
        BigUnit = unit;
        BigValueVisible = true;

        if (t >= 1)
        {
            _animTimer.Stop();
        }
    }

    private static (string Value, string Unit) FormatBig(double ma)
    {
        double abs = Math.Abs(ma);
        return abs >= 1000
            ? ((ma / 1000.0).ToString("0.00", CultureInfo.InvariantCulture), "A")
            : (ma.ToString("0", CultureInfo.InvariantCulture), "mA");
    }

    private static string FormatBigFull(double ma)
    {
        var (value, unit) = FormatBig(ma);
        return string.Create(CultureInfo.InvariantCulture, $"{value} {unit}");
    }

    private void UpdateMinMax(double current)
    {
        _sessionMin = _sessionMin is null ? current : Math.Min(_sessionMin.Value, current);
        _sessionMax = _sessionMax is null ? current : Math.Max(_sessionMax.Value, current);
        MinText = FormatBigFull(_sessionMin.Value);
        MaxText = FormatBigFull(_sessionMax.Value);
    }

    private void UpdatePills(double absCurrent)
    {
        _observedMaxMa = Math.Max(_observedMaxMa, absCurrent);
        double range = Math.Max(_observedMaxMa, 5000);
        double fill = Math.Clamp(absCurrent / range, 0, 1) * Pills.Count;
        for (int i = 0; i < Pills.Count; i++)
        {
            Pills[i].Fill = Math.Clamp(fill - i, 0, 1);
        }
    }

    // ----- facts -----

    private void UpdateFacts(BatteryData b, BatterySnapshot snapshot, AppSettings settings)
    {
        var state = b.ChargeState.Value ?? ChargeState.Unknown;
        string status = state switch
        {
            ChargeState.Charging => b.Critical.Value == true ? "Charging (critical)" : "Charging",
            ChargeState.Discharging => b.Critical.Value == true ? "Discharging (critical)" : "Discharging",
            ChargeState.FullyCharged => "Fully charged",
            ChargeState.IdleOnAc => "Idle (on AC)",
            _ => ValueFormatter.NotSupported,
        };
        if (b.AcOnline.Value == false && state is ChargeState.Unknown)
        {
            status = "Discharging";
        }

        bool fahrenheit = settings.UseFahrenheit;

        SetFact("Status", status, b.ChargeState.Source,
            "Whether the battery is charging, discharging or idle right now.");
        SetFact("Plugged", b.AcOnline.Value switch
        {
            true => "AC power",
            false => "On battery",
            null => ValueFormatter.NotSupported,
        }, b.AcOnline.Source, "Whether the charger is connected.");
        SetFact("Level", ValueFormatter.Percent(b.Percent.Value), b.Percent.Source,
            "Remaining charge as reported by Windows.");
        SetFact("Health", ValueFormatter.Health(b.HealthPercent()), DataProviderSource.Derived,
            "Full-charge capacity compared with the factory design capacity. This is how much of the original capacity the battery can still hold.");
        SetFact("Cycle count", ValueFormatter.CycleCount(b.CycleCount.Value, b.CycleCountEstimated), b.CycleCountEstimated ? DataProviderSource.Derived : b.CycleCount.Source,
            "Number of complete charge cycles counted for this battery.");
        SetFact("Technology", ComposeTechnology(b), b.Chemistry.Source,
            "Battery chemistry and technology as reported by the firmware.");
        SetFact("Design capacity", ComposeCapacity(b.DesignedCapacityMWh.Value, b), b.DesignedCapacityMWh.Source,
            "The capacity the battery was designed for at the factory.");
        SetFact("Full-charge capacity", ComposeCapacity(b.FullChargeCapacityMWh.Value, b), b.FullChargeCapacityMWh.Source,
            "How much energy the battery can currently hold when fully charged.");
        SetFact("Current capacity", ComposeCapacity(b.CurrentCapacityMWh.Value, b), b.CurrentCapacityMWh.Source,
            "Energy stored in the battery right now.");
        SetFact("Temperature", ValueFormatter.Temperature(b.TemperatureCelsius.Value, fahrenheit), b.TemperatureCelsius.Source,
            "Battery temperature. Only shown if the hardware exposes a sensor.");
        SetFact("Voltage", ValueFormatter.Voltage(b.VoltageMillivolts.Value), b.VoltageMillivolts.Source,
            "Battery terminal voltage.");
        SetFact("Power", ValueFormatter.Power(b.PowerMilliwatts()), b.RateMilliwatts.Source,
            "Charging (positive) or discharging (negative) power right now.");
        SetFact("Charge speed", ValueFormatter.SignedPercentPerHour(b.PercentPerHour()), DataProviderSource.Derived,
            "How fast the charge level changes, in percent per hour, from the smoothed current reading.");
        SetFact("Time to full / empty", TimeEstimate(b, settings), DataProviderSource.Derived,
            "Estimated from the smoothed charge rate. The tooltip shows the Windows estimate for comparison.");
        SetFact("Manufacturer", b.Manufacturer.Value ?? ValueFormatter.NotSupported, b.Manufacturer.Source,
            "Battery manufacturer reported by the firmware.");
        SetFact("Device name", b.DeviceName.Value ?? ValueFormatter.NotSupported, b.DeviceName.Source,
            "Battery model name reported by the firmware.");
        SetFact("Serial number", b.SerialNumber.Value ?? ValueFormatter.NotSupported, b.SerialNumber.Source,
            "Battery serial number. Never included in shared snapshots unless you opt in.");
        SetFact("Manufacture date", ValueFormatter.Date(b.ManufactureDate.Value), b.ManufactureDate.Source,
            "When the battery was manufactured, if the firmware reports it.");
        SetFact("PC model", ComposeSystem(snapshot.System), DataProviderSource.Derived,
            "Your computer's manufacturer and model.");
        SetFact("Windows", ComposeOs(snapshot.System), DataProviderSource.Derived,
            "Windows edition and build number.");
        SetFact("Power plan", snapshot.System.ActivePowerPlan ?? ValueFormatter.NotSupported, DataProviderSource.Derived,
            "Currently active Windows power plan.");

        if (settings.ShowRawValues)
        {
            SetFact("Rate (raw)", b.RateMilliwatts.Value is { } r ? $"{r} mW" : ValueFormatter.NotSupported, b.RateMilliwatts.Source,
                "The unmodified rate value as reported by the driver, in mW (signed).");
            SetFact("Voltage (raw)", b.VoltageMillivolts.Value is { } v ? $"{v} mV" : ValueFormatter.NotSupported, b.VoltageMillivolts.Source,
                "The unmodified voltage value as reported by the driver, in mV.");
        }
        else
        {
            RemoveFact("Rate (raw)");
            RemoveFact("Voltage (raw)");
        }
    }

    private void RemoveFact(string label)
    {
        for (int i = Facts.Count - 1; i >= 0; i--)
        {
            if (Facts[i].Label == label)
            {
                Facts.RemoveAt(i);
            }
        }
    }

    private void SetFact(string label, string value, DataProviderSource source, string explanation)
    {
        var fact = EnsureFact(label, explanation);
        fact.Value = value;
        fact.Source = SourceName(source);
    }

    private FactItem EnsureFact(string label, string explanation)
    {
        var existing = Facts.FirstOrDefault(f => f.Label == label);
        if (existing is not null) return existing;
        var item = new FactItem { Label = label, Explanation = explanation };
        Facts.Add(item);
        return item;
    }

    public static string SourceName(DataProviderSource source) => source switch
    {
        DataProviderSource.Ioctl => "Windows battery driver",
        DataProviderSource.Wmi => "WMI",
        DataProviderSource.Win32Power => "Windows power API",
        DataProviderSource.PowerCfg => "powercfg",
        DataProviderSource.Derived => "Calculated by VoltPulse",
        DataProviderSource.Mock => "Demo mode",
        _ => "Unknown source",
    };

    private static string ComposeTechnology(BatteryData b)
    {
        string? chemistry = b.Chemistry.Value;
        string? tech = b.Technology.Value;
        if (chemistry is null && tech is null) return ValueFormatter.NotSupported;
        return string.Join(" · ", new[] { chemistry, tech }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static string ComposeCapacity(uint? mwh, BatteryData b)
    {
        if (mwh is not > 0) return ValueFormatter.NotSupported;
        string baseText = string.Create(CultureInfo.InvariantCulture, $"{mwh.Value:N0} mWh");
        if (b.DesignedVoltageMillivolts.Value is > 0)
        {
            double? mah = BatteryMath.CapacityMah(mwh, b.DesignedVoltageMillivolts.Value);
            if (mah is > 0)
            {
                baseText += string.Create(CultureInfo.InvariantCulture, $"  ({mah.Value:N0} mAh)");
            }
        }
        return baseText;
    }

    private static string TimeEstimate(BatteryData b, AppSettings settings)
    {
        double? seconds;
        string kind;
        if (b.ChargeState.Value == ChargeState.Charging)
        {
            double? missing = b.FullChargeCapacityMWh.Value is { } full && b.Percent.Value is { } pct
                ? full * (100 - pct) / 100.0
                : null;
            double? rate = b.RateMilliwatts.Value is > 0 ? b.RateMilliwatts.Value : null;
            seconds = BatteryMath.TimeToFullSeconds(missing, rate);
            kind = "to full";
        }
        else if (b.ChargeState.Value == ChargeState.Discharging)
        {
            double? cap = b.CurrentCapacityMWh.Value is { } c
                ? c
                : b.FullChargeCapacityMWh.Value is { } full && b.Percent.Value is { } pct ? full * pct / 100.0 : null;
            double? rate = b.RateMilliwatts.Value is < 0 ? -b.RateMilliwatts.Value : null;
            seconds = BatteryMath.TimeToEmptySeconds(cap, rate);
            kind = "empty";
        }
        else
        {
            return b.ChargeState.Value == ChargeState.FullyCharged ? "Full" : ValueFormatter.NotSupported;
        }

        if (seconds is null) return ValueFormatter.NotSupported;
        return string.Create(CultureInfo.InvariantCulture, $"{ValueFormatter.Duration(seconds)} {kind}");
    }

    private static string ComposeSystem(SystemInfo system)
    {
        if (string.IsNullOrWhiteSpace(system.MachineManufacturer) && string.IsNullOrWhiteSpace(system.MachineModel))
        {
            return ValueFormatter.NotSupported;
        }
        return string.Join(" ", new[] { system.MachineManufacturer, system.MachineModel }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static string ComposeOs(SystemInfo system)
    {
        if (system.OsCaption is null) return ValueFormatter.NotSupported;
        return system.OsBuild is null ? system.OsCaption : $"{system.OsCaption} (build {system.OsBuild})";
    }

    // ----- commands -----

    [RelayCommand]
    private void ResetMinMax()
    {
        _sessionMin = null;
        _sessionMax = null;
        MinText = "—";
        MaxText = "—";
        _logger.Debug("Session min/max reset");
    }

    [RelayCommand]
    private void TogglePause()
    {
        _sampler.SetPaused(!IsPaused);
        IsPaused = !IsPaused;
        PauseMenuText = IsPaused ? "Resume logging" : "Pause logging";
    }

    [RelayCommand]
    private void TogglePlug()
    {
        bool plugged = !_mock.IsPluggedIn;
        _mock.SetPluggedIn(plugged);
        PlugMenuText = plugged ? "Simulate unplug" : "Simulate plug in";
        _sampler.ForceSampleNow();
    }

    [RelayCommand]
    private void DismissOnboarding()
    {
        OnboardingVisible = false;
        _settings.Update(s => s.OnboardingShown = true);
    }

    [RelayCommand]
    private void ToggleMiniWidget()
    {
        _settings.Update(s => s.ShowMiniWidget = !s.ShowMiniWidget);
    }

    [RelayCommand]
    private void ToggleDemo()
    {
        bool enable = !_settings.Current.DemoMode;
        _settings.Update(s => s.DemoMode = enable);
        DemoMenuText = enable ? "Disable demo mode" : "Enable demo mode";
        if (enable)
        {
            _mock.SetPluggedIn(false);
        }
        _sampler.ForceSampleNow();
    }

    /// <summary>Plain-text summary used by the Share button (serial only when opted in).</summary>
    public string BuildShareSummary()
    {
        var b = _lastBattery;
        if (b is null) return "VoltPulse: no battery detected.";
        var snapshot = _lastSnapshot;
        var settings = _settings.Current;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("VoltPulse battery snapshot");
        sb.AppendLine($"Level: {(b.Percent.Value is { } p ? $"{p:0}%" : "unknown")}  State: {b.ChargeState.Value}");
        sb.AppendLine($"Current: {Core.Formatting.ValueFormatter.Current(b.CurrentMilliamps(settings.InvertSignConvention))}");
        sb.AppendLine($"Power: {Core.Formatting.ValueFormatter.Power(b.PowerMilliwatts())}  Voltage: {Core.Formatting.ValueFormatter.Voltage(b.VoltageMillivolts.Value)}");
        sb.AppendLine($"Health: {Core.Formatting.ValueFormatter.Health(b.HealthPercent())}");
        if (b.TemperatureCelsius.Value is { } temp)
        {
            sb.AppendLine($"Temperature: {Core.Formatting.ValueFormatter.Temperature(temp, settings.UseFahrenheit)}");
        }
        sb.Append("PC: ").AppendLine(string.Join(" ", new[] { snapshot?.System.MachineManufacturer, snapshot?.System.MachineModel }.Where(s => !string.IsNullOrWhiteSpace(s))));
        sb.Append("Battery: ").AppendLine(string.Join(" ", new[] { b.Manufacturer.Value, b.DeviceName.Value }.Where(s => !string.IsNullOrWhiteSpace(s))));
        if (settings.IncludeSerialInExports && b.SerialNumber.Value is { } sn)
        {
            sb.Append("Serial: ").AppendLine(sn);
        }
        return sb.ToString().TrimEnd();
    }

    public string ExportFolder => _settings.Current.ExportFolder
        ?? (System.IO.Directory.Exists(VoltPulse.Core.Paths.AppPaths.ExportsDir)
            ? VoltPulse.Core.Paths.AppPaths.ExportsDir
            : VoltPulse.Core.Paths.AppPaths.Base);

    public sealed class FactItem : ObservableObject
    {
        public required string Label { get; init; }
        public string? Explanation { get; init; }

        private string _value = "—";
        public string Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value))
                {
                    OnPropertyChanged(nameof(CopyValue));
                    OnPropertyChanged(nameof(ToolTip));
                }
            }
        }

        public string Source { get; set; } = "Unknown source";

        public string CopyValue => _value == ValueFormatter.NotSupported ? string.Empty : _value;

        public string ToolTip =>
            Explanation is null ? $"Source: {Source}" : $"{Explanation}\n\nSource: {Source}";
    }

    public sealed class PillItem : ObservableObject
    {
        private double _fill;

        public double Fill
        {
            get => _fill;
            set
            {
                if (Math.Abs(_fill - value) > 0.001)
                {
                    SetProperty(ref _fill, value);
                }
            }
        }
    }
}
