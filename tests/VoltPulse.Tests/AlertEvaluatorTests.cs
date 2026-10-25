using VoltPulse.Core.Models;
using VoltPulse.Services;

namespace VoltPulse.Tests;

public class AlertEvaluatorTests
{
    private static BatteryData Battery(bool ac, double? percent = 50, ChargeState? state = null, double? temp = null, int? rateMw = null) =>
        new()
        {
            Id = "B1",
            AcOnline = Sourced<bool?>.From(ac, DataProviderSource.Mock),
            Percent = Sourced<double?>.From(percent, DataProviderSource.Mock),
            ChargeState = Sourced<ChargeState?>.From(state ?? (ac ? ChargeState.IdleOnAc : ChargeState.Discharging), DataProviderSource.Mock),
            TemperatureCelsius = Sourced<double?>.From(temp, DataProviderSource.Mock),
            RateMilliwatts = Sourced<int?>.From(rateMw, DataProviderSource.Mock),
            VoltageMillivolts = Sourced<int?>.From(11600, DataProviderSource.Mock),
            FullChargeCapacityMWh = Sourced<uint?>.From(40000, DataProviderSource.Mock),
            DesignedCapacityMWh = Sourced<uint?>.From(50000, DataProviderSource.Mock),
        };

    private static DateTime T(int minutes) => new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddMinutes(minutes);

    [Fact]
    public void BatteryLow_FiresWhenDischargingBelowThreshold()
    {
        var evaluator = new AlertEvaluator(AlertDefaults.All());
        var alert = evaluator.Evaluate(T(0), Battery(ac: false, percent: 15), healthPercent: 90);
        Assert.NotNull(alert);
        Assert.Equal(AlertKind.BatteryLow, alert!.Kind);
    }

    [Fact]
    public void BatteryLow_DoesNotFireWhileCharging()
    {
        var evaluator = new AlertEvaluator(AlertDefaults.All());
        var alert = evaluator.Evaluate(T(0), Battery(ac: true, percent: 15, state: ChargeState.Charging, rateMw: 5000), 90);
        Assert.Null(alert);
    }

    [Fact]
    public void Cooldown_PreventsSpam()
    {
        var evaluator = new AlertEvaluator(AlertDefaults.All());
        Assert.NotNull(evaluator.Evaluate(T(0), Battery(ac: false, percent: 15), 90));
        Assert.Null(evaluator.Evaluate(T(1), Battery(ac: false, percent: 14), 90));
        Assert.NotNull(evaluator.Evaluate(T(0).AddMinutes(31), Battery(ac: false, percent: 12), 90));
    }

    [Fact]
    public void Snooze_BlocksAllRules()
    {
        var evaluator = new AlertEvaluator(AlertDefaults.All());
        evaluator.SnoozeAll(TimeSpan.FromHours(1));
        Assert.Null(evaluator.Evaluate(T(0), Battery(ac: false, percent: 5), 90));
    }

    [Fact]
    public void ChargerConnected_FiresOnTransitionOnly()
    {
        var evaluator = new AlertEvaluator(AlertDefaults.All());
        Assert.Null(evaluator.Evaluate(T(0), Battery(ac: false, percent: 50), 90));
        var alert = evaluator.Evaluate(T(1), Battery(ac: true, percent: 50, rateMw: 5000, state: ChargeState.Charging), 90);
        Assert.NotNull(alert);
        Assert.Equal(AlertKind.ChargerConnected, alert!.Kind);
        Assert.Null(evaluator.Evaluate(T(2), Battery(ac: true, percent: 51, rateMw: 5000, state: ChargeState.Charging), 90));
    }

    [Fact]
    public void HighDrain_RequiresSustainedDuration()
    {
        var rules = AlertDefaults.All();
        rules.First(r => r.Id == AlertKind.HighDrain).Enabled = true;
        rules.First(r => r.Id == AlertKind.HighDrain).Threshold = 3000;
        rules.First(r => r.Id == AlertKind.HighDrain).DurationSeconds = 10;
        var evaluator = new AlertEvaluator(rules);

        // sustained high drain, one sample per second
        AlertEvent? fired = null;
        for (int i = 0; i <= 12; i++)
        {
            fired ??= evaluator.Evaluate(T(0).AddSeconds(i), Battery(ac: false, percent: 50, rateMw: -45000), 90);
        }
        Assert.NotNull(fired);
        Assert.Equal(AlertKind.HighDrain, fired!.Kind);
    }

    [Fact]
    public void DisabledRule_NeverFires()
    {
        var rules = AlertDefaults.All();
        rules.First(r => r.Id == AlertKind.BatteryLow).Enabled = false;
        var evaluator = new AlertEvaluator(rules);
        // 15% is below the low threshold but above critical, so only the low rule could fire
        Assert.Null(evaluator.Evaluate(T(0), Battery(ac: false, percent: 15), 90));
    }

    [Fact]
    public void HighTemperature_FiresAboveThreshold()
    {
        var evaluator = new AlertEvaluator(AlertDefaults.All());
        var alert = evaluator.Evaluate(T(0), Battery(ac: false, percent: 50, temp: 48), 90);
        Assert.NotNull(alert);
        Assert.Equal(AlertKind.HighTemperature, alert!.Kind);
    }

    [Fact]
    public void HealthDropped_FiresBelowThreshold()
    {
        var evaluator = new AlertEvaluator(AlertDefaults.All());
        var alert = evaluator.Evaluate(T(0), Battery(ac: true, percent: 50), healthPercent: 75);
        Assert.NotNull(alert);
        Assert.Equal(AlertKind.HealthDropped, alert!.Kind);
    }

    [Fact]
    public void ChargeLimit_FiresWhenChargingPastThreshold()
    {
        var rules = AlertDefaults.All();
        rules.First(r => r.Id == AlertKind.ChargeLimit).Enabled = true;
        var evaluator = new AlertEvaluator(rules);
        var alert = evaluator.Evaluate(T(0), Battery(ac: true, percent: 85, state: ChargeState.Charging, rateMw: 4000), 90);
        Assert.NotNull(alert);
        Assert.Equal(AlertKind.ChargeLimit, alert!.Kind);
    }
}
