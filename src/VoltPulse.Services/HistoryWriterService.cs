using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Models;

namespace VoltPulse.Services;

/// <summary>
/// Bridges the sampler to the history store: converts snapshots into samples, batches writes
/// (every <see cref="FlushIntervalSeconds"/> seconds), records sessions, and keeps the retention
/// policy (full resolution for N days, hourly aggregates for a year).
/// </summary>
public sealed class HistoryWriterService : IHostedService, IDisposable
{
    private const int FlushIntervalSeconds = 5;
    private static readonly TimeSpan RetentionCheckInterval = TimeSpan.FromHours(6);

    private readonly SamplerService _sampler;
    private readonly IHistoryStore _store;
    private readonly SettingsService _settings;
    private readonly Serilog.ILogger _logger;
    private readonly SessionTracker _sessions = new();
    private readonly System.Threading.Timer _flushTimer;
    private readonly System.Threading.Timer _retentionTimer;

    private List<BatterySample> _pending = [];

    public HistoryWriterService(SamplerService sampler, IHistoryStore store, SettingsService settings, Serilog.ILogger logger)
    {
        _sampler = sampler;
        _store = store;
        _settings = settings;
        _logger = logger.ForContext<HistoryWriterService>();
        _flushTimer = new System.Threading.Timer(_ => Flush(), null, TimeSpan.FromSeconds(FlushIntervalSeconds), TimeSpan.FromSeconds(FlushIntervalSeconds));
        _retentionTimer = new System.Threading.Timer(_ => ApplyRetention(), null, RetentionCheckInterval, RetentionCheckInterval);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sampler.SnapshotReady += OnSnapshot;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sampler.SnapshotReady -= OnSnapshot;
        var open = _sessions.FlushOpen();
        if (open is not null)
        {
            _store.UpsertSession(open);
        }
        Flush();
        return Task.CompletedTask;
    }

    private void OnSnapshot(BatterySnapshot snapshot)
    {
        if (!_settings.Current.LogToDatabase) return;
        if (!snapshot.HasBattery) return;

        var now = snapshot.RetrievedAtUtc;
        var samples = new List<BatterySample>(snapshot.Batteries.Count);
        foreach (var b in snapshot.Batteries)
        {
            samples.Add(new BatterySample(
                now,
                b.Id,
                b.Percent.Value,
                b.VoltageMillivolts.Value,
                b.RateMilliwatts.Value,
                b.CurrentMilliamps(),
                b.PowerMilliwatts(),
                b.TemperatureCelsius.Value,
                b.ChargeState.Value ?? ChargeState.Unknown));
        }

        lock (_pending)
        {
            _pending.AddRange(samples);
        }

        var completed = _sessions.OnSample(
            now,
            snapshot.Combined?.ChargeState.Value ?? ChargeState.Unknown,
            snapshot.Combined?.Percent.Value,
            snapshot.Combined?.RateMilliwatts.Value,
            snapshot.Combined?.CurrentMilliamps());
        if (completed is not null)
        {
            _store.UpsertSession(completed);
            _logger.Information("Session recorded: {Kind} {Start} - {End}, {Pct} -> {Pct2}%",
                completed.Kind, completed.StartUtc, completed.EndUtc, completed.StartPercent, completed.EndPercent);
        }
    }

    /// <summary>The session currently in progress, if any (live view of the tracker).</summary>
    public SessionRecord? OpenSession => _sessions.OpenSession;

    public void Flush()
    {
        List<BatterySample> toWrite;
        lock (_pending)
        {
            if (_pending.Count == 0)
            {
                PersistOpenSession();
                return;
            }
            toWrite = _pending;
            _pending = [];
        }
        try
        {
            _store.AddSamples(toWrite);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to flush {Count} samples", toWrite.Count);
        }
        PersistOpenSession();
    }

    /// <summary>
    /// Keeps the in-progress session in the database (inserted once, then updated) so it shows in
    /// the Sessions page while it is running and survives a crash. Short sessions are never
    /// persisted, matching the completion threshold.
    /// </summary>
    private void PersistOpenSession()
    {
        var open = _sessions.OpenSession;
        if (open is null || open.EndUtc is null) return;
        if ((open.EndUtc.Value - open.StartUtc).TotalSeconds < 30) return;
        try
        {
            _store.UpsertSession(open);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Persisting the in-progress session failed");
        }
    }

    private void ApplyRetention()
    {
        try
        {
            var settings = _settings.Current;
            _store.Downsample(DateTime.UtcNow - TimeSpan.FromDays(settings.FullResolutionDays));
            _store.PruneOlderThan(DateTime.UtcNow - TimeSpan.FromDays(settings.DownsampledDays));
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Retention pass failed");
        }
    }

    public void Dispose()
    {
        _flushTimer.Dispose();
        _retentionTimer.Dispose();
        GC.SuppressFinalize(this);
    }
}
