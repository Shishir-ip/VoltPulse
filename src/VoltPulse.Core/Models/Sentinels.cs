namespace VoltPulse.Core.Models;

/// <summary>
/// Sentinel values defined by the Windows battery IOCTL contract and Win32 power APIs.
/// These must be treated as "unknown", never as real numbers.
/// </summary>
public static class BatterySentinels
{
    public const uint UnknownCapacity = 0xFFFFFFFF;
    public const uint UnknownVoltage = 0xFFFFFFFF;
    public const int UnknownRate = unchecked((int)0x80000000);
    public const uint UnknownTime = 0xFFFFFFFF;

    /// <summary>Win32_Battery.EstimatedRunTime sentinel (seconds).</summary>
    public const int UnknownRunTimeSeconds = 0x7FFFFFFF;

    /// <summary>GetSystemPowerStatus.BatteryLifePercent / BatteryFlag "unknown" sentinel.</summary>
    public const byte UnknownPercentByte = 255;

    public static bool IsKnownRate(int rateMw) => rateMw != UnknownRate;
    public static bool IsKnownVoltage(uint voltageMv) => voltageMv != UnknownVoltage && voltageMv != 0;
    public static bool IsKnownCapacity(uint capacityMwh) => capacityMwh != UnknownCapacity && capacityMwh != 0;
    public static bool IsKnownTime(uint seconds) => seconds != UnknownTime && seconds != 0;
    public static bool IsKnownRunTime(long seconds) => seconds != UnknownRunTimeSeconds && seconds > 0;
}
