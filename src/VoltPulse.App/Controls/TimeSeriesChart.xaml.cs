using System.Windows;
using System.Windows.Controls;

namespace VoltPulse.App.Controls;

/// <summary>
/// Thin wrapper around ScottPlot's WpfPlot exposing only what VoltPulse needs, so the rest of
/// the UI never touches ScottPlot's API directly. X values are period-based ("N before now").
/// </summary>
public partial class TimeSeriesChart : UserControl
{
    public sealed record SeriesData(string Name, double[] Values, double PeriodSeconds, string HexColor);

    public TimeSeriesChart()
    {
        InitializeComponent();
        ApplyDarkStyle();
    }

    private void ApplyDarkStyle()
    {
        var plot = Plot.Plot;
        plot.FigureBackground.Color = ScottPlot.Color.FromHex("#1E1E1E");
        plot.DataBackground.Color = ScottPlot.Color.FromHex("#1E1E1E");
        plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#333333");
        plot.Axes.Color(ScottPlot.Color.FromHex("#9E9E9E"));
    }

    public void Render(IReadOnlyList<SeriesData> series, string yLabel, string xLabel)
    {
        var plot = Plot.Plot;
        plot.Clear();
        bool any = false;
        foreach (var s in series)
        {
            if (s.Values.Length < 2) continue;
            var signal = plot.Add.Signal(s.Values, s.PeriodSeconds);
            signal.LegendText = s.Name;
            signal.Color = ScottPlot.Color.FromHex(s.HexColor);
            signal.LineWidth = 1.8f;
            any = true;
        }
        EmptyHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        plot.Axes.Left.Label.Text = yLabel;
        plot.Axes.Bottom.Label.Text = xLabel;
        Plot.Refresh();
    }

    public void ExportPng(string path, int width = 1280, int height = 720)
    {
        Plot.Plot.SavePng(path, width, height);
    }
}
