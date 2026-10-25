using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using VoltPulse.App.Services;
using VoltPulse.App.ViewModels;

namespace VoltPulse.App.Views;

public partial class DashboardView : UserControl
{
    private readonly DispatcherTimer _renderTimer;

    public DashboardView()
    {
        InitializeComponent();
        _renderTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _renderTimer.Tick += (_, _) => RenderChart();
        ExportButton.Click += OnExport;
        DataContextChanged += (_, _) => OnViewModelChanged();
        IsVisibleChanged += (_, e) =>
        {
            if (ViewModel is { } vm) vm.Active = e.NewValue is true;
        };
        Unloaded += (_, _) => _renderTimer.Stop();
        Loaded += (_, _) => _renderTimer.Start();
    }

    private DashboardViewModel? ViewModel => DataContext as DashboardViewModel;

    private void OnViewModelChanged()
    {
        var vm = ViewModel;
        if (vm is null) return;
        vm.Active = IsVisible;
        vm.DataArrived += () => Dispatcher.BeginInvoke(RenderChart, DispatcherPriority.Background);
    }

    private void RenderChart()
    {
        if (ViewModel is not { Active: true } vm) return;
        var series = vm.BuildSeries(out string yLabel, out string xLabel);
        Chart.Render(series is null ? [] : [series], yLabel, xLabel, emptyHint: vm.BuildEmptyHint());
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export chart as PNG",
            Filter = "PNG image|*.png",
            FileName = $"voltpulse-chart-{DateTime.Now:yyyyMMdd-HHmm}.png",
            InitialDirectory = vm.CurrentExportFolder(),
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                Chart.ExportPng(dialog.FileName);
            }
            catch
            {
                // export is best effort
            }
        }
    }
}
