using System.Globalization;
using System.Text;
using VoltPulse.Core.Formatting;
using VoltPulse.Core.Models;

namespace VoltPulse.Services;

/// <summary>Everything that goes into an export or share.</summary>
public sealed class ReportData
{
    public DateTime GeneratedUtc { get; init; } = DateTime.UtcNow;
    public SystemInfo? System { get; init; }
    public string? BatteryId { get; init; }
    public string? Manufacturer { get; init; }
    public string? DeviceName { get; init; }
    public string? SerialNumber { get; init; }
    public string? Chemistry { get; init; }
    public double? HealthPercent { get; init; }
    public uint? DesignCapacityMWh { get; init; }
    public uint? FullChargeCapacityMWh { get; init; }
    public int? CycleCount { get; init; }
    public double? Percent { get; init; }
    public ChargeState State { get; init; }
    public double? CurrentMa { get; init; }
    public double? VoltageMv { get; init; }
    public double? TemperatureC { get; init; }
    public IReadOnlyList<SessionRecord> Sessions { get; init; } = [];
    /// <summary>Recent samples for the chart (timestamped seconds since report start).</summary>
    public IReadOnlyList<BatterySample> ChartSamples { get; init; } = [];
    public bool IncludeSerial { get; init; }
}

/// <summary>History and report exports: CSV, JSON, HTML and a dependency-free PDF.</summary>
public static class ExportService
{
    public static void ExportCsv(IReadOnlyList<BatterySample> samples, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("timestamp_utc,battery_id,percent,voltage_mv,rate_mw,current_ma,power_mw,temp_c,state");
        foreach (var s in samples)
        {
            sb.Append(s.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(',')
              .Append(Esc(s.BatteryId)).Append(',')
              .Append(s.Percent?.ToString("0.0", CultureInfo.InvariantCulture) ?? "").Append(',')
              .Append(s.VoltageMv?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
              .Append(s.RateMw?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
              .Append(s.CurrentMa?.ToString("0.0", CultureInfo.InvariantCulture) ?? "").Append(',')
              .Append(s.PowerMw?.ToString("0.0", CultureInfo.InvariantCulture) ?? "").Append(',')
              .Append(s.TempC?.ToString("0.0", CultureInfo.InvariantCulture) ?? "").Append(',')
              .Append(s.State).AppendLine();
        }
        File.WriteAllText(path, sb.ToString());
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static void ExportJson(IReadOnlyList<BatterySample> samples, string path)
    {
        var payload = samples.Select(s => new
        {
            timestampUtc = s.TimestampUtc,
            batteryId = s.BatteryId,
            percent = s.Percent,
            voltageMv = s.VoltageMv,
            rateMw = s.RateMw,
            currentMa = s.CurrentMa,
            powerMw = s.PowerMw,
            tempC = s.TempC,
            state = s.State.ToString(),
        });
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(payload, JsonOpts));
    }

    public static void ExportHtml(ReportData report, string path)
    {
        File.WriteAllText(path, BuildHtml(report));
    }

    public static void ExportPdf(ReportData report, string path)
    {
        File.WriteAllBytes(path, BuildPdf(report));
    }

    // ---------- HTML ----------

    private static string BuildHtml(ReportData report)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html><html><head><meta charset="utf-8"><title>VoltPulse report</title>
            <style>
              body{font-family:'Segoe UI',sans-serif;background:#121212;color:#ececec;margin:0;padding:32px}
              .card{background:#1e1e1e;border:1px solid #2a2a2a;border-radius:12px;padding:18px;margin-bottom:16px;max-width:860px}
              h1{font-size:22px} h2{font-size:16px;color:#00bfa5}
              table{border-collapse:collapse;width:100%} td,th{padding:6px 10px;text-align:left;border-bottom:1px solid #2a2a2a;font-size:13px}
              .muted{color:#9e9e9e;font-size:12px}
            </style></head><body>
            """);
        sb.Append("<h1>VoltPulse battery report</h1>");
        sb.Append("<p class='muted'>Generated ").Append(Esc(report.GeneratedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))).Append("</p>");

        sb.Append("<div class='card'><h2>Device</h2><table>");
        AppendRow(sb, "PC", $"{report.System?.MachineManufacturer} {report.System?.MachineModel}");
        AppendRow(sb, "Windows", $"{report.System?.OsCaption} (build {report.System?.OsBuild})");
        AppendRow(sb, "Power plan", report.System?.ActivePowerPlan);
        AppendRow(sb, "Battery", $"{report.Manufacturer} {report.DeviceName}");
        AppendRow(sb, "Serial number", report.IncludeSerial ? report.SerialNumber : "(hidden)");
        AppendRow(sb, "Chemistry", report.Chemistry);
        sb.Append("</table></div>");

        sb.Append("<div class='card'><h2>Health</h2><table>");
        AppendRow(sb, "Health", report.HealthPercent is { } h ? $"{h:0.0}%" : "not available");
        AppendRow(sb, "Design capacity", report.DesignCapacityMWh is { } d ? $"{d:N0} mWh" : "not available");
        AppendRow(sb, "Full-charge capacity", report.FullChargeCapacityMWh is { } f ? $"{f:N0} mWh" : "not available");
        AppendRow(sb, "Cycle count", report.CycleCount?.ToString(CultureInfo.InvariantCulture) ?? "not available");
        sb.Append("</table></div>");

        sb.Append("<div class='card'><h2>Current status</h2><table>");
        AppendRow(sb, "Level", report.Percent is { } p ? $"{p:0}%" : "not available");
        AppendRow(sb, "State", report.State.ToString());
        AppendRow(sb, "Current", report.CurrentMa is { } ma ? $"{ma:0} mA" : "not available");
        AppendRow(sb, "Voltage", report.VoltageMv is { } mv ? $"{mv / 1000.0:0.000} V" : "not available");
        AppendRow(sb, "Temperature", report.TemperatureC is { } t ? $"{t:0.0} °C" : "not available");
        sb.Append("</table></div>");

        sb.Append("<div class='card'><h2>Recent current (last 24 h)</h2>");
        sb.Append(BuildSvg(report.ChartSamples, s => s.CurrentMa, "current (mA)"));
        sb.Append("</div>");

        sb.Append("<div class='card'><h2>Sessions</h2><table><tr><th>Type</th><th>Start</th><th>Duration</th><th>Level</th><th>Average current</th><th>Energy</th></tr>");
        foreach (var s in report.Sessions.Take(20))
        {
            sb.Append("<tr><td>").Append(s.Kind).Append("</td><td>")
              .Append(s.StartUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append("</td><td>")
              .Append(ValueFormatter.Duration((s.EndUtc ?? DateTime.UtcNow) - s.StartUtc)).Append("</td><td>")
              .Append(s.StartPercent is { } sp && s.EndPercent is { } ep ? $"{sp:0}% → {ep:0}%" : "—").Append("</td><td>")
              .Append(s.AvgCurrentMa is { } avg ? $"{avg:N0} mA" : "—").Append("</td><td>")
              .Append(s.EnergyMWh is { } en ? $"{en:N0} mWh" : "—").Append("</td></tr>");
        }
        sb.Append("</table></div>");
        sb.Append("<p class='muted'>Generated locally by VoltPulse. No data left this machine.</p></body></html>");
        return sb.ToString();

        static void AppendRow(StringBuilder b, string key, string? value)
            => b.Append("<tr><th style='text-align:left;width:180px'>").Append(Esc(key)).Append("</th><td>").Append(Esc(value ?? "—")).Append("</td></tr>");
    }

    private static string BuildSvg(IReadOnlyList<BatterySample> samples, Func<BatterySample, double?> pick, string label)
    {
        var values = samples.Select(pick).ToList();
        var points = new List<string>();
        double min = 0, max = 0;
        var valid = values.Where(v => v is { }).Select(v => v!.Value).ToList();
        if (valid.Count > 1)
        {
            min = Math.Min(0, valid.Min());
            max = valid.Max() * 1.1;
        }
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] is not { } v) continue;
            double x = 40 + i * (720.0 / Math.Max(1, values.Count - 1));
            double y = 150 - (max == min ? 0 : (v - min) / (max - min) * 130);
            points.Add($"{x:0.0},{y:0.0}");
        }
        string axis = $"<text x='2' y='20' fill='#9e9e9e' font-size='11'>{Esc(label)}</text>";
        if (points.Count < 2)
        {
            return $"<svg width='760' height='160'>{axis}<text x='40' y='80' fill='#666' font-size='12'>Not enough data yet</text></svg>";
        }
        return $"<svg width='760' height='160'>{axis}" +
               $"<polyline fill='none' stroke='#42a5f5' stroke-width='1.6' points='{string.Join(' ', points)}' />" +
               $"<line x1='40' y1='150' x2='760' y2='150' stroke='#2a2a2a' />" +
               $"<text x='2' y='154' fill='#666' font-size='10'>{min:0}</text>" +
               $"<text x='2' y='24' fill='#666' font-size='10'>{max:0}</text>" +
               "</svg>";
    }

    private static string Esc(string? text) => text is null
        ? string.Empty
        : text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // ---------- PDF (minimal writer, no dependencies) ----------

    private static byte[] BuildPdf(ReportData report)
    {
        var pdf = new SimplePdf();
        pdf.Text("VoltPulse battery report", 20, bold: true);
        pdf.Text($"Generated {report.GeneratedUtc.ToLocalTime():yyyy-MM-dd HH:mm} - all data stays on this machine", 9);
        pdf.Gap();

        pdf.Heading("Device");
        pdf.KeyValue("PC", $"{report.System?.MachineManufacturer} {report.System?.MachineModel}");
        pdf.KeyValue("Windows", $"{report.System?.OsCaption} (build {report.System?.OsBuild})");
        pdf.KeyValue("Power plan", report.System?.ActivePowerPlan);
        pdf.KeyValue("Battery", $"{report.Manufacturer} {report.DeviceName}".Trim());
        pdf.KeyValue("Serial number", report.IncludeSerial ? report.SerialNumber : "(hidden)");
        pdf.KeyValue("Chemistry", report.Chemistry);
        pdf.Gap();

        pdf.Heading("Health");
        pdf.KeyValue("Health", report.HealthPercent is { } h ? $"{h:0.0}%" : "not available");
        pdf.KeyValue("Design capacity", report.DesignCapacityMWh is { } d ? $"{d:N0} mWh" : "not available");
        pdf.KeyValue("Full-charge capacity", report.FullChargeCapacityMWh is { } f ? $"{f:N0} mWh" : "not available");
        pdf.KeyValue("Cycle count", report.CycleCount?.ToString(CultureInfo.InvariantCulture) ?? "not available");
        if (report.HealthPercent is { } hp && report.DesignCapacityMWh is { } dc)
        {
            pdf.Bar($"full-charge / design  ({hp:0.0}%)", hp / 100.0);
        }
        pdf.Gap();

        pdf.Heading("Current status");
        pdf.KeyValue("Level", report.Percent is { } p ? $"{p:0}%" : "not available");
        pdf.KeyValue("State", report.State.ToString());
        pdf.KeyValue("Current", report.CurrentMa is { } ma ? $"{ma:0} mA" : "not available");
        pdf.KeyValue("Voltage", report.VoltageMv is { } mv ? $"{mv / 1000.0:0.000} V" : "not available");
        pdf.KeyValue("Temperature", report.TemperatureC is { } t ? $"{t:0.0} C" : "not available");
        pdf.Gap();

        pdf.Heading("Recent sessions");
        foreach (var s in report.Sessions.Take(12))
        {
            pdf.Text(
                $"{s.Kind,-11} {s.StartUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {ValueFormatter.Duration((s.EndUtc ?? DateTime.UtcNow) - s.StartUtc),-9}  " +
                $"{(s.StartPercent is { } sp && s.EndPercent is { } ep ? $"{sp:0}% -> {ep:0}%" : "-"),-14}  " +
                $"{(s.AvgCurrentMa is { } avg ? $"{avg:N0} mA" : "-")}", 9);
        }
        if (report.Sessions.Count == 0)
        {
            pdf.Text("No sessions recorded yet.", 9);
        }
        pdf.Gap();
        pdf.Text("Chart: recent current (mA), oldest to newest:", 9);
        pdf.Chart(report.ChartSamples.Select(s => s.CurrentMa).ToList());
        pdf.Text("End of report.", 9);

        return pdf.Build();
    }

    private sealed class SimplePdf
    {
        private readonly List<string> _ops = [];
        private double _y = 792;

        public void Gap(double points = 10) => _y -= points;

        public void Text(string text, double size = 10, bool bold = false)
        {
            if (_y < 60)
            {
                NewPage();
            }
            string safe = Ascii(text);
            _ops.Add($"BT /{(bold ? "F2" : "F1")} {size.ToString("0.#", CultureInfo.InvariantCulture)} Tf 50 {_y:0.##} Td ({AsciiEscape(safe)}) Tj ET");
            _y -= size + 4;
        }

        public void Heading(string text) => Text(text, 13, bold: true);

        public void KeyValue(string key, string? value) => Text($"{key}: {value ?? "-"}", 10);

        public void Bar(string label, double fraction)
        {
            Text(label, 8);
            fraction = Math.Clamp(fraction, 0, 1);
            _ops.Add("0.9 0.9 0.9 rg 50 " + (_y - 4).ToString("0.##", CultureInfo.InvariantCulture) + " 300 8 re f");
            _ops.Add("0 0.75 0.65 rg 50 " + (_y - 4).ToString("0.##", CultureInfo.InvariantCulture) + " " + (300 * fraction).ToString("0.##", CultureInfo.InvariantCulture) + " 8 re f");
            _y -= 18;
        }

        public void Chart(List<double?> values)
        {
            var valid = values.Where(v => v is { }).Select(v => v!.Value).ToList();
            if (valid.Count < 2)
            {
                Text("Not enough data yet.", 8);
                return;
            }
            double min = Math.Min(0, valid.Min());
            double max = valid.Max() * 1.1;
            if (Math.Abs(max - min) < 0.001) max = min + 1;
            var points = new List<string>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is not { } v) continue;
                double x = 50 + i * (495.0 / Math.Max(1, values.Count - 1));
                double y = _y - 120 + (v - min) / (max - min) * 100;
                points.Add(string.Create(CultureInfo.InvariantCulture, $"{x:0.##} {y:0.##}"));
            }
            _ops.Add("0.26 0.65 0.96 RG 1 w " + string.Join(" ", points) + " S" /* polyline not available: build path */);
            // PDF has no polyline operator; emit as a path instead.
            _ops.RemoveAt(_ops.Count - 1);
            var path = new StringBuilder("0.26 0.65 0.96 RG 1 w ");
            for (int i = 0; i < points.Count; i++)
            {
                if (i > 0) _ = path.Append(' ');
                _ = path.Append(points[i]).Append(' ').Append(i == 0 ? 'm' : 'l');
            }
            _ = path.Append(" S");
            _ops.Add(path.ToString());
            _y -= 140;
        }

        private void NewPage()
        {
            _ops.Add("showpage");
            _y = 792;
        }

        public byte[] Build()
        {
            var content = string.Join("\n", _ops) + "\nshowpage";
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R /F2 6 0 R >> >> /Contents 4 0 R >>",
                string.Create(CultureInfo.InvariantCulture, $"<< /Length {content.Length} >>\nstream\n{content}\nendstream"),
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>",
            };

            var sb = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<int>();
            for (int i = 0; i < objects.Count; i++)
            {
                offsets.Add(sb.Length);
                sb.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }
            int xref = sb.Length;
            sb.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
            foreach (int offset in offsets)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
            }
            sb.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
            return Encoding.Latin1.GetBytes(sb.ToString());
        }

        private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

        private static string Ascii(string text) => new(text.Select(c =>
            c switch
            {
                >= ' ' and <= '~' => c,
                '°' => '*',
                '—' => '-',
                '–' => '-',
                '→' => '>',
                '·' => '-',
                _ => '?',
            }).ToArray());

        private static string AsciiEscape(string text) => Ascii(text).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }
}
