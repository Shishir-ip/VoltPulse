using System.Text.Json;
using VoltPulse.Core.Models;
using VoltPulse.Services;

namespace VoltPulse.Tests;

public class ExportServiceTests : IDisposable
{
    private readonly string _dir;

    public ExportServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"voltpulse-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // temp cleanup best effort
        }
    }

    private static ReportData Report() => new()
    {
        System = new SystemInfo { MachineManufacturer = "Acer", MachineModel = "Nitro", OsCaption = "Windows 11", OsBuild = "26300", ActivePowerPlan = "Balanced" },
        BatteryId = "B1",
        Manufacturer = "SIMPLO",
        DeviceName = "AP18E7M",
        SerialNumber = "SECRET-123",
        Chemistry = "Li-Ion",
        HealthPercent = 71.5,
        DesignCapacityMWh = 58751,
        FullChargeCapacityMWh = 41965,
        CycleCount = 0,
        Percent = 80,
        State = ChargeState.IdleOnAc,
        CurrentMa = null,
        VoltageMv = 16080,
        TemperatureC = 33.5,
        Sessions =
        [
            new SessionRecord { BatteryId = "B1", StartUtc = DateTime.UtcNow.AddHours(-2), EndUtc = DateTime.UtcNow, Kind = ChargeState.Charging, StartPercent = 40, EndPercent = 80, AvgCurrentMa = 2000, PeakCurrentMa = 2400, EnergyMWh = 9000, SampleCount = 7200 },
        ],
        ChartSamples = Enumerable.Range(0, 100).Select(i => new BatterySample(
            DateTime.UtcNow.AddSeconds(i - 100), "B1", 79 + i * 0.01, 16000, -4000, -345, -4000, 33, ChargeState.Discharging)).ToList(),
        IncludeSerial = false,
    };

    [Fact]
    public void Csv_WritesHeaderAndRows()
    {
        string path = Path.Combine(_dir, "h.csv");
        var samples = Enumerable.Range(0, 5).Select(i => new BatterySample(
            DateTime.UtcNow.AddSeconds(i), "B1", 50 + i, 11600, -4000, -345, -4000, 33.0, ChargeState.Discharging)).ToList();

        ExportService.ExportCsv(samples, path);
        var lines = File.ReadAllLines(path);
        Assert.StartsWith("timestamp_utc,battery_id,percent", lines[0]);
        Assert.Equal(6, lines.Length);
        Assert.Contains("Discharging", lines[1]);
    }

    [Fact]
    public void Json_EmitsParseableArray()
    {
        string path = Path.Combine(_dir, "h.json");
        var samples = new List<BatterySample> { new(DateTime.UtcNow, "B1", 62.5, 11600, 2000, 172, 2000, null, ChargeState.Charging) };
        ExportService.ExportJson(samples, path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var first = doc.RootElement.EnumerateArray().First();
        Assert.Equal("B1", first.GetProperty("batteryId").GetString());
        Assert.Equal(62.5, first.GetProperty("percent").GetDouble());
        Assert.Equal("Charging", first.GetProperty("state").GetString());
    }

    [Fact]
    public void Html_IsSelfContained_AndHidesSerialByDefault()
    {
        string path = Path.Combine(_dir, "r.html");
        ExportService.ExportHtml(Report(), path);
        string html = File.ReadAllText(path);
        Assert.Contains("VoltPulse battery report", html);
        Assert.Contains("<svg", html);
        Assert.DoesNotContain("SECRET-123", html);
        Assert.Contains("(hidden)", html);
    }

    [Fact]
    public void Pdf_StartsWithHeader_AndContainsReportText()
    {
        string path = Path.Combine(_dir, "r.pdf");
        ExportService.ExportPdf(Report(), path);
        var bytes = File.ReadAllBytes(path);
        string asText = System.Text.Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", asText);
        Assert.EndsWith("%%EOF", asText);
        Assert.True(bytes.Length > 1000);
    }

    private static ReportData CopyWithSerial(ReportData r) => new()
    {
        GeneratedUtc = r.GeneratedUtc, System = r.System, BatteryId = r.BatteryId,
        Manufacturer = r.Manufacturer, DeviceName = r.DeviceName, SerialNumber = r.SerialNumber,
        Chemistry = r.Chemistry, HealthPercent = r.HealthPercent, DesignCapacityMWh = r.DesignCapacityMWh,
        FullChargeCapacityMWh = r.FullChargeCapacityMWh, CycleCount = r.CycleCount, Percent = r.Percent,
        State = r.State, CurrentMa = r.CurrentMa, VoltageMv = r.VoltageMv, TemperatureC = r.TemperatureC,
        Sessions = r.Sessions, ChartSamples = r.ChartSamples, IncludeSerial = true,
    };

    [Fact]
    public void Pdf_IncludesSerialWhenOptedIn()
    {
        string path = Path.Combine(_dir, "r2.pdf");
        var report = Report();
        Assert.True(report.IncludeSerial is false);
        var optedIn = CopyWithSerial(report);
        ExportService.ExportPdf(optedIn, path);
        string text = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path));
        Assert.Contains("SECRET-123", text);
    }
}

public class InsightsCalculatorTests
{
    private static SessionRecord Session(ChargeState kind, double start, double end) =>
        new() { BatteryId = "B1", StartUtc = DateTime.UtcNow.AddHours(-1), EndUtc = DateTime.UtcNow, Kind = kind, StartPercent = start, EndPercent = end };

    [Fact]
    public void HealthyHabits_HighScore()
    {
        var sessions = new List<SessionRecord>
        {
            Session(ChargeState.Charging, 40, 80),
            Session(ChargeState.Charging, 50, 79),
            Session(ChargeState.Discharging, 80, 45),
            Session(ChargeState.Discharging, 70, 35),
        };
        var insights = InsightsCalculator.Compute(sessions, [], TimeSpan.FromDays(7));
        Assert.Equal(100, insights.Score);
        Assert.Equal(0, insights.DeepDischarges);
        Assert.Contains(insights.Tips, t => t.Contains("20% and 80%"));
    }

    [Fact]
    public void FullChargesAndDeepDischarges_LowerScore()
    {
        var sessions = new List<SessionRecord>
        {
            Session(ChargeState.Charging, 10, 100),
            Session(ChargeState.Charging, 20, 100),
            Session(ChargeState.Charging, 15, 100),
            Session(ChargeState.Discharging, 100, 10),
            Session(ChargeState.Discharging, 90, 15),
        };
        var insights = InsightsCalculator.Compute(sessions, [], TimeSpan.FromDays(7));
        // 100 - 20 (always charges to full) - 10 (2 deep discharges) - 10 (large discharge depth)
        Assert.Equal(60, insights.Score);
        Assert.Equal(2, insights.DeepDischarges);
        Assert.Equal(1.0, insights.ChargeToFullRatio);
        Assert.Contains(insights.Tips, t => t.Contains("80%"));
    }

    [Fact]
    public void TimeAtFull_UsesSamples()
    {
        var samples = new List<BatterySample>();
        for (int i = 0; i < 100; i++)
        {
            samples.Add(new BatterySample(DateTime.UtcNow.AddSeconds(i), "B1", i < 20 ? 100 : 50, 11600, 0, 0, 0, null, ChargeState.IdleOnAc));
        }
        var insights = InsightsCalculator.Compute([], samples, TimeSpan.FromHours(1));
        Assert.Equal(0.2, insights.TimeAtFullPercent!.Value, 3);
    }

    [Fact]
    public void NoData_ScoreStaysNeutral()
    {
        var insights = InsightsCalculator.Compute([], [], TimeSpan.FromDays(7));
        Assert.Equal(100, insights.Score);
        Assert.Single(insights.Tips);
    }
}

public class BatteryDataTests
{
    [Fact]
    public void FillMissingFrom_KeepsExistingValues()
    {
        var high = new BatteryData { Id = "A" };
        high.Percent = Sourced<double?>.From(80, DataProviderSource.Ioctl);
        high.Manufacturer = Sourced<string?>.From("SIMPLO", DataProviderSource.Ioctl);

        var low = new BatteryData { Id = "A" };
        low.Percent = Sourced<double?>.From(1, DataProviderSource.Wmi);
        low.TemperatureCelsius = Sourced<double?>.From(30, DataProviderSource.Wmi);
        low.CycleCount = Sourced<int?>.From(5, DataProviderSource.Wmi);
        low.CycleCountEstimated = false;

        high.FillMissingFrom(low);

        Assert.Equal(80, high.Percent.Value);      // kept from high-priority source
        Assert.Equal("SIMPLO", high.Manufacturer.Value);
        Assert.Equal(30, high.TemperatureCelsius.Value); // filled from lower source
        Assert.Equal(5, high.CycleCount.Value);
    }

    [Fact]
    public void Clone_CopiesAllFields()
    {
        var b = new BatteryData { Id = "X", CycleCountEstimated = true };
        b.Percent = Sourced<double?>.From(33, DataProviderSource.Mock);
        b.ChargeState = Sourced<ChargeState?>.From(ChargeState.Charging, DataProviderSource.Mock);
        var copy = b.Clone();

        Assert.Equal(33, copy.Percent.Value);
        Assert.Equal(ChargeState.Charging, copy.ChargeState.Value);
        Assert.True(copy.CycleCountEstimated);
        Assert.Equal(DataProviderSource.Mock, copy.Percent.Source);
    }

    [Fact]
    public void CurrentMilliamps_RespectsInvertSign()
    {
        var b = new BatteryData();
        b.RateMilliwatts = Sourced<int?>.From(-4640, DataProviderSource.Ioctl);
        b.VoltageMillivolts = Sourced<int?>.From(11600, DataProviderSource.Ioctl);

        Assert.Equal(-400, b.CurrentMilliamps());
        Assert.Equal(400, b.CurrentMilliamps(invertSign: true));
    }

    [Fact]
    public void MockProvider_StableOverLongRun()
    {
        var mock = new VoltPulse.Providers.MockBatteryProvider();
        mock.SetPluggedIn(true);
        for (int i = 0; i < 1000; i++)
        {
            var data = mock.ReadAll()[0];
            Assert.InRange(data.Percent.Value!.Value, 0, 100);
            Assert.True(data.VoltageMillivolts.Value is > 10000 and < 13000);
            if (data.Percent.Value >= 100)
            {
                Assert.Equal(ChargeState.FullyCharged, data.ChargeState.Value);
            }
        }
    }
}
