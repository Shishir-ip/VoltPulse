using System.Globalization;
using System.Reflection;
using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Models;

namespace VoltPulse.Providers;

#pragma warning disable CA1305 // diagnostics text only

/// <summary>
/// Builds a full raw dump of every field from every provider - the "console/debug view"
/// used by the diagnostics page and the --diagnostics CLI mode.
/// </summary>
public static class DiagnosticsDump
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Build(IEnumerable<IBatteryProvider> providers, IAggregatedBatteryProvider? aggregated)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"VoltPulse diagnostics");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"OS        : {Environment.OSVersion.VersionString}");
        sb.AppendLine($"Runtime   : {Environment.Version}");
        sb.AppendLine($"Is 64-bit : {Environment.Is64BitProcess}");
        sb.AppendLine($"Machine   : {Environment.MachineName}");
        sb.AppendLine("");

        foreach (var provider in providers)
        {
            sb.AppendLine(new string('-', 60));
            IReadOnlyList<BatteryData> batteries = [];
            try
            {
                batteries = provider.ReadAll();
                sb.AppendLine($"  Available: {provider.IsAvailable}   LastError: {provider.LastError ?? "(none)"}   Batteries: {batteries.Count}");
                if (batteries.Count == 0)
                {
                    sb.AppendLine("  (no batteries)");
                }
                foreach (var battery in batteries)
                {
                    sb.AppendLine($"  Battery: {battery.Id}");
                    foreach (var prop in typeof(BatteryData).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (prop.GetIndexParameters().Length > 0) continue;
                        if (prop.Name == nameof(BatteryData.Id)) continue;
                        sb.AppendLine($"    {prop.Name,-28} {FormatValue(prop.GetValue(battery))}");
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  ERROR: {ex.Message}");
            }
            sb.AppendLine("");
        }

        if (aggregated is not null)
        {
            sb.AppendLine(new string('=', 60));
            sb.AppendLine("Merged snapshot");
            try
            {
                var snapshot = aggregated.ReadSnapshot();
                sb.AppendLine($"  Batteries merged : {snapshot.Batteries.Count}");
                foreach (var b in snapshot.Batteries)
                {
                    sb.AppendLine($"  [{b.Id}] state={b.ChargeState.Value}, percent={FormatValue(b.Percent)}, rate={FormatValue(b.RateMilliwatts)}, voltage={FormatValue(b.VoltageMillivolts)}");
                }
                sb.AppendLine("  Provider statuses:");
                foreach (var p in snapshot.ProviderStatuses)
                {
                    sb.AppendLine($"    {p.Name,-28} available={p.Available} error={p.LastError ?? "(none)"}");
                }
                var system = snapshot.System;
                sb.AppendLine($"  PC: {system.MachineManufacturer} {system.MachineModel}");
                sb.AppendLine($"  OS: {system.OsCaption} build {system.OsBuild}");
                sb.AppendLine($"  Power plan: {system.ActivePowerPlan ?? "(unknown)"}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  ERROR: {ex.Message}");
            }
        }

        return sb.ToString();
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "(null)",
            System.Collections.IEnumerable and not string => "(collection)",
            _ => value.ToString() ?? "(empty)",
        };
    }
}
