using VoltPulse.Core.Models;

namespace VoltPulse.Core.Abstractions;

/// <summary>Persistent history of battery samples and sessions.</summary>
public interface IHistoryStore
{
    void AddSamples(IReadOnlyList<BatterySample> samples);

    IReadOnlyList<BatterySample> Query(DateTime fromUtc, DateTime toUtc, string? batteryId = null);

    /// <summary>Hourly aggregates for long time ranges (see retention policy).</summary>
    IReadOnlyList<BatterySample> QueryHourly(DateTime fromUtc, DateTime toUtc, string? batteryId = null);

    long SampleCount { get; }

    void ClearHistory();

    void UpsertSession(SessionRecord session);

    IReadOnlyList<SessionRecord> GetSessions(int limit = 200, int offset = 0);

    /// <summary>Folds full-resolution samples older than the cutoff into hourly aggregates.</summary>
    void Downsample(DateTime olderThanUtc);

    /// <summary>Deletes hourly aggregates and sessions older than the cutoff.</summary>
    void PruneOlderThan(DateTime cutoffUtc);
}
