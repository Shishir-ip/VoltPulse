using System.Diagnostics;
using System.Text.RegularExpressions;
using VoltPulse.Core.Models;

namespace VoltPulse.Providers;

/// <summary>Caches static machine info (manufacturer, model, OS, active power plan).</summary>
public sealed partial class SystemInfoProvider
{
    private readonly Serilog.ILogger _logger;
    private readonly object _gate = new();
    private SystemInfo? _cached;
    private DateTime _cachedAtUtc = DateTime.MinValue;
    private string? _powerPlan;
    private DateTime _planAtUtc = DateTime.MinValue;

    public SystemInfoProvider(Serilog.ILogger logger) => _logger = logger.ForContext<SystemInfoProvider>();

    [GeneratedRegex(@"\(([^)]*)\)\s*$")]
    private static partial Regex PowerPlanRegex();

    public SystemInfo Get()
    {
        lock (_gate)
        {
            if (_cached is not null && DateTime.UtcNow - _cachedAtUtc < TimeSpan.FromMinutes(10))
            {
                if (_cached.ActivePowerPlan is null && DateTime.UtcNow - _planAtUtc > TimeSpan.FromMinutes(5))
                {
                    _powerPlan = ReadActivePowerPlan();
                    _planAtUtc = DateTime.UtcNow;
                    _cached.ActivePowerPlan = _powerPlan;
                }
                return _cached;
            }

            var info = new SystemInfo();
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "root\\CIMV2", "SELECT Manufacturer, Model FROM Win32_ComputerSystem");
                foreach (var o in searcher.Get())
                {
                    using (o)
                    {
                        info.MachineManufacturer = TryGetString(o, "Manufacturer");
                        info.MachineModel = TryGetString(o, "Model");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Win32_ComputerSystem query failed");
            }

            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "root\\CIMV2", "SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem");
                foreach (var o in searcher.Get())
                {
                    using (o)
                    {
                        info.OsCaption = TryGetString(o, "Caption");
                        info.OsVersion = TryGetString(o, "Version");
                        info.OsBuild = TryGetString(o, "BuildNumber");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Win32_OperatingSystem query failed");
            }

            if (DateTime.UtcNow - _planAtUtc > TimeSpan.FromMinutes(5))
            {
                _powerPlan = ReadActivePowerPlan();
                _planAtUtc = DateTime.UtcNow;
            }
            info.ActivePowerPlan = _powerPlan;

            _cached = info;
            _cachedAtUtc = DateTime.UtcNow;
            return info;
        }
    }

    private static string? TryGetString(System.Management.ManagementBaseObject o, string property)
    {
        try
        {
            return o[property]?.ToString() is { Length: > 0 } s ? s : null;
        }
        catch
        {
            return null;
        }
    }

    private string? ReadActivePowerPlan()
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg", "/getactivescheme")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return null;
            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000)) process.Kill(true);
            var match = PowerPlanRegex().Match(output);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "powercfg /getactivescheme failed");
            return null;
        }
    }
}
