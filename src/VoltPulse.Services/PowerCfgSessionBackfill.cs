using VoltPulse.Core.Models;

namespace VoltPulse.Services;

/// <summary>
/// Reconstructs charge/discharge sessions from a powercfg battery report's recent-usage entries.
/// The report lists per-period entries (active/suspend, on AC or battery, with the energy drained
/// and the remaining capacity) - grouping consecutive same-mode entries yields sessions that
/// predate VoltPulse's own recording. Values are approximate and sessions are marked imported.
/// </summary>
public static class PowerCfgSessionBackfill
{
    public const string ImportedBatteryId = "IMPORTED";
    private static readonly TimeSpan MinRun = TimeSpan.FromMinutes(10);

    /// <param name="usage">Report usage entries, any order.</param>
    /// <param name="designedVoltageMv">Nominal voltage used to estimate average current (0 = skip current).</param>
    public static IReadOnlyList<SessionRecord> BuildSessions(IReadOnlyList<UsagePeriod> usage, int designedVoltageMv)
    {
        var ordered = usage.Where(u => u.Duration > TimeSpan.Zero).OrderBy(u => u.StartUtc).ToList();
        var sessions = new List<SessionRecord>();

        int i = 0;
        while (i < ordered.Count)
        {
            var mode = Classify(ordered[i], i > 0 ? ordered[i - 1] : null);
            if (mode is null)
            {
                i++;
                continue;
            }

            int start = i;
            while (i + 1 < ordered.Count)
            {
                var next = Classify(ordered[i + 1], ordered[i]);
                if (next != mode) break;
                i++;
            }

            var run = ordered[start..(i + 1)];
            var entryBefore = start > 0 ? ordered[start - 1] : null;
            i++;

            var session = BuildSession(mode.Value, run, entryBefore, designedVoltageMv);
            if (session is not null)
            {
                sessions.Add(session);
            }
        }

        return sessions;
    }

    /// <summary>
    /// Discharge = on battery. Charge = on AC with the remaining capacity rising. Everything else
    /// (idle on AC, suspend on AC with flat capacity) is a gap between sessions.
    /// </summary>
    private static ChargeState? Classify(UsagePeriod entry, UsagePeriod? previous)
    {
        if (!entry.OnAc) return ChargeState.Discharging;
        if (previous is { } prev
            && prev.EndCapacityMwh is { } prevCap
            && entry.EndCapacityMwh is { } cap
            && cap > prevCap + 50) // >0.05 kWh noise hysteresis
        {
            return ChargeState.Charging;
        }
        return null;
    }

    private static SessionRecord? BuildSession(ChargeState kind, List<UsagePeriod> run, UsagePeriod? entryBefore, int designedVoltageMv)
    {
        var startUtc = run[0].StartUtc;
        var endUtc = run[^1].StartUtc + run[^1].Duration;
        if (endUtc - startUtc < MinRun) return null;

        double durationHours = (endUtc - startUtc).TotalHours;
        double energyMWh = 0;
        double startPercent = double.NaN;
        double endPercent = double.NaN;
        bool first = true;

        foreach (var entry in run)
        {
            uint fcc = entry.FullChargeCapacityMwh ?? 0;

            if (kind == ChargeState.Discharging)
            {
                energyMWh += entry.DischargeMwh ?? 0;
            }

            if (fcc > 0 && entry.EndCapacityMwh is { } cap)
            {
                double endPct = Math.Clamp(cap * 100.0 / fcc, 0, 100);
                if (first)
                {
                    // Level when the run began: the previous entry's end, or for discharges the
                    // first entry's end plus what it drained.
                    double? beginCap = kind == ChargeState.Discharging && entry.DischargeMwh is { } drain
                        ? cap + drain
                        : entryBefore?.EndCapacityMwh;
                    if (beginCap is { } bc && bc > 0) startPercent = Math.Clamp(bc * 100.0 / fcc, 0, 100);
                    first = false;
                }
                if (kind == ChargeState.Charging)
                {
                    // Charging energy = the capacity rise during the entry.
                    double? beginCap = entryBefore?.EndCapacityMwh;
                    if (beginCap is { } bc2 && bc2 > 0 && cap > bc2)
                    {
                        energyMWh += cap - bc2;
                    }
                }
                endPercent = endPct;
            }
            entryBefore = entry;
        }

        if (double.IsNaN(startPercent)) startPercent = endPercent;
        if (double.IsNaN(endPercent)) endPercent = startPercent;

        double? avgCurrentMa = null;
        if (designedVoltageMv > 0 && durationHours > 0 && energyMWh > 0)
        {
            double avgPowerMw = energyMWh / durationHours; // mWh / h = mW
            avgCurrentMa = Math.Round(avgPowerMw / designedVoltageMv * 1000.0, 0);
        }

        return new SessionRecord
        {
            BatteryId = ImportedBatteryId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            Kind = kind,
            StartPercent = Math.Round(startPercent, 1),
            EndPercent = Math.Round(endPercent, 1),
            AvgCurrentMa = avgCurrentMa,
            PeakCurrentMa = null, // the report has no per-period peak
            EnergyMWh = Math.Round(energyMWh, 0),
            SampleCount = run.Count,
        };
    }
}
