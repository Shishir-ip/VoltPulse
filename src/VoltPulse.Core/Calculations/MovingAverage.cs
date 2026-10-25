namespace VoltPulse.Core.Calculations;

/// <summary>Short moving average used to smooth live readings. An unknown sample resets the window.</summary>
public sealed class MovingAverage
{
    private readonly int _window;
    private readonly Queue<double> _values = new();

    public MovingAverage(int window) => _window = Math.Max(1, window);

    public int Window => _window;

    public double? Push(double? value)
    {
        if (value is null)
        {
            _values.Clear();
            return null;
        }

        _values.Enqueue(value.Value);
        while (_values.Count > _window) _values.Dequeue();
        return _values.Average();
    }

    public void Reset() => _values.Clear();
}

/// <summary>
/// Rejects impossible one-tick jumps (more than <c>factor</c> times the previous value) while still
/// accepting genuine sustained changes after <c>confirmTicks</c> consecutive occurrences.
/// </summary>
public sealed class SpikeFilter
{
    private readonly double _factor;
    private readonly int _confirmTicks;
    private double? _lastValid;
    private double? _pending;
    private int _pendingCount;

    public SpikeFilter(double factor = BatteryMath.SpikeFactor, int confirmTicks = 3)
    {
        _factor = factor;
        _confirmTicks = Math.Max(1, confirmTicks);
    }

    /// <summary>Raised whenever a sample was rejected as a spike (for logging).</summary>
    public event Action<double, double>? SpikeRejected;

    public double? Push(double? value)
    {
        if (value is null)
        {
            _lastValid = null;
            _pending = null;
            _pendingCount = 0;
            return null;
        }

        if (_lastValid is null || !BatteryMath.IsSpike(_lastValid, value, _factor))
        {
            _lastValid = value;
            _pending = null;
            _pendingCount = 0;
            return value;
        }

        if (_pending.HasValue && Math.Abs(_pending.Value - value.Value) < double.Epsilon)
        {
            _pendingCount++;
        }
        else
        {
            _pending = value;
            _pendingCount = 1;
        }

        if (_pendingCount >= _confirmTicks)
        {
            _lastValid = value;
            _pending = null;
            _pendingCount = 0;
            return value;
        }

        SpikeRejected?.Invoke(_lastValid.Value, value.Value);
        return _lastValid;
    }

    public void Reset()
    {
        _lastValid = null;
        _pending = null;
        _pendingCount = 0;
    }
}
