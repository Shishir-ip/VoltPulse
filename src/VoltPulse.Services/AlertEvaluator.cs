using VoltPulse.Core.Models;

namespace VoltPulse.Services;

/// <summary>
/// Pure alert rule evaluation - no UI, no timers. Feed one snapshot per tick and it returns an
/// alert when a rule fires. Handles cooldowns, snoozes, charger transitions and sustained
/// conditions (high drain, slow charging). Unit-testable in isolation.
/// </summary>
public sealed class AlertEvaluator
{
    private readonly Dictionary<AlertKind, AlertRuleSettings> _rules;
    private readonly Dictionary<AlertKind, DateTime> _lastFiredUtc = [];
    private readonly Dictionary<AlertKind, DateTime> _snoozedUntilUtc = [];

    private bool? _lastAcOnline;
    private ChargeState _lastState = ChargeState.Unknown;
    private double? _lastHealth;
    private DateTime? _highDrainStartUtc;
    private DateTime? _slowChargeStartUtc;

    public AlertEvaluator(IEnumerable<AlertRuleSettings> rules)
    {
        _rules = rules.ToDictionary(r => r.Id);
    }

    public void UpdateRules(IEnumerable<AlertRuleSettings> rules)
    {
        _rules.Clear();
        foreach (var rule in rules)
        {
            _rules[rule.Id] = rule;
        }
    }

    public void Snooze(AlertKind kind, TimeSpan duration) => _snoozedUntilUtc[kind] = DateTime.UtcNow + duration;

    public void SnoozeAll(TimeSpan duration)
    {
        foreach (var kind in _rules.Keys)
        {
            _snoozedUntilUtc[kind] = DateTime.UtcNow + duration;
        }
    }

    /// <summary>Evaluates all rules; returns at most one alert per tick, highest priority first.</summary>
    public AlertEvent? Evaluate(DateTime utcNow, BatteryData battery, double? healthPercent)
    {
        bool? ac = battery.AcOnline.Value;
        var state = battery.ChargeState.Value ?? ChargeState.Unknown;
        double? percent = battery.Percent.Value;
        double? tempC = battery.TemperatureCelsius.Value;
        double? currentMa = battery.CurrentMilliamps();

        // Transition detection must run even when rules are disabled, to keep the state fresh.
        bool chargerConnected = _lastAcOnline == false && ac == true;
        bool chargerDisconnected = _lastAcOnline == true && ac == false;
        bool becameFull = state == ChargeState.FullyCharged && _lastState != ChargeState.FullyCharged;
        _lastAcOnline = ac ?? _lastAcOnline;
        _lastState = state;
        _lastHealth = healthPercent ?? _lastHealth;

        var alert = TryFire(utcNow, AlertKind.BatteryCritical, "Battery critical",
            percent is { } p && ac == false && p <= GetThreshold(AlertKind.BatteryCritical),
            $"Battery at {percent:0}% - plug in now to avoid data loss.");

        alert ??= TryFire(utcNow, AlertKind.BatteryLow, "Battery low",
            percent is { } p2 && ac == false && p2 <= GetThreshold(AlertKind.BatteryLow) && state == ChargeState.Discharging,
            $"Battery at {percent:0}% - consider plugging in.");

        alert ??= TryFire(utcNow, AlertKind.HighTemperature, "Battery is hot",
            tempC is { } t && t >= GetThreshold(AlertKind.HighTemperature),
            $"Battery temperature is {tempC:0.0} °C.");

        alert ??= TrySustained(utcNow, AlertKind.HighDrain, "Unusually high drain",
            currentMa is { } ma && Math.Abs(ma) >= GetThreshold(AlertKind.HighDrain),
            ref _highDrainStartUtc,
            $"Current draw is {Math.Abs(currentMa ?? 0):N0} mA - higher than usual.");

        alert ??= TryFire(utcNow, AlertKind.ChargeLimit, "Charge limit reached",
            percent is { } p3 && state == ChargeState.Charging && p3 >= GetThreshold(AlertKind.ChargeLimit),
            $"Battery at {percent:0}% - unplugging now helps the battery live longer.");

        alert ??= TryFire(utcNow, AlertKind.FullyCharged, "Fully charged",
            becameFull, "The battery is fully charged. You can unplug the charger.");

        alert ??= TrySustained(utcNow, AlertKind.SlowCharging, "Slow charging detected",
            state == ChargeState.Charging && currentMa is { } c && c < GetThreshold(AlertKind.SlowCharging),
            ref _slowChargeStartUtc,
            $"Charging current is only {currentMa:N0} mA - the charger may be weak or the cable damaged.");

        alert ??= TryFire(utcNow, AlertKind.ChargerConnected, "Charger connected",
            chargerConnected, "The charger was plugged in.");

        alert ??= TryFire(utcNow, AlertKind.ChargerDisconnected, "Charger disconnected",
            chargerDisconnected, "Running on battery power now.");

        alert ??= TryFire(utcNow, AlertKind.HealthDropped, "Battery health dropped",
            healthPercent is { } h && h < GetThreshold(AlertKind.HealthDropped),
            $"Battery health is {healthPercent:0.0}% - below your threshold.");

        return alert;
    }

    private double GetThreshold(AlertKind kind) => _rules.TryGetValue(kind, out var rule) ? rule.Threshold : 0;

    private AlertEvent? TryFire(DateTime utcNow, AlertKind kind, string title, bool condition, string message)
    {
        if (!condition) return null;
        if (!_rules.TryGetValue(kind, out var rule) || !rule.Enabled) return null;
        if (_snoozedUntilUtc.TryGetValue(kind, out var snoozed) && utcNow < snoozed) return null;
        if (_lastFiredUtc.TryGetValue(kind, out var last) && utcNow - last < TimeSpan.FromMinutes(rule.CooldownMinutes)) return null;

        _lastFiredUtc[kind] = utcNow;
        return new AlertEvent(kind, title, message, utcNow, rule.PlaySound);
    }

    private AlertEvent? TrySustained(
        DateTime utcNow, AlertKind kind, string title, bool condition,
        ref DateTime? startUtc, string message)
    {
        if (!condition)
        {
            startUtc = null;
            return null;
        }
        startUtc ??= utcNow;
        int required = _rules.TryGetValue(kind, out var rule) ? rule.DurationSeconds : 10;
        if ((utcNow - startUtc.Value).TotalSeconds < required) return null;

        startUtc = null; // fire once per sustained episode
        return TryFire(utcNow, kind, title, condition: true, message);
    }
}
