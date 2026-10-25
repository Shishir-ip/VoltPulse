using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace VoltPulse.Services;

/// <summary>Capacity history imported from a powercfg /batteryreport XML (long-term health data).</summary>
public sealed record CapacityHistoryEntry(DateTime Date, uint FullChargeCapacity, uint DesignCapacity, int CycleCount);

public sealed record PowerCfgReportData(
    string? BatteryId,
    string? Manufacturer,
    string? SerialNumber,
    string? Chemistry,
    uint? DesignCapacity,
    uint? FullChargeCapacity,
    int? CycleCount,
    IReadOnlyList<CapacityHistoryEntry> CapacityHistory,
    DateTime ImportedAtUtc);

/// <summary>
/// Provider D: runs `powercfg /batteryreport /XML` and parses the capacity history so the Health
/// page can show long-term battery fade beyond what VoltPulse has recorded itself.
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

    /// <summary>Parses the XML defensively: anything missing or malformed degrades to null.</summary>
    public PowerCfgReportData? Parse(string xml)
    {
        try
        {
            XDocument doc = XDocument.Parse(xml);
            var battery = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Battery");
            var history = new List<CapacityHistoryEntry>();

            foreach (var entry in doc.Descendants().Where(e => e.Name.LocalName == "BatteryCycleEntry"))
            {
                var date = ParseDate(El(entry, "StartDate")) ?? ParseDate(entry.Attribute("StartDate")?.Value);
                uint? full = ParseUint(El(entry, "FullChargeCapacity")) ?? ParseUint(entry.Attribute("FullChargeCapacity")?.Value);
                uint? design = ParseUint(El(entry, "DesignCapacity")) ?? ParseUint(entry.Attribute("DesignCapacity")?.Value);
                int? cycles = ParseInt(El(entry, "CycleCount")) ?? ParseInt(entry.Attribute("CycleCount")?.Value);
                if (date is not null && full is > 0)
                {
                    history.Add(new CapacityHistoryEntry(date.Value, full.Value, design ?? 0, cycles ?? 0));
                }
            }
            history.Sort((a, b) => a.Date.CompareTo(b.Date));

            return new PowerCfgReportData(
                BatteryId: El(battery, "Id"),
                Manufacturer: El(battery, "Manufacturer"),
                SerialNumber: El(battery, "SerialNumber"),
                Chemistry: El(battery, "Chemistry"),
                DesignCapacity: ParseUint(El(battery, "DesignCapacity")),
                FullChargeCapacity: ParseUint(El(battery, "FullChargeCapacity")),
                CycleCount: ParseInt(El(battery, "CycleCount")),
                CapacityHistory: history,
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

    private static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static uint? ParseUint(string? text)
        => uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;

    private static int? ParseInt(string? text)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
}
