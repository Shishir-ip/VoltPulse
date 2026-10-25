using Microsoft.Extensions.Hosting;
using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Models;

namespace VoltPulse.Services;

/// <summary>
/// Hosted wrapper around <see cref="AlertEvaluator"/>: feeds it snapshots from the sampler and
/// forwards fired alerts to the presenter. Runs on the sampling thread; the presenter marshals.
/// </summary>
public sealed class AlertEngine : IHostedService, IDisposable
{
    private readonly SamplerService _sampler;
    private readonly SettingsService _settings;
    private readonly IAlertPresenter _presenter;
    private readonly Serilog.ILogger _logger;
    private AlertEvaluator _evaluator;

    public AlertEngine(SamplerService sampler, SettingsService settings, IAlertPresenter presenter, Serilog.ILogger logger)
    {
        _sampler = sampler;
        _settings = settings;
        _presenter = presenter;
        _logger = logger.ForContext<AlertEngine>();
        _evaluator = new AlertEvaluator(settings.Current.Alerts);
        _settings.Changed += s => _evaluator.UpdateRules(s.Alerts);
    }

    public void SnoozeAll(TimeSpan duration)
    {
        _evaluator.SnoozeAll(duration);
        _logger.Information("All alerts snoozed for {Duration}", duration);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sampler.SnapshotReady += OnSnapshot;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sampler.SnapshotReady -= OnSnapshot;
        return Task.CompletedTask;
    }

    private void OnSnapshot(BatterySnapshot snapshot)
    {
        try
        {
            var battery = snapshot.Combined ?? (snapshot.Batteries.Count > 0 ? snapshot.Batteries[0] : null);
            if (battery is null) return;
            var alert = _evaluator.Evaluate(snapshot.RetrievedAtUtc, battery, battery.HealthPercent());
            if (alert is not null)
            {
                _logger.Information("Alert fired: {Kind} - {Title}", alert.Kind, alert.Title);
                _presenter.Show(alert);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Alert evaluation failed");
        }
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
