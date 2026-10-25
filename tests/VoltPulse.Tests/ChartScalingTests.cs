using VoltPulse.Core.Calculations;

namespace VoltPulse.Tests;

public class ChartScalingTests
{
    [Fact]
    public void AllNaN_NothingValid()
    {
        var (min, max, valid) = ChartScaling.ComputeLimits([double.NaN, double.NaN, double.NaN]);
        Assert.Equal(0, valid);
    }

    [Fact]
    public void PartialGaps_LimitsFromValidValuesOnly()
    {
        var values = new double?[] { 100, double.NaN, 200, double.NaN, 300 };
        var (min, max, valid) = ChartScaling.ComputeLimits(values, padFraction: 0.1, minSpan: 1);
        Assert.Equal(3, valid);
        Assert.Equal(80, min, 3);   // 100 - span(200) * 0.1
        Assert.Equal(320, max, 3);  // 300 + span(200) * 0.1
    }

    [Fact]
    public void FlatSeries_GetsMinimumSpan()
    {
        var (min, max, valid) = ChartScaling.ComputeLimits([5.0, 5.0, 5.0], padFraction: 0.1, minSpan: 1.0);
        Assert.Equal(3, valid);
        Assert.Equal(4.9, min, 3);  // 5 - minSpan(1) * 0.1
        Assert.Equal(5.1, max, 3);  // 5 + minSpan(1) * 0.1
    }

    [Fact]
    public void NullsAreGapsToo()
    {
        Assert.Equal(0, ChartScaling.ValidCount([null, double.NaN, null]));
        Assert.Equal(1, ChartScaling.ValidCount([null, 7.0, double.NaN]));
    }
}
