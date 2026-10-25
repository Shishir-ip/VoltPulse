using System.Diagnostics;
using VoltPulse.Core.Models;

namespace VoltPulse.Services;

/// <summary>Computed charging-habit insights (pure logic, unit-testable).</summary>
public sealed record BatteryInsights(
    int SessionsAnalyzed,
    double? ChargeToFullRatio,
    double? TimeAtFullPercent,
    int DeepDischarges,
    double? AverageDischargeDepth,
    int Score,
    IReadOnlyList<string> Tips);

public static class InsightsCalculator
{
    /// <param name="sessions">Sessions within the analysis window (oldest first not required).</param>
    /// <param name="samples">Samples within the window, for time-at-100% estimation.</param>
    /// <param name="window">The window length, to normalize time at full.</param>
    public static BatteryInsights Compute(IReadOnlyList<SessionRecord> sessions, IReadOnlyList<BatterySample> samples, TimeSpan window)
    {
        var charge = sessions.Where(s => s.Kind == ChargeState.Charging).ToList();
        var discharge = sessions.Where(s => s.Kind == ChargeState.Discharging).ToList();

        double? toFullRatio = charge.Count > 0
            ? charge.Count(s => (s.EndPercent ?? 0) >= 99) / (double)charge.Count
            : null;
        int deep = discharge.Count(s => (s.EndPercent ?? 100) <= 20);
        double? avgDepth = discharge.Count > 0
            ? discharge.Average(s => Math.Max(0, (s.StartPercent ?? 0) - (s.EndPercent ?? 0)))
            : null;

        double? timeAtFull = null;
        if (samples.Count > 0 && window > TimeSpan.Zero)
        {
            double atFull = samples.Count(s => (s.Percent ?? 0) >= 99.5);
            timeAtFull = atFull / samples.Count * window.TotalHours;
        }

        int score = 100;
        var tips = new List<string>();
        if (toFullRatio is { } ratio && ratio > 0.5 && charge.Count >= 2)
        {
            score -= 20;
            tips.Add("You charge to 100% most of the time. Stopping around 80% reduces long-term wear.");
        }
        if (timeAtFull is { } t && t > window.TotalHours * 0.15)
        {
            score -= 10;
            tips.Add("The battery spends a lot of time at 100%. Unplugging when full is gentler on the cells.");
        }
        if (deep > 0)
        {
            score -= Math.Min(30, deep * 5);
            tips.Add($"Deep discharges (below 20%) happened {deep} time(s). Charging a bit earlier is easier on the battery.");
        }
        if (avgDepth is { } depth && depth > 60)
        {
            score -= 10;
            tips.Add("Your typical discharge depth is large. Smaller top-ups between 20% and 80% extend lifespan.");
        }
        if (tips.Count == 0)
        {
            tips.Add("Your charging habits look good. Keeping the battery between 20% and 80% extends its lifespan.");
        }
        score = Math.Clamp(score, 0, 100);

        return new BatteryInsights(
            SessionsAnalyzed: sessions.Count,
            ChargeToFullRatio: toFullRatio,
            TimeAtFullPercent: timeAtFull,
            DeepDischarges: deep,
            AverageDischargeDepth: avgDepth,
            Score: score,
            Tips: tips);
    }
}

/// <summary>One row of the Power usage page (per-process CPU as a drain proxy).</summary>
public sealed record ProcessUsage(string Name, int Id, double CpuPercent, double WorkingSetMb)
{
    public string CpuText => CpuPercent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " %";
    public string MemoryText => WorkingSetMb.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
}

public static class PowerUsageScanner
{
    /// <summary>
    /// Approximates per-process power draw from CPU time deltas. Windows has no per-app mA,
    /// so this is explicitly an estimate.
    /// </summary>
    public static async Task<IReadOnlyList<ProcessUsage>> ScanAsync(int sampleMs = 800, int top = 15)
    {
        int cores = Math.Max(1, Environment.ProcessorCount);
        var snapshot = new List<(Process Process, TimeSpan Cpu, long WsBytes)>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                snapshot.Add((process, process.TotalProcessorTime, process.WorkingSet64));
            }
            catch
            {
                // access denied or process exited
            }
        }

        await Task.Delay(sampleMs).ConfigureAwait(false);

        var result = new List<ProcessUsage>();
        foreach (var (process, cpuStart, ws) in snapshot)
        {
            try
            {
                double cpuSeconds = (process.TotalProcessorTime - cpuStart).TotalSeconds;
                string name = process.ProcessName;
                int id = process.Id;
                process.Dispose();
                double percent = cpuSeconds / (cores * sampleMs / 1000.0) * 100.0;
                if (percent > 0.05)
                {
                    result.Add(new ProcessUsage(name, id, percent, ws / 1024.0 / 1024.0));
                }
            }
            catch
            {
                process.Dispose();
            }
        }
        return result.OrderByDescending(p => p.CpuPercent).Take(top).ToList();
    }
}
