using VoltPulse.Services;

namespace VoltPulse.Tests;

public class PowerCfgReportImporterTests
{
    private const string SampleXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <BatteryReports xmlns="http://schemas.microsoft.com/battery/2012">
          <Batteries>
            <Battery>
              <Id>AP18E7M</Id>
              <Manufacturer>SIMPLO</Manufacturer>
              <SerialNumber>20598</SerialNumber>
              <Chemistry>Li-Ion</Chemistry>
              <DesignCapacity>58751</DesignCapacity>
              <FullChargeCapacity>41965</FullChargeCapacity>
              <CycleCount>0</CycleCount>
            </Battery>
          </Batteries>
          <Report>
            <CapacityHistory>
              <BatteryCycleEntry>
                <StartDate>2025-01-15T00:00:00</StartDate>
                <FullChargeCapacity>45123</FullChargeCapacity>
                <DesignCapacity>58751</DesignCapacity>
                <CycleCount>120</CycleCount>
              </BatteryCycleEntry>
              <BatteryCycleEntry>
                <StartDate>2026-06-15T00:00:00</StartDate>
                <FullChargeCapacity>41965</FullChargeCapacity>
                <DesignCapacity>58751</DesignCapacity>
                <CycleCount>342</CycleCount>
              </BatteryCycleEntry>
            </CapacityHistory>
          </Report>
        </BatteryReports>
        """;

    [Fact]
    public void Parse_ExtractsBatteryAndHistory()
    {
        var importer = new PowerCfgReportImporter(new Serilog.LoggerConfiguration().CreateLogger());
        var data = importer.Parse(SampleXml);

        Assert.NotNull(data);
        Assert.Equal("AP18E7M", data!.BatteryId);
        Assert.Equal("SIMPLO", data.Manufacturer);
        Assert.Equal(58751u, data.DesignCapacity);
        Assert.Equal(41965u, data.FullChargeCapacity);
        Assert.Equal(2, data.CapacityHistory.Count);
        Assert.Equal(45123u, data.CapacityHistory[0].FullChargeCapacity);
        Assert.Equal(2025, data.CapacityHistory[0].Date.Year);
        Assert.Equal(342, data.CapacityHistory[1].CycleCount);
    }

    [Fact]
    public void Parse_Garbage_ReturnsNull()
    {
        var importer = new PowerCfgReportImporter(new Serilog.LoggerConfiguration().CreateLogger());
        Assert.Null(importer.Parse("not xml at all <<<>>>"));
        Assert.Null(importer.Parse(""));
    }

    [Fact]
    public void Parse_MissingSections_ReturnsEmptyHistory()
    {
        var importer = new PowerCfgReportImporter(new Serilog.LoggerConfiguration().CreateLogger());
        var data = importer.Parse("<BatteryReports></BatteryReports>");
        Assert.NotNull(data);
        Assert.Empty(data!.CapacityHistory);
        Assert.Null(data.DesignCapacity);
    }
}
