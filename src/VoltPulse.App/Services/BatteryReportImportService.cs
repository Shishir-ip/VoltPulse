using System.IO;
using System.Text.Json;
using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Models;
using VoltPulse.Core.Paths;
using VoltPulse.Providers;
using VoltPulse.Services;

namespace VoltPulse.App.Services;

/// <summary>
/// Imports the Windows battery report (powercfg /batteryreport), keeps a 24-hour cache, publishes
/// the long-term capacity fade and backfills the Sessions page from the report's usage history.
/// The import runs automatically the first time the Health or Sessions page is shown.
/// </summary>
public sealed class BatteryReportImportService
{
    private static readonly TimeSpan CacheFreshness = TimeSpan.FromHours(24);

    private readonly PowerCfgReportImporter _importer;
    private readonly IHistoryStore _store;
    private readonly IAggregatedBatteryProvider _provider;
    private readonly Serilog.ILogger _logger;
    private readonly string _cacheFile = Path.Combine(AppPaths.CacheDir, "powercfg-report.json");
    private readonly object _gate = new();

    private PowerCfgReportData? _last;
    private DateTime _lastImportUtc = DateTime.MinValue;
    private bool _running;

    public BatteryReportImportService(
        PowerCfgReportImporter importer, IHistoryStore store, IAggregatedBatteryProvider provider, Serilog.ILogger logger)
    {
        _importer = importer;
        _store = store;
        _provider = provider;
        _logger = logger.ForContext<BatteryReportImportService>();
        LoadCache();
    }

    /// <summary>Raised after an import finished (also fired for cached data on late subscription).</summary>
    public event Action<PowerCfgReportData>? ImportCompleted;

    public PowerCfgReportData? Last => _last;

    /// <summary>
    /// Imports (or serves from cache) the report. Safe to call repeatedly; <paramref name="force"/>
    /// re-runs powercfg even when the cache is fresh.
    /// </summary>
    public void EnsureFresh(bool force)
    {
        lock (_gate)
        {
            if (_running) return;
            if (!force && _last is not null && DateTime.UtcNow - _lastImportUtc < CacheFreshness)
            {
                var cached = _last;
                ImportCompleted?.Invoke(cached);
                return;
            }
            _running = true;
        }

        Task.Run(() =>
        {
            try
            {
                var data = force || _last is null ? _importer.Import() : _last;
                if (data is null)
                {
                    return;
                }
                lock (_gate)
                {
                    _last = data;
                    _lastImportUtc = DateTime.UtcNow;
                }
                SaveCache(data);
                BackfillSessions(data);
                ImportCompleted?.Invoke(data);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Battery report import failed");
            }
            finally
            {
                lock (_gate) _running = false;
            }
        });
    }

    /// <summary>Rebuilds the imported sessions from the report's usage history.</summary>
    private void BackfillSessions(PowerCfgReportData data)
    {
        if (data.RecentUsage.Count == 0) return;
        try
        {
            int voltageMv = 0;
            try
            {
                var snapshot = _provider.ReadSnapshot();
                var battery = snapshot.Combined ?? (snapshot.Batteries.Count > 0 ? snapshot.Batteries[0] : null);
                voltageMv = battery?.DesignedVoltageOrCurrentVoltageMv() ?? 0;
            }
            catch
            {
                // without a voltage the backfilled rows just omit the estimated current
            }

            var sessions = PowerCfgSessionBackfill.BuildSessions(data.RecentUsage, voltageMv);
            _store.DeleteImportedSessions();
            foreach (var session in sessions)
            {
                _store.UpsertSession(session);
            }
            _logger.Information("Backfilled {Count} session(s) from the Windows battery report", sessions.Count);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Session backfill from the battery report failed");
        }
    }

    private void LoadCache()
    {
        try
        {
            if (File.Exists(_cacheFile))
            {
                var report = JsonSerializer.Deserialize<PowerCfgReportData>(File.ReadAllText(_cacheFile));
                if (report is not null && DateTime.UtcNow - report.ImportedAtUtc < CacheFreshness)
                {
                    _last = report;
                    _lastImportUtc = report.ImportedAtUtc;
                }
            }
        }
        catch
        {
            // cache is best effort
        }
    }

    private void SaveCache(PowerCfgReportData report)
    {
        try
        {
            AppPaths.EnsureDirectories();
            File.WriteAllText(_cacheFile, JsonSerializer.Serialize(report));
        }
        catch
        {
            // cache is best effort
        }
    }
}
