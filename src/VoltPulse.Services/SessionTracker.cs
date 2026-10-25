using VoltPulse.Core.Models;

namespace VoltPulse.Services;

/// <summary>
/// Detects charge/discharge sessions from the merged snapshot stream (combined view).
/// State changes are debounced so a single flickery sample cannot split a session.
/// Pure logic - unit-testable without any infrastructure.
/// </summary>
public sealed class SessionTracker
{
    private const int LeavingDebounce = 2;
    private const int MinSessionSeconds = 30;

    private ChargeState _state = ChargeState.Unknown;
    private SessionRecord? _open;
    private int _leavingCount;
    private DateTime _lastUtc;
    private double? _lastPercent;
    private bool _hasLast;

    /// <summary>The session currently being recorded, if any (may still change).</summary>
    public SessionRecord? OpenSession => _open;

    /// <summary>Feeds one sample; returns a completed session when one just ended, otherwise null.</summary>
    public SessionRecord? OnSample(DateTime utcNow, ChargeState state, double? percent, int? rateMw, double? currentMa)
    {
        SessionRecord? completed = null;
        double dt = _hasLast ? Math.Clamp((utcNow - _lastUtc).TotalSeconds, 0, 30) : 0;

        if (state is ChargeState.Charging or ChargeState.Discharging)
        {
            if (_open is null)
            {
                StartSession(utcNow, state, percent);
            }
            else if (_open.Kind != state)
            {
                if (++_leavingCount >= LeavingDebounce)
                {
                    completed = CloseSession(utcNow, percent);
                    StartSession(utcNow, state, percent);
                }
            }
            else
            {
                _leavingCount = 0;
            }

            if (_open is not null)
            {
                _open.EndUtc = utcNow;
                _open.EndPercent = percent;
                _open.SampleCount++;
                if (currentMa is { } ma)
                {
                    double abs = Math.Abs(ma);
                    _open.AvgCurrentMa = ((_open.AvgCurrentMa ?? 0) * (_open.SampleCount - 1) + abs) / _open.SampleCount;
                    _open.PeakCurrentMa = Math.Max(_open.PeakCurrentMa ?? 0, abs);
                }
                if (rateMw is { } rate && dt > 0)
                {
                    _open.EnergyMWh = (_open.EnergyMWh ?? 0) + Math.Abs(rate) * dt / 3600.0;
                }
            }
        }
        else
        {
            // idle / full / unknown: after a short debounce, close the open session
            if (_open is not null && ++_leavingCount >= LeavingDebounce)
            {
                completed = CloseSession(utcNow, percent);
            }
        }

        _state = state;
        _lastUtc = utcNow;
        _lastPercent = percent;
        _hasLast = true;
        return completed;
    }

    /// <summary>Closes and returns any open session (app shutdown).</summary>
    public SessionRecord? FlushOpen()
    {
        if (_open is null) return null;
        _leavingCount = LeavingDebounce;
        return CloseSession(DateTime.UtcNow, _lastPercent);
    }

    private void StartSession(DateTime utcNow, ChargeState kind, double? percent)
    {
        _open = new SessionRecord
        {
            BatteryId = "COMBINED",
            StartUtc = utcNow,
            EndUtc = utcNow,
            Kind = kind,
            StartPercent = percent,
            EndPercent = percent,
        };
        _leavingCount = 0;
    }

    private SessionRecord? CloseSession(DateTime utcNow, double? endPercent)
    {
        var session = _open;
        _open = null;
        _leavingCount = 0;
        if (session is null) return null;
        session.EndUtc = utcNow;
        session.EndPercent = endPercent;
        double seconds = (session.EndUtc.Value - session.StartUtc).TotalSeconds;
        if (seconds < MinSessionSeconds) return null; // too short to be meaningful
        return session;
    }
}
