using System.Windows;
using System.Windows.Controls;
using VoltPulse.Core.Calculations;

namespace VoltPulse.App.Controls;

/// <summary>
/// Thin wrapper around ScottPlot's WpfPlot exposing only what VoltPulse needs, so the rest of
/// the UI never touches ScottPlot's API directly. X values are period-based ("N before now"),
/// with the newest sample anchored at x = 0. Series may contain NaN gaps; when a render call
/// contains no valid data at all, an optional hint is shown instead of an empty plot.
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

    public void Render(IReadOnlyList<SeriesData> series, string yLabel, string xLabel, string? emptyHint = null)
    {
        var plot = Plot.Plot;
        plot.Clear();
        bool anyPlottable = false;
        double longestSpanSeconds = 0;
        var allValues = new List<double?>();

        foreach (var s in series)
        {
            if (s.Values.Length < 2) continue;
            var signal = plot.Add.Signal(s.Values, s.PeriodSeconds);
            signal.LegendText = s.Name;
            signal.Color = ScottPlot.Color.FromHex(s.HexColor);
            signal.LineWidth = 1.8f;
            // Anchor the newest sample at x = 0 so the axis reads "N before now".
            signal.Data.XOffset = -s.Values.Length * s.PeriodSeconds;
            anyPlottable = true;
            longestSpanSeconds = Math.Max(longestSpanSeconds, s.Values.Length * s.PeriodSeconds);
            foreach (var v in s.Values)
            {
                allValues.Add(v);
            }
        }

        EmptyHint.Text = string.IsNullOrWhiteSpace(emptyHint) ? "Collecting data..." : emptyHint;
        EmptyHint.Visibility = anyPlottable ? Visibility.Collapsed : Visibility.Visible;

        if (anyPlottable)
        {
            var (min, max, valid) = ChartScaling.ComputeLimits(allValues);
            if (valid > 0)
            {
                plot.Axes.SetLimitsY(min, max);
            }
            if (longestSpanSeconds > 0)
            {
                plot.Axes.SetLimitsX(-longestSpanSeconds, 0);
            }
        }

        plot.Axes.Left.Label.Text = yLabel;
        plot.Axes.Bottom.Label.Text = xLabel;
        Plot.Refresh();
    }

    public void ExportPng(string path, int width = 1280, int height = 720)
    {
        Plot.Plot.SavePng(path, width, height);
    }
}
