namespace VoltPulse.Core.Models;

/// <summary>Static machine information shown on the Live screen and in reports.</summary>
public sealed class SystemInfo
{
    public string? MachineManufacturer { get; set; }
    public string? MachineModel { get; set; }
    public string? OsCaption { get; set; }
    public string? OsVersion { get; set; }
    public string? OsBuild { get; set; }
    public string? ActivePowerPlan { get; set; }
}

/// <summary>Reports whether a provider worked on the last read (for the diagnostics page).</summary>
public sealed record ProviderStatusInfo(string Name, bool Available, string? LastError);

/// <summary>One merged, timestamped view of every battery in the system.</summary>
public sealed class BatterySnapshot
{
    public DateTime RetrievedAtUtc { get; init; } = DateTime.UtcNow;
    public IReadOnlyList<BatteryData> Batteries { get; init; } = [];
    /// <summary>Combined view across all batteries (capacities summed; null when no battery exists).</summary>
    public BatteryData? Combined { get; init; }
    public SystemInfo System { get; init; } = new();
    public IReadOnlyList<ProviderStatusInfo> ProviderStatuses { get; init; } = [];

    public bool HasBattery => Batteries.Count > 0;
}
