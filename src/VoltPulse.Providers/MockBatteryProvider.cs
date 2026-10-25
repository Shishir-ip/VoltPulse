using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Calculations;
using VoltPulse.Core.Models;

namespace VoltPulse.Providers;

/// <summary>
/// Deterministic simulated battery used on machines without a real battery (demo mode) and for QA.
/// Starts discharging; toggle with <see cref="SetPluggedIn"/> to simulate the charger.
/// </summary>
public sealed class MockBatteryProvider : IBatteryProvider
{
    private const uint DesignCapacityMWh = 50000;
    private const uint FullChargeCapacityMWh = 43500; // 87% health

    private readonly object _gate = new();
    private readonly Random _random;
    private double _percent = 62.0;
    private bool _pluggedIn;
    private bool _hot;
    private DateTime _lastTickUtc = DateTime.UtcNow;

    public MockBatteryProvider(int seed = 20261025) => _random = new Random(seed);

    public string Name => "Mock (demo)";
    public bool IsAvailable => true;
    public string? LastError => null;

    public void SetPluggedIn(bool plugged) { lock (_gate) { _pluggedIn = plugged; } }
    public bool IsPluggedIn { get { lock (_gate) { return _pluggedIn; } } }
    public void SetHot(bool hot) { lock (_gate) { _hot = hot; } }
    public void ForcePercent(double percent) { lock (_gate) { _percent = Math.Clamp(percent, 0, 100); } }

    public IReadOnlyList<BatteryData> ReadAll()
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            double dt = (now - _lastTickUtc).TotalSeconds;
            if (dt is < 0 or > 10) dt = 1.0;
            _lastTickUtc = now;

            int voltageMv = (int)(10800 + _percent * 16); // ~10.8 V empty .. 12.4 V full
            double currentMa;
            if (_pluggedIn && _percent >= 99.9)
            {
                currentMa = 0;
            }
            else if (_pluggedIn)
            {
                currentMa = 1800 + _random.Next(-150, 150);
            }
            else
            {
                currentMa = -(650 + _random.Next(-120, 120) + (_hot ? 900 : 0));
            }

            int rateMw = (int)Math.Round(currentMa * voltageMv / 1000.0);
            _percent = Math.Clamp(_percent + rateMw / (double)FullChargeCapacityMWh * dt / 3600.0 * 100.0, 0, 100);

            double tempC = _hot
                ? 51 + _random.NextDouble() * 3
                : 32 + Math.Abs(currentMa) / 2000.0 * 4 + _random.NextDouble();
            tempC = Math.Round(tempC, 1);

            bool discharging = currentMa < 0;
            bool charging = currentMa > 0;
            var data = new BatteryData { Id = "MOCK-1" };
            data.DisplayName = Sourced<string?>.From("Demo battery", DataProviderSource.Mock);
            data.AcOnline = Sourced<bool?>.From(_pluggedIn, DataProviderSource.Mock);
            data.ChargeState = Sourced<ChargeState?>.From(
                BatteryMath.ResolveChargeState(_pluggedIn, charging, discharging, _percent),
                DataProviderSource.Mock);
            data.Percent = Sourced<double?>.From(Math.Round(_percent, 1), DataProviderSource.Mock);
            data.VoltageMillivolts = Sourced<int?>.From(voltageMv, DataProviderSource.Mock);
            data.RateMilliwatts = Sourced<int?>.From(rateMw, DataProviderSource.Mock);
            data.TemperatureCelsius = Sourced<double?>.From(tempC, DataProviderSource.Mock);
            data.Critical = Sourced<bool?>.From(_percent <= 10, DataProviderSource.Mock);
            data.DesignedCapacityMWh = Sourced<uint?>.From(DesignCapacityMWh, DataProviderSource.Mock);
            data.FullChargeCapacityMWh = Sourced<uint?>.From(FullChargeCapacityMWh, DataProviderSource.Mock);
            data.CurrentCapacityMWh = Sourced<uint?>.From((uint)(FullChargeCapacityMWh * _percent / 100.0), DataProviderSource.Mock);
            data.CycleCount = Sourced<int?>.From(342, DataProviderSource.Mock);
            data.Chemistry = Sourced<string?>.From("LION", DataProviderSource.Mock);
            data.Technology = Sourced<string?>.From("Rechargeable", DataProviderSource.Mock);
            data.Manufacturer = Sourced<string?>.From("VoltCell Energy", DataProviderSource.Mock);
            data.DeviceName = Sourced<string?>.From("VP-50 Standard", DataProviderSource.Mock);
            data.SerialNumber = Sourced<string?>.From("VPC-2026-0451", DataProviderSource.Mock);
            data.ManufactureDate = Sourced<DateTime?>.From(new DateTime(2023, 6, 15), DataProviderSource.Mock);

            if (discharging && rateMw < 0)
            {
                double capacityMwh = FullChargeCapacityMWh * _percent / 100.0;
                data.EstimatedRunTimeSeconds = Sourced<long?>.From(
                    (long)BatteryMath.TimeToEmptySeconds(capacityMwh, -rateMw)!, DataProviderSource.Mock);
            }

            data.DesignedVoltageMillivolts = Sourced<int?>.From(11600, DataProviderSource.Mock);
            return [data];
        }
    }

    public void Dispose()
    {
    }
}
