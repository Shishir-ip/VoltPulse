using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using VoltPulse.Core.Models;
using VoltPulse.Providers;

namespace VoltPulse.Services;

/// <summary>
/// Background sampling engine. Reads the aggregated provider on a timer, raises
/// <see cref="SnapshotReady"/> on the sampling task (consumers marshal to their own context),
/// adapts the interval when the window is hidden, and can be kicked for immediate samples.
/// </summary>
public sealed class SamplerService : IHostedService, IDisposable
{
    private readonly IAggregatedBatteryProvider _provider;
    private readonly SettingsService _settings;
    private readonly Serilog.ILogger _logger;
    private readonly SemaphoreSlim _kick = new(0, 1);
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public event Action<BatterySnapshot>? SnapshotReady;

    public bool Paused { get; set; }
    public bool WindowVisible { get; set; } = true;

    public SamplerService(IAggregatedBatteryProvider provider, SettingsService settings, Serilog.ILogger logger)
    {
        _provider = provider;
        _settings = settings;
        _logger = logger.ForContext<SamplerService>();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _provider.BatterySetChanged += OnBatterySetChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    private void OnBatterySetChanged() => ForceSampleNow();

    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case Microsoft.Win32.PowerModes.Resume:
                // Handles/devices may be stale after sleep; re-initialize and sample immediately.
                _logger.Information("System resumed; re-initializing providers");
                try
                {
                    _provider.Invalidate();
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Provider re-initialization after resume failed");
                }
                ForceSampleNow();
                break;
            case Microsoft.Win32.PowerModes.StatusChange:
                // AC plugged/unplugged: sample right away so the UI reacts instantly.
                ForceSampleNow();
                break;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _provider.BatterySetChanged -= OnBatterySetChanged;
        _cts?.Cancel();
        if (_loop is not null)
        {
            await Task.WhenAny(_loop, Task.Delay(2000, cancellationToken)).ConfigureAwait(false);
        }
    }

    public void SetPaused(bool paused) => Paused = paused;

    public void ForceSampleNow()
    {
        try
        {
            _kick.Release();
        }
        catch (SemaphoreFullException)
        {
            // a kick is already pending
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        _logger.Information("Sampler started (interval {Interval}s)", _settings.Current.EffectiveSamplingInterval());
        while (!ct.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (!Paused)
                {
                    var snapshot = _provider.ReadSnapshot();
                    SnapshotReady?.Invoke(snapshot);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Sampling tick failed");
            }

            double interval = WindowVisible
                ? _settings.Current.EffectiveSamplingInterval()
                : _settings.Current.BackgroundSamplingIntervalSeconds;
            double delaySec = Math.Max(0.05, interval - sw.Elapsed.TotalSeconds);

            try
            {
                bool kicked = await _kick.WaitAsync(TimeSpan.FromSeconds(delaySec), ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) break;
                if (kicked) continue; // sample immediately
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        _logger.Information("Sampler stopped");
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _kick.Dispose();
    }
}
