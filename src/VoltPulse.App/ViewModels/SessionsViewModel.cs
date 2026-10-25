using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Formatting;
using VoltPulse.Core.Models;
using VoltPulse.App.Services;
using VoltPulse.Services;

namespace VoltPulse.App.ViewModels;

public sealed partial class SessionRow : ObservableObject
{
    public required SessionRecord Session { get; init; }
    public bool IsOngoing { get; init; }

    public string Kind => (Session.Kind == ChargeState.Charging ? "Charging" : "Discharging")
        + (IsOngoing ? " (ongoing)" : "");

    public string Start => Session.StartUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    public string Duration => ValueFormatter.Duration((Session.EndUtc ?? DateTime.UtcNow) - Session.StartUtc);

    public string PercentChange => (Session.StartPercent is { } sp && Session.EndPercent is { } ep)
        ? $"{sp:0}% → {ep:0}%  ({ep - sp:+0;-0})"
        : "—";

    public string AverageCurrent => Session.AvgCurrentMa is null ? "—" : ValueFormatter.Current(Session.AvgCurrentMa);

    public string PeakCurrent => Session.PeakCurrentMa is null ? "—" : ValueFormatter.Current(Session.PeakCurrentMa);

    public string Energy => Session.EnergyMWh is null ? "—" : $"{Session.EnergyMWh:N0} mWh";
}

public sealed partial class SessionsViewModel : ObservableObject
{
    private readonly IHistoryStore _store;
    private readonly HistoryWriterService _writer;
    private readonly BatteryReportImportService _importService;
    private readonly Dispatcher _dispatcher;
    private readonly System.Windows.Threading.DispatcherTimer _autoRefreshTimer;

    public System.Collections.ObjectModel.ObservableCollection<SessionRow> Sessions { get; } = [];

    [ObservableProperty]
    private SessionRow? _selectedSession;

    [ObservableProperty]
    private string _summary = "No sessions recorded yet. They appear automatically when you charge or discharge the battery.";

    public event Action<SessionRecord?>? SessionGraphRequested;

    public SessionsViewModel(IHistoryStore store, HistoryWriterService writer, BatteryReportImportService importService)
    {
        _store = store;
        _writer = writer;
        _importService = importService;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _autoRefreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10),
        };
        _autoRefreshTimer.Tick += (_, _) => Refresh();
        _importService.ImportCompleted += _ => RefreshFromAnyThread();
    }

    public void Activate()
    {
        _importService.EnsureFresh(force: false);
        Refresh();
        _autoRefreshTimer.Start();
    }

    public void Deactivate() => _autoRefreshTimer.Stop();

    public void RefreshFromAnyThread() => _dispatcher.BeginInvoke(Refresh);

    [RelayCommand]
    private void Refresh()
    {
        var open = _writer.OpenSession;
        var sessions = _store.GetSessions(200).ToList();
        if (open is null)
        {
            Sessions.Clear();
            foreach (var s in sessions)
            {
                Sessions.Add(new SessionRow { Session = s });
            }
        }
        else
        {
            // Show the in-progress session at the top (the store row is skipped to avoid duplicates).
            Sessions.Clear();
            if (open.Id is null || sessions.All(s => s.Id != open.Id))
            {
                Sessions.Add(new SessionRow { Session = open, IsOngoing = true });
            }
            foreach (var s in sessions)
            {
                Sessions.Add(new SessionRow { Session = s, IsOngoing = open.Id is not null && s.Id == open.Id });
            }
        }
        Summary = Sessions.Count == 0
            ? "No sessions recorded yet. They appear automatically when you charge or discharge the battery; " +
              "older sessions are backfilled from the Windows battery report."
            : $"{Sessions.Count} session(s). Click one to see its graph.";
    }

    partial void OnSelectedSessionChanged(SessionRow? value)
    {
        SessionGraphRequested?.Invoke(value?.Session);
    }

    [RelayCommand]
    private void ClearHistory()
    {
        _store.ClearHistory();
        Sessions.Clear();
        Summary = "History cleared.";
        SessionGraphRequested?.Invoke(null);
    }


}
