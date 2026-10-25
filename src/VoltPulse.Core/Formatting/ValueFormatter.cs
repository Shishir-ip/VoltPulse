using System.Globalization;

namespace VoltPulse.Core.Formatting;

/// <summary>Human-readable formatting for every value shown in the UI and exports.</summary>
public static class ValueFormatter
{
    public const string NotSupported = "Not supported by this device";
    public const string Measuring = "measuring...";

    public static string Current(double? ma)
    {
        if (ma is null) return NotSupported;
        double v = ma.Value;
        string sign = v < 0 ? "-" : string.Empty;
        double abs = Math.Abs(v);
        return abs >= 1000
            ? string.Create(CultureInfo.InvariantCulture, $"{sign}{abs / 1000.0:0.00} A")
            : string.Create(CultureInfo.InvariantCulture, $"{sign}{abs:0} mA");
    }

    public static string Power(double? mw)
    {
        if (mw is null) return NotSupported;
        double abs = Math.Abs(mw.Value);
        return abs >= 1000
            ? string.Create(CultureInfo.InvariantCulture, $"{mw.Value / 1000.0:0.00} W")
            : string.Create(CultureInfo.InvariantCulture, $"{mw.Value:0} mW");
    }

    public static string Voltage(double? mv)
        => mv is null ? NotSupported : string.Create(CultureInfo.InvariantCulture, $"{mv.Value / 1000.0:0.000} V");

    public static string Percent(double? p)
        => p is null ? NotSupported : string.Create(CultureInfo.InvariantCulture, $"{p.Value:0}%");

    public static string Health(double? h)
        => h is null ? NotSupported : string.Create(CultureInfo.InvariantCulture, $"{h.Value:0.0}%");

    public static string Temperature(double? celsius, bool fahrenheit)
    {
        if (celsius is null) return NotSupported;
        return fahrenheit
            ? string.Create(CultureInfo.InvariantCulture, $"{celsius.Value * 9.0 / 5.0 + 32.0:0.0} °F")
            : string.Create(CultureInfo.InvariantCulture, $"{celsius.Value:0.0} °C");
    }

    public static string Capacity(double? mwh)
        => mwh is null ? NotSupported : string.Create(CultureInfo.InvariantCulture, $"{mwh.Value:N0} mWh");

    public static string CycleCount(int? cycles, bool estimated)
        => cycles is null
            ? NotSupported
            : estimated ? $"{cycles.Value} (estimated)" : $"{cycles.Value}";

    public static string Duration(double? seconds)
    {
        if (seconds is null) return NotSupported;
        return Duration(TimeSpan.FromSeconds(seconds.Value));
    }

    public static string Duration(TimeSpan? t)
    {
        if (t is null) return NotSupported;
        var span = t.Value;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalDays >= 2) return $"{(int)span.TotalDays} d {span.Hours} h";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours} h {span.Minutes:00} m";
        if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes} m {span.Seconds:00} s";
        return $"{(int)span.TotalSeconds} s";
    }

    public static string Date(DateTime? date)
        => date is null ? NotSupported : date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string SignedPercentPerHour(double? pph)
    {
        if (pph is null) return NotSupported;
        string sign = pph.Value < 0 ? "" : "+";
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{pph.Value:0.0}%/h");
    }
}
