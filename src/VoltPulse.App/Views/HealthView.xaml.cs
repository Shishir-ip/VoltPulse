using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VoltPulse.App.Controls;
using VoltPulse.App.ViewModels;

namespace VoltPulse.App.Views;

public partial class HealthView : UserControl
{
    public HealthView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => OnViewModelChanged();
        Loaded += (_, _) => UpdateRing();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && DataContext is HealthViewModel viewModel)
            {
                viewModel.Activate();
            }
        };
    }

    private HealthViewModel? ViewModel => DataContext as HealthViewModel;

    private void OnViewModelChanged()
    {
        if (ViewModel is not { } vm) return;
        vm.FadeChartUpdated += OnFadeChartUpdated;
        vm.PropertyChanged += (_, _) => UpdateRing();
        UpdateRing();
    }

    private void OnFadeChartUpdated(double[] values, string xLabel)
    {
        FadeChart.Render(
        [
            new TimeSeriesChart.SeriesData("Full-charge capacity", values, 1.0, "#00BFA5"),
        ], "Full-charge capacity (mWh)", xLabel);
    }

    private void UpdateRing()
    {
        if (HealthRing is null || ViewModel is null) return;
        double pct = 0;
        if (double.TryParse(ViewModel.HealthText.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            pct = Math.Clamp(v, 0, 100) / 100.0;
        }
        // The ring ellipse is 150 px with a 12 px stroke: circumference in stroke units.
        const double diameter = 138; // 150 - stroke
        double units = Math.PI * diameter / 12;
        HealthRing.StrokeDashArray = new DoubleCollection { Math.Max(0.001, units * pct), 999 };
        HealthRing.Stroke = (Brush)(System.Windows.Application.Current.TryFindResource(
            pct >= 0.8 ? "BrushStateFull" : pct >= 0.6 ? "BrushStateDischarging" : "BrushStateCritical")
            ?? Brushes.Gray);
    }
}
