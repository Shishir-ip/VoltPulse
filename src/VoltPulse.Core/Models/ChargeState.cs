namespace VoltPulse.Core.Models;

/// <summary>High-level charge state of a battery.</summary>
public enum ChargeState
{
    Unknown,
    Discharging,
    Charging,
    FullyCharged,
    /// <summary>Plugged in, not charging, not full (e.g. rate 0 mW while plugged in below 100%).</summary>
    IdleOnAc,
}

/// <summary>Drives the app's state colors: discharging orange, charging teal, full green, critical red.</summary>
public enum BatteryLiveState
{
    Unknown,
    Discharging,
    Charging,
    Full,
    Critical,
}
