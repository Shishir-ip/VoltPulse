using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;
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

    private void OnAuthorLinkClick(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            _ = Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // opening a browser is best effort
        }
        e.Handled = true;
    }
}
