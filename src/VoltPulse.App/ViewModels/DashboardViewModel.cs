using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Models;
using VoltPulse.Core.Paths;
using VoltPulse.Services;

namespace VoltPulse.App.ViewModels;

/// <summary>Fixed-capacity ring of doubles; missing samples push NaN so time stays aligned.</summary>
public sealed class RingSeries
{
    private readonly double[] _buffer;
    private int _head;
    public int Capacity { get; }
    public int Count { get; private set; }

    public RingSeries(int capacity)
    {
        Capacity = capacity;
        _buffer = new double[capacity];
    }

    public void Push(double? value)
    {
        _buffer[_head] = value ?? double.NaN;
        _head = (_head + 1) % _buffer.Length;
        if (Count < _buffer.Length) Count++;
    }

    /// <summary>Returns samples ordered oldest to newest.</summary>
    public double[] ToArray()
    {
        var result = new double[Count];
        int start = (_head - Count + _buffer.Length) % _buffer.Length;
        for (int i = 0; i < Count; i++)
        {
            result[i] = _buffer[(start + i) % _buffer.Length];
        }
        return result;
    }
}

public sealed partial class DashboardViewModel : ObservableObject
{
    private const int RingCapacity = 86400; // 24 h at 1 Hz

    private static readonly string[] SeriesNames = ["Current", "Power", "Voltage", "Level", "Temperature"];
    private static readonly string[] RangeNames = ["1 min", "10 min", "1 hour", "24 hours", "7 days", "30 days"];

    private readonly SamplerService _sampler;
    private readonly IHistoryStore _store;
    private readonly SettingsService _settings;
    private readonly Dispatcher _dispatcher;

    private readonly RingSeries _current = new(RingCapacity);
    private readonly RingSeries _power = new(RingCapacity);
    private readonly RingSeries _voltage = new(RingCapacity);
    private readonly RingSeries _level = new(RingCapacity);
    private readonly RingSeries _temperature = new(RingCapacity);
    private string? _primaryBatteryId;
    private List<BatterySample> _dbCache = [];
    private DateTime _dbCacheAtUtc = DateTime.MinValue;
    private readonly object _gate = new();

    public static IReadOnlyList<string> Series { get; } = SeriesNames;
    public static IReadOnlyList<string> Ranges { get; } = RangeNames;

    [ObservableProperty]
    private string _selectedSeries = "Current";

    [ObservableProperty]
    private string _selectedRange = "10 min";

    [ObservableProperty]
    private bool _paused;

    [ObservableProperty]
    private string _pauseLabel = "Pause";

    /// <summary>Set by the view; charts only refresh while the page is visible.</summary>
    [ObservableProperty]
    private bool _active;

    public event Action? DataArrived;

    public DashboardViewModel(SamplerService sampler, IHistoryStore store, SettingsService settings, Serilog.ILogger logger)
    {
        _sampler = sampler;
        _store = store;
        _settings = settings;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _sampler.SnapshotReady += OnSnapshot;
    }

    private void OnSnapshot(BatterySnapshot snapshot) =>
        _dispatcher.BeginInvoke(() =>
        {
            lock (_gate)
            {
                var battery = snapshot.Combined ?? (snapshot.Batteries.Count > 0 ? snapshot.Batteries[0] : null);
                if (battery is null) return;
                _primaryBatteryId = battery.Id == "COMBINED" ? (snapshot.Batteries.Count > 0 ? snapshot.Batteries[0].Id : battery.Id) : battery.Id;
                var settings = _settings.Current;
                double? current = battery.CurrentMilliamps(settings.InvertSignConvention);
                if (!Paused)
                {
                    _current.Push(current);
                    _power.Push(battery.PowerMilliwatts() is { } mw ? mw / 1000.0 : null);
                    _voltage.Push(battery.VoltageMillivolts.Value is { } mv ? mv / 1000.0 : null);
                    _level.Push(battery.Percent.Value);
                    _temperature.Push(battery.TemperatureCelsius.Value);
                }
            }
            DataArrived?.Invoke();
        });

    /// <summary>Builds the series to draw for the current selection (called from the UI timer).</summary>
    public Controls.TimeSeriesChart.SeriesData? BuildSeries(out string yLabel, out string xLabel)
    {
        yLabel = SelectedSeries switch
        {
            "Power" => "Power (W)",
            "Voltage" => "Voltage (V)",
            "Level" => "Charge level (%)",
            "Temperature" => "Temperature (°C)",
            _ => "Current (mA)",
        };
        xLabel = SelectedRange is "7 days" or "30 days" ? "hours before now" : "seconds before now";

        lock (_gate)
        {
            switch (SelectedRange)
            {
                case "1 min":
                case "10 min":
                case "1 hour":
                    double[] values = SelectedSeries switch
                    {
                        "Power" => _power.ToArray(),
                        "Voltage" => _voltage.ToArray(),
                        "Level" => _level.ToArray(),
                        "Temperature" => _temperature.ToArray(),
                        _ => _current.ToArray(),
                    };
                    int take = SelectedRange switch
                    {
                        "1 min" => 60,
                        "10 min" => 600,
                        _ => 3600,
                    };
                    if (values.Length > take)
                    {
                        var sliced = new double[take];
                        Array.Copy(values, values.Length - take, sliced, 0, take);
                        values = sliced;
                    }
                    return new Controls.TimeSeriesChart.SeriesData(SelectedSeries, values, _settings.Current.EffectiveSamplingInterval(), SeriesColor(SelectedSeries));

                case "24 hours":
                    EnsureDbCache(TimeSpan.FromHours(24), useHourly: false);
                    return FromDb(sample => SelectedSeries switch
                    {
                        "Power" => sample.PowerMw / 1000.0,
                        "Voltage" => sample.VoltageMv is { } v ? v / 1000.0 : null,
                        "Level" => sample.Percent,
                        "Temperature" => sample.TempC,
                        _ => sample.CurrentMa,
                    }, 1);

                case "7 days":
                case "30 days":
                    EnsureDbCache(TimeSpan.FromDays(SelectedRange == "7 days" ? 7 : 30), useHourly: true);
                    return FromDb(sample => SelectedSeries switch
                    {
                        "Power" => sample.PowerMw / 1000.0,
                        "Voltage" => null,
                        "Level" => sample.Percent,
                        "Temperature" => sample.TempC,
                        _ => sample.CurrentMa,
                    }, 3600);

                default:
                    return null;
            }
        }
    }

    private Controls.TimeSeriesChart.SeriesData FromDb(Func<BatterySample, double?> pick, double periodSeconds)
    {
        var values = new double[_dbCache.Count];
        for (int i = 0; i < _dbCache.Count; i++)
        {
            values[i] = pick(_dbCache[i]) ?? double.NaN;
        }
        return new Controls.TimeSeriesChart.SeriesData(SelectedSeries, values, periodSeconds, SeriesColor(SelectedSeries));
    }

    private void EnsureDbCache(TimeSpan span, bool useHourly)
    {
        var now = DateTime.UtcNow;
        if (now - _dbCacheAtUtc < TimeSpan.FromSeconds(30)) return;
        _dbCacheAtUtc = now;
        var from = now - span;
        var id = _primaryBatteryId;
        _dbCache = useHourly
            ? [.. _store.QueryHourly(from, now, id)]
            : [.. _store.Query(from, now, id)];
    }

    private static string SeriesColor(string series) => series switch
    {
        "Power" => "#FF9100",
        "Voltage" => "#66BB6A",
        "Level" => "#00BFA5",
        "Temperature" => "#FF5252",
        _ => "#42A5F5",
    };

    public string CurrentExportFolder()
    {
        try
        {
            return _settings.Current.ExportFolder is { } folder && Directory.Exists(folder)
                ? folder
                : AppPaths.ExportsDir;
        }
        catch
        {
            return AppPaths.ExportsDir;
        }
    }

    /// <summary>Explains why the chosen series may have no data (shown instead of an empty plot).</summary>
    public string BuildEmptyHint()
    {
        return SelectedSeries switch
        {
            "Current" => "No current data in this range - the driver reports no rate while plugged in " +
                         "(the battery idles on AC). Unplug to see discharge current, or switch Series to Level.",
            "Power" => "No power data in this range - the driver reports no rate while plugged in (idle on AC). " +
                       "Unplug to see discharge power, or switch Series to Level.",
            "Temperature" => "No temperature data in this range - this machine may not expose a battery temperature sensor.",
            "Voltage" => "No voltage data in this range yet.",
            "Level" => "No charge-level data in this range yet.",
            _ => "No data in this range yet.",
        };
    }

    [RelayCommand]
    private void TogglePause()
    {
        Paused = !Paused;
        PauseLabel = Paused ? "Resume" : "Pause";
    }
}
