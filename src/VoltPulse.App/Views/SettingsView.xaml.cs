using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using VoltPulse.App.ViewModels;
using VoltPulse.Core.Abstractions;

namespace VoltPulse.App.Views;

public partial class SettingsView : UserControl
{
    private IHistoryStore? _store;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => OnViewModelChanged();
    }

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    private void OnViewModelChanged()
    {
        if (ViewModel is not { } vm) return;
        _store ??= VoltPulse.App.App.HostServices?.GetService(typeof(IHistoryStore)) as IHistoryStore;
        vm.HistoryClearRequested += ConfirmClearHistory;
    }

    private void ConfirmClearHistory()
    {
        if (_store is null || ViewModel is null) return;
        var result = MessageBox.Show(
            "Delete all recorded history (samples, hourly aggregates and sessions)? This cannot be undone.",
            "Clear history", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            _store.ClearHistory();
            ViewModel.Status = "History cleared.";
        }
    }
}
