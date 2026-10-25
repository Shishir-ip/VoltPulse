using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Formatting;
using VoltPulse.Core.Models;

namespace VoltPulse.App.ViewModels;

public sealed partial class SessionRow : ObservableObject
{
    public required SessionRecord Session { get; init; }

    public string Kind => Session.Kind == ChargeState.Charging ? "Charging" : "Discharging";

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
    private readonly Dispatcher _dispatcher;

    public System.Collections.ObjectModel.ObservableCollection<SessionRow> Sessions { get; } = [];

    [ObservableProperty]
    private SessionRow? _selectedSession;

    [ObservableProperty]
    private string _summary = "No sessions recorded yet. They appear automatically when you charge or discharge the battery.";

    public event Action<SessionRecord?>? SessionGraphRequested;

    public SessionsViewModel(IHistoryStore store)
    {
        _store = store;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public void RefreshFromAnyThread() => _dispatcher.BeginInvoke(Refresh);

    [RelayCommand]
    private void Refresh()
    {
        var sessions = _store.GetSessions(200);
        Sessions.Clear();
        foreach (var s in sessions)
        {
            Sessions.Add(new SessionRow { Session = s });
        }
        Summary = sessions.Count == 0
            ? "No sessions recorded yet. They appear automatically when you charge or discharge the battery."
            : $"{sessions.Count} session(s) stored. Click one to see its graph.";
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
