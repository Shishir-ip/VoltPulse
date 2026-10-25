using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Models;
using VoltPulse.Providers;

namespace VoltPulse.Tests;

public class FakeProvider : IBatteryProvider
{
    private readonly List<BatteryData> _batteries;
    public string Name { get; }
    public bool IsAvailable => _batteries.Count > 0;
    public string? LastError { get; }
    public int ReadCount { get; private set; }

    public FakeProvider(string name, params BatteryData[] batteries)
    {
        Name = name;
        _batteries = [.. batteries];
    }

    public IReadOnlyList<BatteryData> ReadAll()
    {
        ReadCount++;
        return _batteries;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

public class AggregatedProviderTests
{
    private static Serilog.Core.Logger SilentLogger() => new Serilog.LoggerConfiguration().CreateLogger();

    private static BatteryData Make(string id, double? percent = null, int? rate = null, uint? design = null, uint? full = null, uint? current = null) =>
        new()
        {
            Id = id,
            Percent = percent is null ? Sourced<double?>.Unknown : Sourced<double?>.From(percent, DataProviderSource.Wmi),
            RateMilliwatts = rate is null ? Sourced<int?>.Unknown : Sourced<int?>.From(rate, DataProviderSource.Wmi),
            DesignedCapacityMWh = design is null ? Sourced<uint?>.Unknown : Sourced<uint?>.From(design, DataProviderSource.Wmi),
            FullChargeCapacityMWh = full is null ? Sourced<uint?>.Unknown : Sourced<uint?>.From(full, DataProviderSource.Wmi),
            CurrentCapacityMWh = current is null ? Sourced<uint?>.Unknown : Sourced<uint?>.From(current, DataProviderSource.Wmi),
        };

    [Fact]
    public void HigherPriorityProvider_WinsPerField()
    {
        var high = Make("B1", rate: 5000);
        high.RateMilliwatts = Sourced<int?>.From(5000, DataProviderSource.Ioctl);
        var low = Make("B1-low", rate: 9999, percent: 42);

        var agg = new AggregatedBatteryProvider(
            new IBatteryProvider[] { new FakeProvider("High", high), new FakeProvider("Low", low) },
            new SystemInfoProvider(SilentLogger()),
            SilentLogger());

        var snap = agg.ReadSnapshot();
        var b = Assert.Single(snap.Batteries);
        Assert.Equal(5000, b.RateMilliwatts.Value);
        Assert.Equal(DataProviderSource.Ioctl, b.RateMilliwatts.Source);
        Assert.Equal(42, b.Percent.Value);
        Assert.Equal(DataProviderSource.Wmi, b.Percent.Source);
    }

    [Fact]
    public void ProviderFailure_DoesNotBreakMerge()
    {
        var failing = new ThrowingProvider("Broken");
        var good = Make("B1", percent: 77);
        var agg = new AggregatedBatteryProvider(
            new IBatteryProvider[] { failing, new FakeProvider("Good", good) },
            new SystemInfoProvider(SilentLogger()),
            SilentLogger());

        var snap = agg.ReadSnapshot();
        var b = Assert.Single(snap.Batteries);
        Assert.Equal(77, b.Percent.Value);
        Assert.Contains(snap.ProviderStatuses, p => p.Name == "Broken" && !p.Available);
    }

    [Fact]
    public void Combined_SumsCapacities_WeightsPercent()
    {
        var a = Make("A", percent: 50, current: 10000, rate: -1000, design: 50000, full: 40000);
        var b = Make("B", percent: 100, current: 30000, rate: -3000, design: 50000, full: 40000);
        var agg = new AggregatedBatteryProvider(
            new IBatteryProvider[] { new FakeProvider("P", a, b) },
            new SystemInfoProvider(SilentLogger()),
            SilentLogger());

        var snap = agg.ReadSnapshot();
        Assert.Equal(2, snap.Batteries.Count);
        var combined = snap.Combined;
        Assert.NotNull(combined);
        Assert.Equal(87.5, combined!.Percent.Value!.Value, 1); // (50*10k + 100*30k)/40k
        Assert.Equal(100000u, combined.DesignedCapacityMWh.Value);
        Assert.Equal(80000u, combined.FullChargeCapacityMWh.Value);
        Assert.Equal(40000u, combined.CurrentCapacityMWh.Value);
        Assert.Equal(-4000, combined.RateMilliwatts.Value);
    }

    [Fact]
    public void DemoProvider_UsedOnlyWhenNoRealBatteryAndEnabled()
    {
        var mock = new MockBatteryProvider();
        var agg = new AggregatedBatteryProvider(
            new IBatteryProvider[] { new FakeProvider("None") },
            new SystemInfoProvider(SilentLogger()),
            SilentLogger(),
            demoProvider: mock,
            shouldUseDemoProvider: () => true);

        var snap = agg.ReadSnapshot();
        Assert.Single(snap.Batteries);
        Assert.Equal("MOCK-1", snap.Batteries[0].Id);

        var disabled = new AggregatedBatteryProvider(
            new IBatteryProvider[] { new FakeProvider("None") },
            new SystemInfoProvider(SilentLogger()),
            SilentLogger(),
            demoProvider: mock,
            shouldUseDemoProvider: () => false);
        Assert.Empty(disabled.ReadSnapshot().Batteries);
    }

    private sealed class ThrowingProvider : IBatteryProvider
    {
        public ThrowingProvider(string name) => Name = name;
        public string Name { get; }
        public bool IsAvailable => false;
        public string? LastError => "throws";
        public IReadOnlyList<BatteryData> ReadAll() => throw new InvalidOperationException("boom");
        public void Dispose()
        {
        }
    }
}

public class MockProviderTests
{
    [Fact]
    public void ChargingScenario_RaisesPercentAndPositiveRate()
    {
        var mock = new MockBatteryProvider();
        mock.ForcePercent(40);
        var discharge = mock.ReadAll()[0];
        Assert.True(discharge.RateMilliwatts.Value < 0);
        Assert.Equal(ChargeState.Discharging, discharge.ChargeState.Value);

        mock.SetPluggedIn(true);
        var charge = mock.ReadAll()[0];
        Assert.True(charge.RateMilliwatts.Value > 0);
        Assert.Equal(ChargeState.Charging, charge.ChargeState.Value);
        Assert.True(charge.AcOnline.Value == true);
    }

    [Fact]
    public void FullAndPlugged_IsFullyCharged()
    {
        var mock = new MockBatteryProvider();
        mock.SetPluggedIn(true);
        mock.ForcePercent(100);
        var data = mock.ReadAll()[0];
        Assert.Equal(ChargeState.FullyCharged, data.ChargeState.Value);
        Assert.Equal(0, data.RateMilliwatts.Value);
    }

    [Fact]
    public void StaticFacts_ArePopulated()
    {
        var mock = new MockBatteryProvider();
        var data = mock.ReadAll()[0];
        Assert.Equal("MOCK-1", data.Id);
        Assert.True(data.DesignedCapacityMWh.Value > 0);
        Assert.True(data.FullChargeCapacityMWh.Value > 0);
        Assert.True(data.HealthPercent() is > 0 and < 100);
        Assert.NotNull(data.Manufacturer.Value);
        Assert.NotNull(data.SerialNumber.Value);
    }

    [Fact]
    public void PercentStaysWithinBounds()
    {
        var mock = new MockBatteryProvider();
        mock.ForcePercent(1);
        for (int i = 0; i < 50; i++)
        {
            var data = mock.ReadAll()[0];
            Assert.InRange(data.Percent.Value!.Value, 0, 100);
            if (data.Percent.Value <= 0) break;
        }
    }
}
