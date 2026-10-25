using VoltPulse.Core.Models;

namespace VoltPulse.Core.Calculations;

public static class BatteryMath
{
    /// <summary>Reject a new rate sample when it exceeds this multiple of the previous valid value.</summary>
    public const double SpikeFactor = 10.0;

    /// <summary>
    /// Converts the Windows-reported power flow into current. Windows reports rate in mW, not mA:
    /// current_mA = rate_mW / voltage_mV * 1000. Sign follows the rate: negative discharging, positive charging.
    /// </summary>
    public static double? CurrentMilliamps(int? rateMw, int? voltageMv)
    {
        if (rateMw is null || voltageMv is null || voltageMv <= 0) return null;
        return rateMw.Value * 1000.0 / voltageMv.Value;
    }

    public static double? HealthPercent(double? fullChargeMwh, double? designMwh)
    {
        if (fullChargeMwh is not > 0 || designMwh is not > 0) return null;
        return ClampPercent(fullChargeMwh.Value / designMwh.Value * 100.0);
    }

    public static double? WearPercent(double? healthPercent)
        => healthPercent is null ? null : Math.Clamp(100.0 - healthPercent.Value, 0, 100);

    public static double? ClampPercent(double? percent)
        => percent is null ? null : Math.Clamp(percent.Value, 0, 100);

    /// <summary>Converts mWh to mAh using a voltage (note in the UI: uses nominal voltage when only that is known).</summary>
    public static double? CapacityMah(double? mwh, int? voltageMv)
    {
        if (mwh is null || mwh <= 0 || voltageMv is null || voltageMv <= 0) return null;
        return mwh.Value * 1000.0 / voltageMv.Value;
    }

    /// <summary>Charge/discharge speed in percent per hour. Signed: positive charging, negative discharging.</summary>
    public static double? PercentPerHour(double? rateMw, double? fullChargeMwh)
    {
        if (rateMw is null || fullChargeMwh is not > 0) return null;
        return rateMw.Value / fullChargeMwh.Value * 100.0;
    }

    /// <summary>Seconds until empty using the magnitude of the discharge rate (positive mW value).</summary>
    public static double? TimeToEmptySeconds(double? capacityMwh, double? dischargeRateMw)
    {
        if (capacityMwh is not > 0 || dischargeRateMw is not > 0) return null;
        return capacityMwh.Value / dischargeRateMw.Value * 3600.0;
    }

    public static double? TimeToFullSeconds(double? missingMwh, double? chargeRateMw)
    {
        if (missingMwh is null || missingMwh <= 0 || chargeRateMw is not > 0) return null;
        return missingMwh.Value / chargeRateMw.Value * 3600.0;
    }

    /// <summary>True when the candidate jumps more than <paramref name="factor"/> times the previous value.</summary>
    public static bool IsSpike(double? previous, double? candidate, double factor = SpikeFactor)
    {
        if (previous is null || candidate is null || previous == 0) return false;
        double p = Math.Abs(previous.Value);
        double c = Math.Abs(candidate.Value);
        if (p < 0.5) return false; // no meaningful baseline yet
        return c > p * factor;
    }

    /// <summary>Resolves the charge state from provider flags, tolerating missing flags.</summary>
    public static ChargeState ResolveChargeState(bool? acOnline, bool chargingFlag, bool dischargingFlag, double? percent)
    {
        if (chargingFlag) return ChargeState.Charging;
        if (dischargingFlag) return ChargeState.Discharging;
        if (acOnline == true) return percent is >= 99.5 ? ChargeState.FullyCharged : ChargeState.IdleOnAc;
        if (acOnline == false) return ChargeState.Discharging;
        return ChargeState.Unknown;
    }

    /// <summary>Picks the UI state color bucket. Critical/hot wins over everything else.</summary>
    public static BatteryLiveState ResolveLiveState(
        ChargeState state, double? percent, double? temperatureC,
        double criticalPercent = 10, double hotThresholdC = 55)
    {
        if ((percent is not null && percent <= criticalPercent) || (temperatureC is not null && temperatureC >= hotThresholdC))
        {
            return BatteryLiveState.Critical;
        }
        return state switch
        {
            ChargeState.Charging => BatteryLiveState.Charging,
            ChargeState.FullyCharged or ChargeState.IdleOnAc => BatteryLiveState.Full,
            ChargeState.Discharging => BatteryLiveState.Discharging,
            _ => BatteryLiveState.Unknown,
        };
    }
}
