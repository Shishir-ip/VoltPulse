namespace VoltPulse.Core.Models;

/// <summary>One row of battery telemetry, as persisted to history.</summary>
public sealed record BatterySample(
    DateTime TimestampUtc,
    string BatteryId,
    double? Percent,
    int? VoltageMv,
    int? RateMw,
    double? CurrentMa,
    double? PowerMw,
    double? TempC,
    ChargeState State);

/// <summary>A detected charge or discharge session.</summary>
public sealed class SessionRecord
{
    public long? Id { get; set; }
    public string BatteryId { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public ChargeState Kind { get; set; }
    public double? StartPercent { get; set; }
    public double? EndPercent { get; set; }
    public double? AvgCurrentMa { get; set; }
    public double? PeakCurrentMa { get; set; }
    /// <summary>Total absolute energy moved in mWh (charged or discharged).</summary>
    public double? EnergyMWh { get; set; }
    public int SampleCount { get; set; }
}
