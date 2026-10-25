using System.Runtime.InteropServices;

namespace VoltPulse.Providers.Win32;

/// <summary>
/// P/Invoke declarations for the Windows battery contract (SetupAPI + battery IOCTLs +
/// GetSystemPowerStatus). Constants follow BatClass.h / setupapi.h.
/// </summary>
internal static class BatteryNative
{
    public static readonly Guid GuidDevclassBattery = new("72631E54-78A4-11D0-BCF7-00AA00B7B32A");

    public const uint DigcfPresent = 0x2;
    public const uint DigcfDeviceinterface = 0x10;

    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint FileShareRead = 0x1;
    public const uint FileShareWrite = 0x2;
    public const uint OpenExisting = 3;

    public const int ErrorInsufficientBuffer = 122;
    public const int ErrorInvalidParameter = 87;
    public const int ErrorNoSuchDevice = 2;
    public const int ErrorNoMoreItems = 259;
    public const int ErrorInvalidFunction = 1;

    // CTL_CODE(FILE_DEVICE_BATTERY=0x29, f, METHOD_BUFFERED, FILE_READ_ACCESS)
    public const uint IoctlBatteryQueryTag = 0x00294040;
    public const uint IoctlBatteryQueryStatus = 0x00294044;
    public const uint IoctlBatteryQueryInformation = 0x00294048;

    public const uint BatteryPowerOnLine = 0x1;
    public const uint BatteryDischarging = 0x2;
    public const uint BatteryCharging = 0x4;
    public const uint BatteryCritical = 0x8;

    public const uint BatterySystemBattery = 0x10000000;
    public const uint BatteryIsShortTerm = 0x20000000;
    public const uint BatteryRelativeCapacity = 0x40000000;

    public const int BatteryInfoLevelInformation = 0;
    public const int BatteryInfoLevelTemperature = 2;
    public const int BatteryInfoLevelEstimatedTime = 3;
    public const int BatteryInfoLevelDeviceName = 4;
    public const int BatteryInfoLevelManufactureDate = 5;
    public const int BatteryInfoLevelManufactureName = 6;
    public const int BatteryInfoLevelUniqueId = 7;
    public const int BatteryInfoLevelSerialNumber = 8;

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, int memberIndex,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr detailDataBuffer,
        uint detailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(
        IntPtr device, uint ioControlCode, IntPtr inBuffer, uint inBufferSize,
        IntPtr outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;      // 0 offline, 1 online, 255 unknown
        public byte BatteryFlag;       // 1 high, 2 low, 4 critical, 8 charging, 128 no battery, 255 unknown
        public byte BatteryLifePercent; // 0-100, 255 unknown
        public byte Reserved1;
        public uint BatteryLifeTime;   // seconds, 0xFFFFFFFF unknown
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS systemPowerStatus);

    public static readonly IntPtr InvalidHandle = new(-1);

    /// <summary>
    /// Runs DeviceIoControl and returns the number of bytes written into <paramref name="output"/>,
    /// or -1 when the call failed. Some OEM drivers "succeed" with 0 bytes written; callers must
    /// treat that as a failure so all-zero data never leaks into readings.
    /// </summary>
    public static unsafe int TryQuery(IntPtr device, uint ioControlCode, byte[]? input, byte[] output)
    {
        fixed (byte* inPtr = input is { Length: > 0 } ? input : Array.Empty<byte>())
        fixed (byte* outPtr = output)
        {
            bool ok = DeviceIoControl(
                device, ioControlCode,
                input is { Length: > 0 } ? (IntPtr)inPtr : IntPtr.Zero,
                (uint)(input?.Length ?? 0),
                (IntPtr)outPtr, (uint)output.Length,
                out uint returned, IntPtr.Zero);
            return ok ? (int)returned : -1;
        }
    }
}
