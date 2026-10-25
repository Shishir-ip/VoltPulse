using System.Windows.Controls;
using VoltPulse.App.ViewModels;

namespace VoltPulse.App.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && DataContext is AboutViewModel viewModel)
            {
                viewModel.RefreshProviderStatus();
            }
        };
    }
}
