namespace VoltPulse.Core.Models;

public enum AlertKind
{
    BatteryLow,
    BatteryCritical,
    ChargeLimit,
    FullyCharged,
    HighTemperature,
    HighDrain,
    HealthDropped,
    ChargerConnected,
    ChargerDisconnected,
    SlowCharging,
}

public enum TemperatureUnit { Celsius, Fahrenheit }

/// <summary>Which quantity the Live screen's big reading shows.</summary>
public enum LiveUnit { Current, Watt }

public enum ThemeMode { Dark, Light, FollowWindows }

public sealed class AlertRuleSettings
{
    public AlertKind Id { get; set; }
    public bool Enabled { get; set; }
    public double Threshold { get; set; }
    /// <summary>Seconds the condition must persist before firing (used by drain/slow-charge rules).</summary>
    public int DurationSeconds { get; set; }
    public int CooldownMinutes { get; set; } = 15;
    public bool PlaySound { get; set; } = true;
}

public sealed class MiniWidgetSettings
{
    public double X { get; set; } = 80;
    public double Y { get; set; } = 80;
    public double Opacity { get; set; } = 0.85;
    public bool ClickThrough { get; set; }
}

public static class AlertDefaults
{
    public static List<AlertRuleSettings> All() =>
    [
        new() { Id = AlertKind.BatteryLow, Enabled = true, Threshold = 20, CooldownMinutes = 30 },
        new() { Id = AlertKind.BatteryCritical, Enabled = true, Threshold = 10, CooldownMinutes = 10 },
        new() { Id = AlertKind.ChargeLimit, Enabled = false, Threshold = 80, CooldownMinutes = 30 },
        new() { Id = AlertKind.FullyCharged, Enabled = true, CooldownMinutes = 60 },
        new() { Id = AlertKind.HighTemperature, Enabled = true, Threshold = 45, CooldownMinutes = 30 },
        new() { Id = AlertKind.HighDrain, Enabled = false, Threshold = 5000, DurationSeconds = 10, CooldownMinutes = 15 },
        new() { Id = AlertKind.HealthDropped, Enabled = true, Threshold = 80, CooldownMinutes = 1440 },
        new() { Id = AlertKind.ChargerConnected, Enabled = true, CooldownMinutes = 1 },
        new() { Id = AlertKind.ChargerDisconnected, Enabled = true, CooldownMinutes = 1 },
        new() { Id = AlertKind.SlowCharging, Enabled = false, Threshold = 300, DurationSeconds = 180, CooldownMinutes = 120 },
    ];
}

/// <summary>All user-configurable settings. Persisted as JSON in %APPDATA%\VoltPulse\settings.json.</summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    // Appearance
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;
    public bool UseStateColors { get; set; } = true;
    public double FontScale { get; set; } = 1.0;
    public bool UseFahrenheit { get; set; }
    public bool Use24HourClock { get; set; } = true;
    public LiveUnit LiveUnit { get; set; } = LiveUnit.Current;
    public string Language { get; set; } = "en";

    // Sampling
    public double SamplingIntervalSeconds { get; set; } = 1.0;
    public double BackgroundSamplingIntervalSeconds { get; set; } = 5.0;
    public int SmoothingSamples { get; set; } = 3;
    public bool InvertSignConvention { get; set; }
    public bool RejectSpikes { get; set; } = true;

    // Startup / shell
    public bool StartWithWindows { get; set; }
    public bool StartMinimizedToTray { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool ShowTrayIcon { get; set; } = true;
    public bool ShowMiniWidget { get; set; }
    public MiniWidgetSettings MiniWidget { get; set; } = new();

    // Data
    public int FullResolutionDays { get; set; } = 7;
    public int DownsampledDays { get; set; } = 365;
    public string? ExportFolder { get; set; }
    public bool IncludeSerialInExports { get; set; }
    public bool LogToDatabase { get; set; } = true;

    // Notifications
    public List<AlertRuleSettings> Alerts { get; set; } = AlertDefaults.All();

    // Advanced
    public string? PreferredProvider { get; set; }
    public bool ShowRawValues { get; set; }
    public bool DebugLogging { get; set; }
    public bool DemoMode { get; set; }
    public bool OnboardingShown { get; set; }

    /// <summary>Returns the effective sampling interval clamped to the supported set (0.5, 1, 2, 5, 10 s).</summary>
    public double EffectiveSamplingInterval()
    {
        double[] allowed = [0.5, 1, 2, 5, 10];
        double best = allowed[0];
        foreach (double a in allowed)
        {
            if (Math.Abs(a - SamplingIntervalSeconds) < Math.Abs(best - SamplingIntervalSeconds)) best = a;
        }
        return best;
    }
}
