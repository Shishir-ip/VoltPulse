using System.Windows.Controls;
using VoltPulse.App.ViewModels;

namespace VoltPulse.App.Views;

public partial class PowerUsageView : UserControl
{
    public PowerUsageView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (DataContext is not PowerUsageViewModel viewModel) return;
            if (e.NewValue is true) viewModel.Activate();
            else viewModel.Deactivate();
        };
    }
}
