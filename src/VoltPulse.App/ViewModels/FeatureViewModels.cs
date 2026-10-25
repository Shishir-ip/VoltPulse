using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Formatting;
using VoltPulse.Core.Models;
using VoltPulse.Core.Paths;
using VoltPulse.Providers;
using VoltPulse.Services;

namespace VoltPulse.App.ViewModels;

#pragma warning disable CA1305 // status text only

public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly IHistoryStore _store;
    private readonly SettingsService _settings;
    private readonly PowerCfgReportImporter _importer;
    private readonly IAggregatedBatteryProvider _aggregated;
    private readonly Serilog.ILogger _logger;

    [ObservableProperty]
    private string _status = "Export your history as CSV or JSON, or build a full HTML/PDF report.";

    [ObservableProperty]
    private string _powerCfgSummary = "";

    public ReportsViewModel(IHistoryStore store, SettingsService settings, PowerCfgReportImporter importer, IAggregatedBatteryProvider aggregated, Serilog.ILogger logger)
    {
        _store = store;
        _settings = settings;
        _importer = importer;
        _aggregated = aggregated;
        _logger = logger.ForContext<ReportsViewModel>();
    }

    private string ExportDir
    {
        get
        {
            try
            {
                return _settings.Current.ExportFolder is { } f && Directory.Exists(f) ? f : AppPaths.ExportsDir;
            }
            catch
            {
                return AppPaths.ExportsDir;
            }
        }
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var from = DateTime.UtcNow - TimeSpan.FromDays(_settings.Current.FullResolutionDays);
        var samples = _store.Query(from, DateTime.UtcNow);
        if (samples.Count == 0)
        {
            Status = "No history recorded yet.";
            return;
        }
        string path = Path.Combine(ExportDir, $"voltpulse-history-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        try
        {
            ExportService.ExportCsv(samples, path);
            Status = $"Exported {samples.Count:N0} samples to {path}";
        }
        catch (Exception ex)
        {
            Status = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ExportJson()
    {
        var from = DateTime.UtcNow - TimeSpan.FromDays(_settings.Current.FullResolutionDays);
        var samples = _store.Query(from, DateTime.UtcNow);
        if (samples.Count == 0)
        {
            Status = "No history recorded yet.";
            return;
        }
        string path = Path.Combine(ExportDir, $"voltpulse-history-{DateTime.Now:yyyyMMdd-HHmm}.json");
        try
        {
            ExportService.ExportJson(samples, path);
            Status = $"Exported {samples.Count:N0} samples to {path}";
        }
        catch (Exception ex)
        {
            Status = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ExportHtml()
    {
        ExportReport(ExportService.ExportHtml, "html");
    }

    [RelayCommand]
    private void ExportPdf()
    {
        ExportReport(ExportService.ExportPdf, "pdf");
    }

    private void ExportReport(Action<ReportData, string> writer, string extension)
    {
        try
        {
            var report = BuildReport();
            string path = Path.Combine(ExportDir, $"voltpulse-report-{DateTime.Now:yyyyMMdd-HHmm}.{extension}");
            writer(report, path);
            Status = $"Report saved to {path}";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Report export failed");
            Status = $"Report failed: {ex.Message}";
        }
    }

    private ReportData BuildReport()
    {
        var snapshot = _aggregated.ReadSnapshot();
        var battery = snapshot.Combined ?? (snapshot.Batteries.Count > 0 ? snapshot.Batteries[0] : null);
        var from = DateTime.UtcNow - TimeSpan.FromHours(24);
        return new ReportData
        {
            System = snapshot.System,
            BatteryId = battery?.Id,
            Manufacturer = battery?.Manufacturer.Value,
            DeviceName = battery?.DeviceName.Value,
            SerialNumber = battery?.SerialNumber.Value,
            Chemistry = battery?.Chemistry.Value,
            HealthPercent = battery?.HealthPercent(),
            DesignCapacityMWh = battery?.DesignedCapacityMWh.Value,
            FullChargeCapacityMWh = battery?.FullChargeCapacityMWh.Value,
            CycleCount = battery?.CycleCount.Value,
            Percent = battery?.Percent.Value,
            State = battery?.ChargeState.Value ?? ChargeState.Unknown,
            CurrentMa = battery?.CurrentMilliamps(_settings.Current.InvertSignConvention),
            VoltageMv = battery?.VoltageMillivolts.Value,
            TemperatureC = battery?.TemperatureCelsius.Value,
            Sessions = [.. _store.GetSessions(50)],
            ChartSamples = [.. _store.Query(from, DateTime.UtcNow, battery?.Id)],
            IncludeSerial = _settings.Current.IncludeSerialInExports,
        };
    }

    [RelayCommand]
    private void ImportPowerCfgReport()
    {
        PowerCfgSummary = "Running powercfg (takes a few seconds)...";
        Task.Run(() =>
        {
            var data = _importer.Import();
            _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (data is null)
                {
                    PowerCfgSummary = "powercfg could not produce a battery report on this machine.";
                    return;
                }
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Battery: {data.Manufacturer} {data.BatteryId} ({data.Chemistry ?? "chemistry unknown"})");
                sb.AppendLine($"Design capacity: {data.DesignCapacity:N0} mWh   Full-charge: {data.FullChargeCapacity:N0} mWh   Cycles: {data.CycleCount}");
                if (data.CapacityHistory.Count > 0)
                {
                    var first = data.CapacityHistory[0];
                    var last = data.CapacityHistory[^1];
                    sb.AppendLine($"Capacity history: {first.FullChargeCapacity:N0} mWh on {first.Date:yyyy-MM-dd} -> {last.FullChargeCapacity:N0} mWh on {last.Date:yyyy-MM-dd} ({data.CapacityHistory.Count} entries)");
                }
                else
                {
                    sb.AppendLine("No capacity history entries in the report.");
                }
                PowerCfgSummary = sb.ToString();
            });
        });
    }

    [RelayCommand]
    private void OpenExportsFolder()
    {
        try
        {
            Directory.CreateDirectory(ExportDir);
            using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{ExportDir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }
}

public sealed partial class InsightsViewModel : ObservableObject
{
    private readonly IHistoryStore _store;
    private readonly Dispatcher _dispatcher;

    [ObservableProperty]
    private string _scoreText = "—";

    [ObservableProperty]
    private string _chargeToFullText = "—";

    [ObservableProperty]
    private string _timeAtFullText = "—";

    [ObservableProperty]
    private string _deepDischargeText = "—";

    [ObservableProperty]
    private string _avgDepthText = "—";

    public System.Collections.ObjectModel.ObservableCollection<string> Tips { get; } = [];

    public InsightsViewModel(IHistoryStore store)
    {
        _store = store;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public void Activate() => Refresh();

    [RelayCommand]
    private void Refresh()
    {
        Task.Run(() =>
        {
            var window = TimeSpan.FromDays(7);
            var from = DateTime.UtcNow - window;
            var sessions = _store.GetSessions(500).Where(s => s.StartUtc >= from).ToList();
            var samples = _store.Query(from, DateTime.UtcNow);
            var insights = InsightsCalculator.Compute(sessions, samples, window);
            _ = _dispatcher.BeginInvoke(() =>
            {
                ScoreText = $"{insights.Score}/100";
                ChargeToFullText = insights.ChargeToFullRatio is { } r ? $"{r * 100:0}% of charge sessions" : "Not enough data";
                TimeAtFullText = insights.TimeAtFullPercent is { } t ? $"{t:0.0} h in the last 7 days" : "Not enough data";
                DeepDischargeText = insights.DeepDischarges.ToString(System.Globalization.CultureInfo.InvariantCulture);
                AvgDepthText = insights.AverageDischargeDepth is { } d ? $"{d:0}% per session" : "Not enough data";
                Tips.Clear();
                foreach (var tip in insights.Tips)
                {
                    Tips.Add(tip);
                }
            });
        });
    }
}

public sealed partial class PowerUsageViewModel : ObservableObject
{
    private readonly DispatcherTimer _timer;

    public System.Collections.ObjectModel.ObservableCollection<ProcessUsage> Processes { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _refreshLabel = "Refresh";

    public PowerUsageViewModel()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10),
        };
        _timer.Tick += (_, _) => Refresh();
    }

    public void Activate()
    {
        _timer.Start();
        Refresh();
    }

    public void Deactivate() => _timer.Stop();

    [RelayCommand]
    private void Refresh()
    {
        if (IsLoading) return;
        IsLoading = true;
        RefreshLabel = "Scanning...";
        Task.Run(async () =>
        {
            var processes = await PowerUsageScanner.ScanAsync().ConfigureAwait(false);
            _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                Processes.Clear();
                foreach (var p in processes)
                {
                    Processes.Add(p);
                }
                IsLoading = false;
                RefreshLabel = "Refresh";
            });
        });
    }
}

public sealed partial class AboutViewModel : ObservableObject
{
    private readonly IAggregatedBatteryProvider _provider;
    private readonly IEnumerable<IBatteryProvider> _providers;
    private readonly Serilog.ILogger _logger;

    public string Version { get; }
    public string Runtime { get; } = Environment.Version.ToString();
    public string Os { get; } = Environment.OSVersion.VersionString;

    [ObservableProperty]
    private string _providerStatus = "";

    [ObservableProperty]
    private string _status = "";

    public AboutViewModel(IAggregatedBatteryProvider provider, IEnumerable<IBatteryProvider> providers, Serilog.ILogger logger)
    {
        _provider = provider;
        _providers = providers;
        _logger = logger.ForContext<AboutViewModel>();
        Version = typeof(AboutViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        RefreshProviderStatus();
    }

    public void RefreshProviderStatus()
    {
        Task.Run(() =>
        {
            var snapshot = _provider.ReadSnapshot();
            var lines = snapshot.ProviderStatuses.Select(p => $"{p.Name,-26} {(p.Available ? "available" : $"unavailable{(p.LastError is null ? "" : $": {p.LastError}")}")}");
            _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() => ProviderStatus = string.Join(Environment.NewLine, lines));
        });
    }

    [RelayCommand]
    private void CopyDiagnostics()
    {
        Task.Run(() =>
        {
            string dump = Providers.DiagnosticsDump.Build(_providers, _provider);
            try
            {
                File.WriteAllText(Path.Combine(AppPaths.LogsDir, "diagnostics.txt"), dump);
            }
            catch
            {
                // best effort
            }
            _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    System.Windows.Clipboard.SetText(dump);
                    Status = "Diagnostics copied to the clipboard and saved to the logs folder.";
                }
                catch
                {
                    Status = "Diagnostics saved to the logs folder (clipboard was busy).";
                }
            });
        });
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDir);
            using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{AppPaths.LogsDir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }
}
