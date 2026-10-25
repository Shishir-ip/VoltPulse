using VoltPulse.Core.Models;
using VoltPulse.Services;

namespace VoltPulse.Tests;

public class SessionTrackerTests
{
    [Fact]
    public void ChargingSession_DetectedAndClosed()
    {
        var tracker = new SessionTracker();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        Assert.Null(tracker.OnSample(t0, ChargeState.Charging, 50, 5000, 430));
        for (int i = 1; i <= 60; i++)
        {
            Assert.Null(tracker.OnSample(t0.AddSeconds(i), ChargeState.Charging, 50 + i * 0.1, 5000, 431));
        }

        Assert.Null(tracker.OnSample(t0.AddSeconds(61), ChargeState.Discharging, 59, -4000, -350)); // debounce
        var ended = tracker.OnSample(t0.AddSeconds(62), ChargeState.Discharging, 60, -4000, -350);
        Assert.NotNull(ended);
        Assert.Equal(ChargeState.Charging, ended!.Kind);
        Assert.Equal(62, (ended.EndUtc!.Value - ended.StartUtc).TotalSeconds, 0);
        Assert.Equal(50, ended.StartPercent);
        Assert.Equal(60, ended.EndPercent);
        Assert.True(ended.AvgCurrentMa > 400);
        Assert.True(ended.EnergyMWh > 0);
    }

    [Fact]
    public void SingleFlicker_DoesNotSplitSession()
    {
        var tracker = new SessionTracker();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        tracker.OnSample(t0, ChargeState.Charging, 50, 5000, 430);
        tracker.OnSample(t0.AddSeconds(1), ChargeState.Charging, 50, 5000, 430);

        // one idle sample should not close the charging session
        Assert.Null(tracker.OnSample(t0.AddSeconds(2), ChargeState.IdleOnAc, 50.2, 0, 0));
        Assert.NotNull(tracker.OpenSession);
    }

    [Fact]
    public void ShortSession_IsDropped()
    {
        var tracker = new SessionTracker();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        tracker.OnSample(t0, ChargeState.Charging, 50, 5000, 430);
        var ended = tracker.OnSample(t0.AddSeconds(3), ChargeState.IdleOnAc, 50.5, 0, 0);
        Assert.Null(ended);
    }

    [Fact]
    public void FlushOpen_ReturnsOpenSession()
    {
        var tracker = new SessionTracker();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        tracker.OnSample(t0, ChargeState.Discharging, 80, -5000, -420);
        var ended = tracker.FlushOpen();
        Assert.NotNull(ended);
        Assert.Equal(ChargeState.Discharging, ended!.Kind);
        Assert.Null(tracker.OpenSession);
    }
}
