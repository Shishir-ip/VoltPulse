using VoltPulse.Core.Models;
using VoltPulse.Services;

namespace VoltPulse.Tests;

/// <summary>
/// Fixtures follow the REAL powercfg /batteryreport XML schema: HistoryEntry and UsageEntry
/// carry their values as attributes (verified on Windows 11, report version 3).
/// </summary>
public class PowerCfgReportImporterTests
{
    private const string SampleXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <BatteryReports xmlns="http://schemas.microsoft.com/battery/2012">
          <ReportInformation>
            <ComputerName>LAPTOP</ComputerName>
            <ReportVersion>3.0</ReportVersion>
          </ReportInformation>
          <SystemInformation></SystemInformation>
          <Batteries>
            <Battery>
              <Id>AP18E7M</Id>
              <Manufacturer>SIMPLO</Manufacturer>
              <SerialNumber>20598</SerialNumber>
              <Chemistry>LION</Chemistry>
              <DesignCapacity>58751</DesignCapacity>
              <FullChargeCapacity>40394</FullChargeCapacity>
              <CycleCount>0</CycleCount>
            </Battery>
          </Batteries>
          <RecentUsage>
            <UsageEntry
                  Timestamp="2026-10-18T18:37:00Z"
                  Duration="89942625265"
                  Ac="1"
                  EntryType="Active"
                  ChargeCapacity="34265"
                  Discharge="15"
                  FullChargeCapacity="42566"
                  IsNextOnBattery="0"
                  />
            <UsageEntry
                  Timestamp="2026-10-19T19:25:28Z"
                  Duration="13510941309"
                  Ac="0"
                  EntryType="Active"
                  ChargeCapacity="33664"
                  Discharge="4666"
                  FullChargeCapacity="42566"
                  IsNextOnBattery="0"
                  />
            <UsageEntry
                  Timestamp="2026-10-19T19:48:00Z"
                  Duration="288979740725"
                  Ac="0"
                  EntryType="Suspend"
                  ChargeCapacity="28998"
                  Discharge="878"
                  FullChargeCapacity="42566"
                  IsNextOnBattery="1"
                  />
          </RecentUsage>
          <History>
            <HistoryEntry
                  StartDate="2024-10-28T16:22:02Z"
                  EndDate="2024-11-04T00:34:30Z"
                  DesignCapacity="58751"
                  FullChargeCapacity="56302"
                  CycleCount="0"
                  ActiveDcEnergy="120566"
                  />
            <HistoryEntry
                  StartDate="2025-10-26T00:00:00Z"
                  EndDate="2025-11-02T00:00:00Z"
                  DesignCapacity="58751"
                  FullChargeCapacity="41965"
                  CycleCount="0"
                  ActiveDcEnergy="183279"
                  />
          </History>
        </BatteryReports>
        """;

    [Fact]
    public void Parse_ExtractsBatteryAndCapacityHistory()
    {
        var importer = new PowerCfgReportImporter(new Serilog.LoggerConfiguration().CreateLogger());
        var data = importer.Parse(SampleXml);

        Assert.NotNull(data);
        Assert.Equal("AP18E7M", data!.BatteryId);
        Assert.Equal("SIMPLO", data.Manufacturer);
        Assert.Equal(58751u, data.DesignCapacity);
        Assert.Equal(40394u, data.FullChargeCapacity);
        Assert.Equal(2, data.CapacityHistory.Count);
        Assert.Equal(56302u, data.CapacityHistory[0].FullChargeCapacity);
        Assert.Equal(2024, data.CapacityHistory[0].Date.Year);
        Assert.Equal(41965u, data.CapacityHistory[1].FullChargeCapacity);
    }

    [Fact]
    public void Parse_ExtractsRecentUsageEntries()
    {
        var importer = new PowerCfgReportImporter(new Serilog.LoggerConfiguration().CreateLogger());
        var data = importer.Parse(SampleXml);

        Assert.NotNull(data);
        Assert.Equal(3, data!.RecentUsage.Count);
        var first = data.RecentUsage[0];
        Assert.True(first.OnAc);
        Assert.Equal(8994.2625265, first.Duration.TotalSeconds, 4);
        Assert.Equal(34265u, first.EndCapacityMwh);
        Assert.Equal(42566u, first.FullChargeCapacityMwh);
        Assert.False(data.RecentUsage[1].OnAc);
        Assert.Equal(4666u, data.RecentUsage[1].DischargeMwh);
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
        Assert.Empty(data.RecentUsage);
        Assert.Null(data.DesignCapacity);
    }

    [Fact]
    public void Parse_LegacyChildElementLayout_StillSupported()
    {
        const string legacy = """
            <BatteryReports>
              <Batteries>
                <Battery><Id>OLD</Id><DesignCapacity>50000</DesignCapacity><FullChargeCapacity>40000</FullChargeCapacity></Battery>
              </Batteries>
              <CapacityHistory>
                <BatteryCycleEntry>
                  <StartDate>2025-01-15T00:00:00</StartDate>
                  <FullChargeCapacity>45123</FullChargeCapacity>
                  <DesignCapacity>50000</DesignCapacity>
                  <CycleCount>120</CycleCount>
                </BatteryCycleEntry>
              </CapacityHistory>
            </BatteryReports>
            """;
        var importer = new PowerCfgReportImporter(new Serilog.LoggerConfiguration().CreateLogger());
        var data = importer.Parse(legacy);
        Assert.NotNull(data);
        Assert.Equal(45123u, data!.CapacityHistory[0].FullChargeCapacity);
        Assert.Equal(120, data.CapacityHistory[0].CycleCount);
    }
}

public class PowerCfgSessionBackfillTests
{
    private static UsagePeriod Entry(DateTime start, double minutes, bool ac, uint endCap, uint drain, uint fcc = 42566) =>
        new(start, TimeSpan.FromMinutes(minutes), ac, "Active", endCap, drain, fcc);

    [Fact]
    public void DischargeRun_BecomesASession()
    {
        var t0 = new DateTime(2026, 10, 19, 19, 0, 0, DateTimeKind.Utc);
        var usage = new List<UsagePeriod>
        {
            Entry(t0, 27, ac: true, endCap: 33664, drain: 0),          // idle on AC (gap)
            Entry(t0.AddMinutes(30), 25, ac: false, endCap: 32000, drain: 1664),
            Entry(t0.AddMinutes(55), 30, ac: false, endCap: 28998, drain: 3002),
            Entry(t0.AddMinutes(90), 20, ac: true, endCap: 28998, drain: 0),   // plugged back in
        };

        var sessions = PowerCfgSessionBackfill.BuildSessions(usage, designedVoltageMv: 16080);

        var discharge = Assert.Single(sessions);
        Assert.Equal(ChargeState.Discharging, discharge.Kind);
        Assert.Equal("IMPORTED", discharge.BatteryId);
        Assert.Equal(55, (discharge.EndUtc!.Value - discharge.StartUtc).TotalMinutes, 0); // 55 min run
        Assert.Equal(4666, discharge.EnergyMWh!.Value, 0); // 1664 + 3002
        Assert.Equal(79.1, discharge.StartPercent!.Value, 1);   // 33664+1664 of 42566
        Assert.Equal(68.1, discharge.EndPercent!.Value, 1);     // 28998 of 42566
        Assert.NotNull(discharge.AvgCurrentMa);
        Assert.True(discharge.AvgCurrentMa > 0);
    }

    [Fact]
    public void ChargeRun_BecomesASession()
    {
        var t0 = new DateTime(2026, 10, 19, 19, 0, 0, DateTimeKind.Utc);
        var usage = new List<UsagePeriod>
        {
            Entry(t0, 30, ac: true, endCap: 20000, drain: 0),
            Entry(t0.AddMinutes(30), 30, ac: true, endCap: 28000, drain: 0),   // +8000
            Entry(t0.AddMinutes(60), 30, ac: true, endCap: 35000, drain: 0),   // +7000
        };

        var sessions = PowerCfgSessionBackfill.BuildSessions(usage, designedVoltageMv: 16080);

        var charge = Assert.Single(sessions);
        Assert.Equal(ChargeState.Charging, charge.Kind);
        Assert.Equal(15000, charge.EnergyMWh!.Value, 0);
        Assert.Equal(47.0, charge.StartPercent!.Value, 1); // 20000 of 42566
        Assert.Equal(82.2, charge.EndPercent!.Value, 1);
    }

    [Fact]
    public void ShortRuns_AreDropped()
    {
        var t0 = new DateTime(2026, 10, 19, 19, 0, 0, DateTimeKind.Utc);
        var usage = new List<UsagePeriod>
        {
            Entry(t0, 5, ac: false, endCap: 30000, drain: 200),
        };
        Assert.Empty(PowerCfgSessionBackfill.BuildSessions(usage, 16080));
    }

    [Fact]
    public void IdleOnAcPeriods_AreSkipped()
    {
        var t0 = new DateTime(2026, 10, 19, 19, 0, 0, DateTimeKind.Utc);
        var usage = new List<UsagePeriod>
        {
            Entry(t0, 60, ac: true, endCap: 34000, drain: 10),
            Entry(t0.AddMinutes(70), 60, ac: true, endCap: 33950, drain: 50),  // slight drain, still idle
        };
        Assert.Empty(PowerCfgSessionBackfill.BuildSessions(usage, 16080));
    }
}
