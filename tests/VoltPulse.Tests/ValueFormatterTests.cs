using VoltPulse.Core.Formatting;

namespace VoltPulse.Tests;

public class ValueFormatterTests
{
    [Fact]
    public void Current_HandlesNullAndUnits()
    {
        Assert.Equal(ValueFormatter.NotSupported, ValueFormatter.Current(null));
        Assert.Equal("430 mA", ValueFormatter.Current(430));
        Assert.Equal("-430 mA", ValueFormatter.Current(-430));
        Assert.Equal("1.50 A", ValueFormatter.Current(1500));
        Assert.Equal("0 mA", ValueFormatter.Current(0));
    }

    [Fact]
    public void Power_Voltage_Percent_Temperature()
    {
        Assert.Equal("12.34 W", ValueFormatter.Power(12340));
        Assert.Equal("980 mW", ValueFormatter.Power(980));
        Assert.Equal(ValueFormatter.NotSupported, ValueFormatter.Power(null));

        Assert.Equal("11.600 V", ValueFormatter.Voltage(11600));
        Assert.Equal("62%", ValueFormatter.Percent(62.4));
        Assert.Equal(ValueFormatter.NotSupported, ValueFormatter.Percent(null));

        Assert.Equal("33.5 °C", ValueFormatter.Temperature(33.47, fahrenheit: false));
        Assert.Equal("92.3 °F", ValueFormatter.Temperature(33.5, fahrenheit: true));
        Assert.Equal(ValueFormatter.NotSupported, ValueFormatter.Temperature(null, false));
    }

    [Fact]
    public void Duration_FormatsCompact()
    {
        Assert.Equal("45 s", ValueFormatter.Duration((TimeSpan?)TimeSpan.FromSeconds(45)));
        Assert.Equal("12 m 30 s", ValueFormatter.Duration((TimeSpan?)TimeSpan.FromSeconds(750)));
        Assert.Equal("3 h 25 m", ValueFormatter.Duration((TimeSpan?)TimeSpan.FromSeconds(12300)));
        Assert.Equal("2 d 4 h", ValueFormatter.Duration((TimeSpan?)TimeSpan.FromSeconds(187200)));
        Assert.Equal(ValueFormatter.NotSupported, ValueFormatter.Duration((TimeSpan?)null));
        Assert.Equal("0 s", ValueFormatter.Duration((TimeSpan?)TimeSpan.FromSeconds(-5)));
    }

    [Fact]
    public void Capacity_CycleCount_Date()
    {
        Assert.Equal("50,000 mWh", ValueFormatter.Capacity(50000));
        Assert.Equal(ValueFormatter.NotSupported, ValueFormatter.Capacity(null));

        Assert.Equal("342", ValueFormatter.CycleCount(342, estimated: false));
        Assert.Equal("342 (estimated)", ValueFormatter.CycleCount(342, estimated: true));
        Assert.Equal(ValueFormatter.NotSupported, ValueFormatter.CycleCount(null, false));

        Assert.Equal("2023-06-15", ValueFormatter.Date(new DateTime(2023, 6, 15)));
        Assert.Equal(ValueFormatter.NotSupported, ValueFormatter.Date(null));
    }

    [Fact]
    public void SignedPercentPerHour()
    {
        Assert.Equal("+8.3%/h", ValueFormatter.SignedPercentPerHour(8.27));
        Assert.Equal("-8.3%/h", ValueFormatter.SignedPercentPerHour(-8.27));
        Assert.Equal(ValueFormatter.NotSupported, ValueFormatter.SignedPercentPerHour(null));
    }
}
