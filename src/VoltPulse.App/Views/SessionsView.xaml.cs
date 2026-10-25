using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using VoltPulse.App.Controls;
using VoltPulse.App.ViewModels;
using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Models;

namespace VoltPulse.App.Views;

public partial class SessionsView : UserControl
{
    private IHistoryStore? _store;

    public SessionsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => OnViewModelChanged();
    }

    private SessionsViewModel? ViewModel => DataContext as SessionsViewModel;

    private void OnViewModelChanged()
    {
        if (ViewModel is not { } vm) return;
        _store ??= VoltPulse.App.App.HostServices?.GetService(typeof(IHistoryStore)) as IHistoryStore;
        vm.SessionGraphRequested += OnSessionGraphRequested;
        IsVisibleChanged += (_, e) =>
        {
            if (DataContext is not SessionsViewModel viewModel) return;
            if (e.NewValue is true) viewModel.Activate();
            else viewModel.Deactivate();
        };
    }

    private void OnSessionGraphRequested(SessionRecord? session)
    {
        if (session is null || _store is null)
        {
            SessionChart.Render([], "", "", "Select a session above to see its graph.");
            return;
        }

        var samples = _store.Query(session.StartUtc, (session.EndUtc ?? DateTime.UtcNow) + TimeSpan.FromSeconds(1), session.BatteryId);
        var current = new double[samples.Count];
        var level = new double[samples.Count];
        for (int i = 0; i < samples.Count; i++)
        {
            current[i] = samples[i].CurrentMa ?? double.NaN;
            level[i] = samples[i].Percent ?? double.NaN;
        }

        if (samples.Count < 2)
        {
            SessionChart.Render([], "", "",
                session.BatteryId == "IMPORTED"
                    ? "Raw samples are not available for sessions imported from the Windows report."
                    : "Not enough samples recorded for this session yet.");
            return;
        }

        double period = Math.Max(0.1, (samples[^1].TimestampUtc - samples[0].TimestampUtc).TotalSeconds / (samples.Count - 1));

        SessionChart.Render(
        [
            new TimeSeriesChart.SeriesData("Current (mA)", current, period, "#42A5F5"),
            new TimeSeriesChart.SeriesData("Level (%)", level, period, "#00BFA5"),
        ], "current (mA) / level (%)", "seconds before now");
    }
}
