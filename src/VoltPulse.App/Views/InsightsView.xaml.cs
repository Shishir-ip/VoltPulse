using System.Windows.Controls;
using VoltPulse.App.ViewModels;

namespace VoltPulse.App.Views;

public partial class InsightsView : UserControl
{
    public InsightsView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && DataContext is InsightsViewModel viewModel)
            {
                viewModel.Activate();
            }
        };
    }
}
