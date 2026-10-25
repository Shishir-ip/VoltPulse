using VoltPulse.Core.Models;

namespace VoltPulse.Core.Abstractions;

/// <summary>
/// One battery data source. Implementations return partial <see cref="BatteryData"/> per battery and
/// must never throw for "unsupported" values - they return unknown fields instead.
/// </summary>
public interface IBatteryProvider : IDisposable
{
    string Name { get; }

    /// <summary>True when the last read succeeded and produced at least one battery.</summary>
    bool IsAvailable { get; }

    /// <summary>The last error message, if any (shown in diagnostics).</summary>
    string? LastError { get; }

    /// <summary>Reads all batteries this provider can see. Returns an empty list when nothing is available.</summary>
    IReadOnlyList<BatteryData> ReadAll();
}
