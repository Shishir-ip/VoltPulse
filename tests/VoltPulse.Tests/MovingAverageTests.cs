using VoltPulse.Core.Calculations;

namespace VoltPulse.Tests;

public class MovingAverageTests
{
    [Fact]
    public void AveragesLastN()
    {
        var avg = new MovingAverage(3);
        Assert.Equal(10, avg.Push(10));
        Assert.Equal(15, avg.Push(20));
        Assert.Equal(20, avg.Push(30));       // (10+20+30)/3
        Assert.Equal(30, avg.Push(40));       // (20+30+40)/3
    }

    [Fact]
    public void UnknownResetsWindow()
    {
        var avg = new MovingAverage(3);
        avg.Push(10);
        avg.Push(20);
        Assert.Null(avg.Push(null));
        Assert.Equal(5, avg.Push(5));
    }

    [Fact]
    public void WindowOfOneIsIdentity()
    {
        var avg = new MovingAverage(1);
        Assert.Equal(7, avg.Push(7));
        Assert.Equal(9, avg.Push(9));
    }
}

public class SpikeFilterTests
{
    [Fact]
    public void SingleSpike_RejectedAndLastValidHeld()
    {
        var filter = new SpikeFilter();
        Assert.Equal(400, filter.Push(400));
        double? rejected = null;
        filter.SpikeRejected += (_, _) => rejected = 1;
        Assert.Equal(400, filter.Push(5000));   // 12.5x jump
        Assert.NotNull(rejected);
        Assert.Equal(400, filter.Push(400));    // back to normal, accepted
    }

    [Fact]
    public void SustainedChange_AcceptedAfterConfirmTicks()
    {
        var filter = new SpikeFilter(confirmTicks: 3);
        Assert.Equal(400, filter.Push(400));
        Assert.Equal(400, filter.Push(5000));   // 1st
        Assert.Equal(400, filter.Push(5000));   // 2nd, still held
        Assert.Equal(5000, filter.Push(5000));  // 3rd -> genuine change accepted
    }

    [Fact]
    public void UnknownResetsBaseline()
    {
        var filter = new SpikeFilter();
        Assert.Equal(400, filter.Push(400));
        Assert.Null(filter.Push(null));
        Assert.Equal(4000, filter.Push(4000));  // no baseline -> accepted immediately
    }

    [Fact]
    public void TinyBaseline_DoesNotTriggerSpikes()
    {
        var filter = new SpikeFilter();
        Assert.Equal(0.1, filter.Push(0.1));
        Assert.Equal(2.0, filter.Push(2.0));    // 20x but baseline below 0.5 floor
    }
}
