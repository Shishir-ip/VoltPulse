using System.Windows.Controls;
using VoltPulse.App.Services;
using VoltPulse.App.ViewModels;

namespace VoltPulse.App.Views;

public partial class LiveView : UserControl
{
    public LiveView()
    {
        InitializeComponent();
    }

    private void OnShareClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not LiveViewModel viewModel) return;
        ShareService.CaptureLiveSnapshot(this, viewModel.BuildShareSummary(), viewModel.ExportFolder);
    }
}
