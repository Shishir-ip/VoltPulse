namespace VoltPulse.Core.Calculations;

/// <summary>Axis-scaling helpers for charts that contain gaps (NaN = no data).</summary>
public static class ChartScaling
{
    /// <summary>
    /// Computes Y limits from the valid (non-NaN) values, padded by a fraction of the span.
    /// Returns <see cref="ValidCount"/> = 0 when there is nothing to draw.
    /// </summary>
    public static (double Min, double Max, int ValidCount) ComputeLimits(
        IReadOnlyList<double?> values, double padFraction = 0.12, double minSpan = 1.0)
    {
        double min = double.MaxValue;
        double max = double.MinValue;
        int valid = 0;

        foreach (var v in values)
        {
            if (v is null || double.IsNaN(v.Value)) continue;
            valid++;
            if (v.Value < min) min = v.Value;
            if (v.Value > max) max = v.Value;
        }

        if (valid == 0) return (0, 0, 0);

        double span = Math.Max(max - min, minSpan);
        double pad = span * padFraction;
        return (min - pad, max + pad, valid);
    }

    /// <summary>Counts non-NaN values (gaps excluded).</summary>
    public static int ValidCount(IReadOnlyList<double?> values)
        => values.Count(v => v is not null && !double.IsNaN(v.Value));
}
