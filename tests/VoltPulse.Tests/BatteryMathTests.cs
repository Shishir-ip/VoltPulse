using VoltPulse.Core.Calculations;
using VoltPulse.Core.Models;

namespace VoltPulse.Tests;

public class BatteryMathTests
{
    [Theory]
    [InlineData(4300, 11600, 370.689655)] // 4.3 W / 11.6 V
    [InlineData(-4300, 11600, -370.689655)]
    [InlineData(0, 11600, 0.0)]
    public void CurrentMilliamps_ConvertsPowerAndVoltage(int rateMw, int voltageMv, double expectedMa)
    {
        var ma = BatteryMath.CurrentMilliamps(rateMw, voltageMv);
        Assert.NotNull(ma);
        Assert.Equal(expectedMa, ma!.Value, 4);
    }

    [Fact]
    public void CurrentMilliamps_UnknownInputs_ReturnNull()
    {
        Assert.Null(BatteryMath.CurrentMilliamps(null, 11600));
        Assert.Null(BatteryMath.CurrentMilliamps(4300, null));
        Assert.Null(BatteryMath.CurrentMilliamps(4300, 0));
        Assert.Null(BatteryMath.CurrentMilliamps(4300, -500));
    }

    [Theory]
    [InlineData(43500u, 50000u, 87.0)]
    [InlineData(50000u, 50000u, 100.0)]
    public void HealthPercent_ComputesFullOverDesign(uint full, uint design, double expected)
    {
        var health = BatteryMath.HealthPercent(full, design);
        Assert.Equal(expected, health!.Value, 2);
    }

    [Fact]
    public void HealthPercent_UnknownOrInvalid_ReturnsNull()
    {
        Assert.Null(BatteryMath.HealthPercent(null, 50000));
        Assert.Null(BatteryMath.HealthPercent(43500, null));
        Assert.Null(BatteryMath.HealthPercent(0, 50000));
        Assert.Null(BatteryMath.HealthPercent(43500, 0));
    }

    [Fact]
    public void Sentinels_AreRecognizedAsUnknown()
    {
        Assert.False(BatterySentinels.IsKnownCapacity(0xFFFFFFFF));
        Assert.False(BatterySentinels.IsKnownVoltage(0xFFFFFFFF));
        Assert.False(BatterySentinels.IsKnownRate(unchecked((int)0x80000000)));
        Assert.False(BatterySentinels.IsKnownTime(0xFFFFFFFF));
        Assert.True(BatterySentinels.IsKnownCapacity(50000));
        Assert.True(BatterySentinels.IsKnownRate(-12000));
    }

    [Fact]
    public void WearPercent_IsHundredMinusHealth_Clamped()
    {
        Assert.Equal(13.0, BatteryMath.WearPercent(87.0));
        Assert.Equal(0.0, BatteryMath.WearPercent(105.0));
        Assert.Null(BatteryMath.WearPercent(null));
    }

    [Fact]
    public void CapacityMah_UsesVoltage()
    {
        Assert.Equal(4310.34, BatteryMath.CapacityMah(50000, 11600)!.Value, 2);
        Assert.Null(BatteryMath.CapacityMah(50000, null));
        Assert.Null(BatteryMath.CapacityMah(null, 11600));
    }

    [Fact]
    public void PercentPerHour_SignedByRate()
    {
        double? full = 43500;
        Assert.Equal(8.2759, BatteryMath.PercentPerHour(3600, full)!.Value, 4);
        Assert.Equal(-8.2759, BatteryMath.PercentPerHour(-3600, full)!.Value, 4);
        Assert.Null(BatteryMath.PercentPerHour(3600, null));
    }

    [Fact]
    public void TimeEstimates_RequirePositiveRates()
    {
        Assert.Equal(3600.0, BatteryMath.TimeToEmptySeconds(10000, 10000)!.Value, 2);
        Assert.Null(BatteryMath.TimeToEmptySeconds(10000, -10000)); // negative is not a discharge magnitude
        Assert.Equal(7200.0, BatteryMath.TimeToFullSeconds(20000, 10000)!.Value, 2);
        Assert.Null(BatteryMath.TimeToFullSeconds(0, 10000));
    }

    [Theory]
    [InlineData(100.0, 1001.0, true)]    // more than 10x is a spike
    [InlineData(100.0, 1000.0, false)]   // exactly 10x is not ("more than 10x")
    [InlineData(100.0, 999.0, false)]
    [InlineData(100.0, -1001.0, true)]
    [InlineData(0.0, 1000.0, false)]     // no baseline
    [InlineData(100.0, null, false)]
    [InlineData(null, 1000.0, false)]
    public void IsSpike_DetectsJumpsBeyondFactor(double? previous, double? candidate, bool expected)
    {
        Assert.Equal(expected, BatteryMath.IsSpike(previous, candidate));
    }

    [Theory]
    [InlineData(true, true, false, 50, ChargeState.Charging)]
    [InlineData(true, false, true, 50, ChargeState.Discharging)]
    [InlineData(true, false, false, 100, ChargeState.FullyCharged)]
    [InlineData(true, false, false, 62, ChargeState.IdleOnAc)]
    [InlineData(false, false, false, 40, ChargeState.Discharging)]
    [InlineData(null, false, false, 40, ChargeState.Unknown)]
    public void ResolveChargeState_HandlesFlagsAndFallbacks(bool? ac, bool charging, bool discharging, double percent, ChargeState expected)
    {
        Assert.Equal(expected, BatteryMath.ResolveChargeState(ac, charging, discharging, percent));
    }

    [Theory]
    [InlineData(ChargeState.Charging, 50.0, 40.0, BatteryLiveState.Charging)]
    [InlineData(ChargeState.Discharging, 50.0, 40.0, BatteryLiveState.Discharging)]
    [InlineData(ChargeState.FullyCharged, 100.0, 40.0, BatteryLiveState.Full)]
    [InlineData(ChargeState.Discharging, 8.0, 40.0, BatteryLiveState.Critical)]
    [InlineData(ChargeState.Charging, 50.0, 60.0, BatteryLiveState.Critical)]
    public void ResolveLiveState_ColorBuckets(ChargeState state, double? percent, double? temp, BatteryLiveState expected)
    {
        Assert.Equal(expected, BatteryMath.ResolveLiveState(state, percent, temp));
    }
}
