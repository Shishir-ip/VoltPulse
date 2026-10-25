using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoltPulse.Core.Models;
using VoltPulse.App.Services;
using VoltPulse.Services;

namespace VoltPulse.App.ViewModels;

/// <summary>Health screen: ring value, capacity bars, verdict, cycle count and long-term fade chart.</summary>
public sealed partial class HealthViewModel : ObservableObject
{
    private readonly SamplerService _sampler;
    private readonly SettingsService _settings;
    private readonly BatteryReportImportService _importService;
    private readonly Dispatcher _dispatcher;

    private PowerCfgReportData? _report;
    private double? _healthPercent;

    [ObservableProperty]
    private string _healthText = "—";

    [ObservableProperty]
    private string _verdict = "Measuring...";

    [ObservableProperty]
    private string _verdictDetail = "";

    [ObservableProperty]
    private double _barHealthWidth;    // 0..1 fraction of design capacity

    [ObservableProperty]
    private string _wearText = "—";

    [ObservableProperty]
    private string _designCapacityText = "—";

    [ObservableProperty]
    private string _fullChargeCapacityText = "—";

    [ObservableProperty]
    private string _cycleCountText = "—";

    [ObservableProperty]
    private string _fadeSource = "";

    [ObservableProperty]
    private bool _hasFadeData;

    [ObservableProperty]
    private bool _isImporting;

    public event Action<double[], string>? FadeChartUpdated; // values, tooltip note

    public HealthViewModel(SamplerService sampler, SettingsService settings, BatteryReportImportService importService, Serilog.ILogger logger)
    {
        _sampler = sampler;
        _settings = settings;
        _importService = importService;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _sampler.SnapshotReady += OnSnapshot;
        _importService.ImportCompleted += OnImportCompleted;
        // If a report is already cached from an earlier import, render it as soon as the view subscribes.
        if (_report is not null)
        {
            PublishFade();
        }
    }

    /// <summary>Called by the view when the Health page becomes visible.</summary>
    public void Activate() => _importService.EnsureFresh(force: false);

    private void OnSnapshot(BatterySnapshot snapshot) =>
        _dispatcher.BeginInvoke(() =>
        {
            var battery = snapshot.Combined ?? (snapshot.Batteries.Count > 0 ? snapshot.Batteries[0] : null);
            if (battery is null) return;
            _healthPercent = battery.HealthPercent();
            HealthText = _healthPercent is null ? "—" : $"{_healthPercent:0.0}%";

            (string verdict, string detail) = _healthPercent switch
            {
                null => ("Unknown", "This device does not expose design and full-charge capacity, so health cannot be computed."),
                >= 80 => ("Healthy", "Health = full-charge capacity ÷ design capacity × 100. At 80% or above the battery is in good shape."),
                >= 60 => ("Fair", "Health = full-charge capacity ÷ design capacity × 100. Between 60% and 80% the battery holds noticeably less charge than new."),
                _ => ("Replace soon", "Health = full-charge capacity ÷ design capacity × 100. Below 60% the battery is worn and should be replaced."),
            };
            Verdict = verdict;
            VerdictDetail = detail;

            BarHealthWidth = Math.Clamp((_healthPercent ?? 0) / 100.0, 0, 1);
            WearText = battery.WearPercent() is { } wear ? $"{wear:0.0}%" : "—";
            DesignCapacityText = ValueFormatterHelper.Capacity(battery.DesignedCapacityMWh.Value);
            FullChargeCapacityText = ValueFormatterHelper.Capacity(battery.FullChargeCapacityMWh.Value);
            CycleCountText = battery.CycleCount.Value is { } cycles ? $"{cycles} (from firmware)" : "Not supported by this device";
        });

    private void OnImportCompleted(PowerCfgReportData report) =>
        _dispatcher.BeginInvoke(() =>
        {
            IsImporting = false;
            _report = report;
            PublishFade();
        });

    [RelayCommand]
    private void ImportReport()
    {
        if (IsImporting) return;
        IsImporting = true;
        _importService.EnsureFresh(force: true);
    }

    private void PublishFade()
    {
        var report = _report;
        if (report is null || report.CapacityHistory.Count < 2)
        {
            HasFadeData = false;
            return;
        }
        HasFadeData = true;
        var values = report.CapacityHistory.Select(e => (double)e.FullChargeCapacity).ToArray();
        var first = report.CapacityHistory[0];
        var last = report.CapacityHistory[^1];
        FadeSource = $"From the Windows battery report: {report.CapacityHistory.Count} weekly entries, " +
                     $"{first.FullChargeCapacity:N0} mWh on {first.Date:yyyy-MM-dd} -> {last.FullChargeCapacity:N0} mWh on {last.Date:yyyy-MM-dd}.";
        FadeChartUpdated?.Invoke(values, "weeks before now");
    }
}

internal static class ValueFormatterHelper
{
    public static string Capacity(uint? mwh) => mwh is > 0 ? $"{mwh:N0} mWh" : "Not supported by this device";
}
