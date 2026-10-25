using System.Runtime.InteropServices;
using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Calculations;
using VoltPulse.Core.Models;
using VoltPulse.Providers.Win32;

namespace VoltPulse.Providers;

/// <summary>
/// Provider C: basic facts from GetSystemPowerStatus. Always available on Windows, but coarse:
/// only AC state, percent, critical flag, charging flag and a rough run-time estimate.
/// </summary>
public sealed class Win32PowerProvider : IBatteryProvider
{
    private readonly Serilog.ILogger _logger;

    public Win32PowerProvider(Serilog.ILogger logger) => _logger = logger.ForContext<Win32PowerProvider>();

    public string Name => "Windows power API";
    public bool IsAvailable { get; private set; }
    public string? LastError { get; private set; }

    public IReadOnlyList<BatteryData> ReadAll()
    {
        if (!BatteryNative.GetSystemPowerStatus(out var status))
        {
            IsAvailable = false;
            LastError = $"GetSystemPowerStatus failed (error {Marshal.GetLastWin32Error()})";
            _logger.Debug("GetSystemPowerStatus failed: {Error}", LastError);
            return [];
        }

        if (status.BatteryFlag == 128 || status.BatteryLifePercent == BatterySentinels.UnknownPercentByte && status.BatteryFlag == 255)
        {
            // 128 = no system battery, 255 = unknown status
            if (status.BatteryFlag == 128)
            {
                IsAvailable = false;
                LastError = "No system battery";
                return [];
            }
        }

        const DataProviderSource src = DataProviderSource.Win32Power;
        var data = new BatteryData { Id = "SYSTEM-BATTERY" };

        bool? acOnline = status.ACLineStatus switch
        {
            1 => true,
            0 => false,
            _ => null,
        };
        data.AcOnline = Sourced<bool?>.From(acOnline, src);

        double? percent = status.BatteryLifePercent != BatterySentinels.UnknownPercentByte
            ? Math.Clamp((int)status.BatteryLifePercent, 0, 100)
            : null;
        if (percent is not null)
        {
            data.Percent = Sourced<double?>.From(percent, src);
        }

        bool charging = (status.BatteryFlag & 8) != 0;
        bool critical = (status.BatteryFlag & 4) != 0;
        data.Critical = Sourced<bool?>.From(critical, src);
        data.ChargeState = Sourced<ChargeState?>.From(
            BatteryMath.ResolveChargeState(acOnline, charging, dischargingFlag: acOnline == false, percent), src);

        if (status.BatteryLifeTime is not 0xFFFFFFFF and > 0)
        {
            data.EstimatedRunTimeSeconds = Sourced<long?>.From(status.BatteryLifeTime, src);
        }

        IsAvailable = true;
        LastError = null;
        return [data];
    }

    public void Dispose()
    {
    }
}
