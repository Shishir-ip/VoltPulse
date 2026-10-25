using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Models;
using VoltPulse.Services;

namespace VoltPulse.App.ViewModels;

public sealed partial class AlertRuleRow : ObservableObject
{
    private readonly SettingsService _settings;

    public AlertRuleRow(SettingsService settings, AlertRuleSettings rule)
    {
        _settings = settings;
        Kind = rule.Id;
        Name = rule.Id switch
        {
            AlertKind.BatteryLow => "Battery low",
            AlertKind.BatteryCritical => "Battery critical",
            AlertKind.ChargeLimit => "Charge limit reached",
            AlertKind.FullyCharged => "Fully charged",
            AlertKind.HighTemperature => "High temperature",
            AlertKind.HighDrain => "Unusually high drain",
            AlertKind.HealthDropped => "Health dropped",
            AlertKind.ChargerConnected => "Charger connected",
            AlertKind.ChargerDisconnected => "Charger disconnected",
            AlertKind.SlowCharging => "Slow charging",
            _ => rule.Id.ToString(),
        };
        Description = rule.Id switch
        {
            AlertKind.BatteryLow => "Notify when the charge level falls to this percent while on battery.",
            AlertKind.BatteryCritical => "Urgent notification at a very low charge level.",
            AlertKind.ChargeLimit => "Notify while charging when the battery reaches this percent (unplug reminder).",
            AlertKind.FullyCharged => "Notify when the battery reaches 100%.",
            AlertKind.HighTemperature => "Notify when the battery temperature rises above this value (°C). Requires a temperature sensor.",
            AlertKind.HighDrain => "Notify when current draw exceeds this value (mA) for at least the rule duration.",
            AlertKind.HealthDropped => "Notify when battery health falls below this percent.",
            AlertKind.ChargerConnected => "Notify when the charger is plugged in.",
            AlertKind.ChargerDisconnected => "Notify when the charger is unplugged.",
            AlertKind.SlowCharging => "Notify when the charging current stays below this value (mA).",
            _ => "",
        };
        _enabled = rule.Enabled;
        _threshold = rule.Threshold;
        _cooldownMinutes = rule.CooldownMinutes;
        _playSound = rule.PlaySound;
        _thresholdText = rule.Threshold.ToString(CultureInfo.InvariantCulture);
        _cooldownText = rule.CooldownMinutes.ToString(CultureInfo.InvariantCulture);
    }

    public AlertKind Kind { get; }
    public string Name { get; }
    public string Description { get; }

    public bool HasThreshold => Kind
        is not (AlertKind.FullyCharged or AlertKind.ChargerConnected or AlertKind.ChargerDisconnected);
    public bool HasNoThreshold => !HasThreshold;

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private double _threshold;

    [ObservableProperty]
    private int _cooldownMinutes;

    [ObservableProperty]
    private bool _playSound;

    [ObservableProperty]
    private string _thresholdText;

    [ObservableProperty]
    private string _cooldownText;

    partial void OnEnabledChanged(bool value) => Save();
    partial void OnPlaySoundChanged(bool value) => Save();

    partial void OnThresholdTextChanged(string value) => WriteThreshold();
    partial void OnCooldownTextChanged(string value) => WriteCooldown();

    partial void OnThresholdChanged(double value) => Save();

    partial void OnCooldownMinutesChanged(int value) => Save();

    private void WriteThreshold()
    {
        if (double.TryParse(ThresholdText, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && Math.Abs(t - Threshold) > 0.0001)
        {
            Threshold = t;
        }
    }

    private void WriteCooldown()
    {
        if (int.TryParse(CooldownText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) && c != CooldownMinutes)
        {
            CooldownMinutes = c;
        }
    }

    private void Save() =>
        _settings.Update(s =>
        {
            var rule = s.Alerts.FirstOrDefault(r => r.Id == Kind);
            if (rule is null) return;
            rule.Enabled = Enabled;
            rule.Threshold = Threshold;
            rule.CooldownMinutes = Math.Clamp(CooldownMinutes, 0, 24 * 60);
            rule.PlaySound = PlaySound;
        });
}

public sealed partial class AlertsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly AlertEngine _alertEngine;
    private readonly IAlertPresenter _presenter;

    public ObservableCollection<AlertRuleRow> Rules { get; } = [];

    [ObservableProperty]
    private string _status = "";

    public AlertsViewModel(SettingsService settings, AlertEngine alertEngine, IAlertPresenter presenter)
    {
        _settings = settings;
        _alertEngine = alertEngine;
        _presenter = presenter;
        foreach (var rule in settings.Current.Alerts)
        {
            Rules.Add(new AlertRuleRow(settings, rule));
        }
    }

    [RelayCommand]
    private void SnoozeAll()
    {
        _alertEngine.SnoozeAll(TimeSpan.FromHours(1));
        Status = "All alerts are snoozed for one hour.";
    }

    [RelayCommand]
    private void TestNotification()
    {
        _presenter.Show(new AlertEvent(
            AlertKind.FullyCharged, "Test notification",
            "This is what VoltPulse alerts look like.", DateTime.UtcNow, PlaySound: true));
    }

    [RelayCommand]
    private void ResetRules()
    {
        _settings.Update(s => s.Alerts = AlertDefaults.All());
        Rules.Clear();
        foreach (var rule in _settings.Current.Alerts)
        {
            Rules.Add(new AlertRuleRow(_settings, rule));
        }
        Status = "Alert rules restored to defaults.";
    }
}
