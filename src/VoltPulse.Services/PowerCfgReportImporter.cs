using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace VoltPulse.Services;

/// <summary>Capacity history imported from a powercfg /batteryreport XML (long-term health data).</summary>
public sealed record CapacityHistoryEntry(DateTime Date, uint FullChargeCapacity, uint DesignCapacity, int CycleCount);

/// <summary>One recent-usage period from the report (per-entry AC/DC state, duration and energy).</summary>
public sealed record UsagePeriod(
    DateTime StartUtc,
    TimeSpan Duration,
    bool OnAc,
    string EntryType,
    uint? EndCapacityMwh,
    uint? DischargeMwh,
    uint? FullChargeCapacityMwh);

public sealed record PowerCfgReportData(
    string? BatteryId,
    string? Manufacturer,
    string? SerialNumber,
    string? Chemistry,
    uint? DesignCapacity,
    uint? FullChargeCapacity,
    int? CycleCount,
    IReadOnlyList<CapacityHistoryEntry> CapacityHistory,
    IReadOnlyList<UsagePeriod> RecentUsage,
    DateTime ImportedAtUtc);

/// <summary>
/// Provider D: runs `powercfg /batteryreport /XML` and parses it so VoltPulse can show long-term
/// capacity fade and backfill sessions. The parser matches the real report schema (HistoryEntry /
/// UsageEntry with attributes) while remaining tolerant of older layouts and missing sections.
/// </summary>
public sealed class PowerCfgReportImporter
{
    private readonly Serilog.ILogger _logger;

    public PowerCfgReportImporter(Serilog.ILogger logger) => _logger = logger.ForContext<PowerCfgReportImporter>();

    public PowerCfgReportData? Import()
    {
        string tempXml = Path.Combine(Path.GetTempPath(), $"voltpulse-batteryreport-{Guid.NewGuid():N}.xml");
        try
        {
            var psi = new ProcessStartInfo("powercfg", $"/batteryreport /XML /OUTPUT \"{tempXml}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                _logger.Warning("powercfg could not be started");
                return null;
            }
            if (!process.WaitForExit(30_000))
            {
                process.Kill(true);
                _logger.Warning("powercfg battery report timed out");
                return null;
            }
            if (!File.Exists(tempXml))
            {
                _logger.Warning("powercfg did not produce a battery report");
                return null;
            }

            return Parse(File.ReadAllText(tempXml));
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "powercfg battery report import failed");
            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(tempXml)) File.Delete(tempXml);
            }
            catch
            {
                // temp cleanup best effort
            }
        }
    }

    /// <summary>Parses the XML defensively: anything missing or malformed degrades to empty lists.</summary>
    public PowerCfgReportData? Parse(string xml)
    {
        try
        {
            XDocument doc = XDocument.Parse(xml);
            var battery = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Battery");

            var history = new List<CapacityHistoryEntry>();
            foreach (var entry in doc.Descendants().Where(e => e.Name.LocalName is "HistoryEntry" or "BatteryCycleEntry"))
            {
                var date = AttrDate(entry, "StartDate") ?? AttrDate(entry, "EndDate");
                uint? full = AttrUint(entry, "FullChargeCapacity");
                uint? design = AttrUint(entry, "DesignCapacity");
                int? cycles = AttrInt(entry, "CycleCount");
                if (date is not null && full is > 0)
                {
                    history.Add(new CapacityHistoryEntry(date.Value, full.Value, design ?? 0, cycles ?? 0));
                }
            }
            history.Sort((a, b) => a.Date.CompareTo(b.Date));

            var usage = new List<UsagePeriod>();
            foreach (var entry in doc.Descendants().Where(e => e.Name.LocalName == "UsageEntry"))
            {
                var start = AttrDate(entry, "Timestamp");
                if (start is null) continue;
                long ticks = AttrLong(entry, "Duration") ?? 0;
                usage.Add(new UsagePeriod(
                    start.Value,
                    TimeSpan.FromTicks(ticks),
                    AttrLong(entry, "Ac") == 1,
                    Attr(entry, "EntryType") ?? "Active",
                    AttrUint(entry, "ChargeCapacity"),
                    AttrUint(entry, "Discharge"),
                    AttrUint(entry, "FullChargeCapacity")));
            }
            usage.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));

            return new PowerCfgReportData(
                BatteryId: El(battery, "Id"),
                Manufacturer: El(battery, "Manufacturer"),
                SerialNumber: El(battery, "SerialNumber"),
                Chemistry: El(battery, "Chemistry"),
                DesignCapacity: AttrUint(battery!, "DesignCapacity"),
                FullChargeCapacity: AttrUint(battery!, "FullChargeCapacity"),
                CycleCount: AttrInt(battery!, "CycleCount"),
                CapacityHistory: history,
                RecentUsage: usage,
                ImportedAtUtc: DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Battery report XML parse failed");
            return null;
        }
    }

    private static string? El(XElement? parent, string localName)
        => parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;

    private static string? Attr(XElement? e, string name) =>
        e?.Attribute(name)?.Value is { Length: > 0 } v ? v : null;

    private static string? AttrOrChild(XElement e, string name) => Attr(e, name) ?? El(e, name);

    private static DateTime? AttrDate(XElement e, string name)
    {
        string? text = AttrOrChild(e, name);
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
            ? d
            : null;
    }

    private static uint? AttrUint(XElement e, string name)
    {
        string? text = Attr(e, name) ?? El(e, name);
        return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
    }

    private static int? AttrInt(XElement e, string name)
    {
        string? text = Attr(e, name) ?? El(e, name);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static long? AttrLong(XElement e, string name)
    {
        string? text = Attr(e, name) ?? El(e, name);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
