using System.Runtime.InteropServices;
using System.Text;
using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Calculations;
using VoltPulse.Core.Models;
using VoltPulse.Providers.Win32;

namespace VoltPulse.Providers;

/// <summary>
/// Provider A: reads batteries through the Win32 battery IOCTL (SetupAPI enumeration,
/// IOCTL_BATTERY_QUERY_TAG/INFORMATION/STATUS). Most accurate source when the firmware
/// supports it. Keeps handles open and re-queries the tag when the battery is swapped.
/// </summary>
public sealed class IoctlBatteryProvider : IBatteryProvider, IRefreshable
{
    private const int MaxBatteries = 16;

    private sealed class DeviceState
    {
        public IntPtr Handle;
        public uint Tag;
        public Statics? Cached;
    }

    private sealed class Statics
    {
        public string? UniqueId;
        public string? DeviceName;
        public string? ManufacturerName;
        public string? SerialNumber;
        public string? Chemistry;
        public string? Technology;
        public DateTime? ManufactureDate;
        public uint Capabilities;
        public uint DesignedCapacity;
        public uint FullChargedCapacity;
    }

    private readonly Serilog.ILogger _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, DeviceState> _devices = new(StringComparer.OrdinalIgnoreCase);

    public IoctlBatteryProvider(Serilog.ILogger logger) => _logger = logger.ForContext<IoctlBatteryProvider>();

    public string Name => "Windows battery driver";
    public bool IsAvailable { get; private set; }
    public string? LastError { get; private set; }

    public IReadOnlyList<BatteryData> ReadAll()
    {
        lock (_gate)
        {
            var results = new List<BatteryData>();
            try
            {
                var paths = EnumerateBatteryDevicePaths();
                if (paths.Count == 0)
                {
                    SetError("No battery device interface present");
                    return results;
                }

                foreach (var device in _devices.Keys.Where(k => !paths.Contains(k, StringComparer.OrdinalIgnoreCase)).ToArray())
                {
                    CloseDevice(_devices[device]);
                    _devices.Remove(device);
                }

                int index = 0;
                foreach (string path in paths.Take(MaxBatteries))
                {
                    var data = ReadBattery(path, index);
                    if (data is not null)
                    {
                        results.Add(data);
                        index++;
                    }
                }
            }
            catch (Exception ex)
            {
                SetError(ex.Message);
                _logger.Debug(ex, "IOCTL read failed");
            }

            IsAvailable = results.Count > 0;
            if (IsAvailable) LastError = null;
            return results;
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            foreach (var state in _devices.Values)
            {
                CloseDevice(state);
            }
            _devices.Clear();
        }
    }

    private BatteryData? ReadBattery(string path, int index)
    {
        if (!_devices.TryGetValue(path, out var state))
        {
            state = new DeviceState();
            _devices[path] = state;
        }

        if (state.Handle == BatteryNative.InvalidHandle || state.Handle == IntPtr.Zero)
        {
            state.Handle = BatteryNative.CreateFileW(
                path, BatteryNative.GenericRead | BatteryNative.GenericWrite,
                BatteryNative.FileShareRead | BatteryNative.FileShareWrite,
                IntPtr.Zero, BatteryNative.OpenExisting, 0, IntPtr.Zero);
            if (state.Handle == BatteryNative.InvalidHandle)
            {
                LastError = $"Cannot open {Path.GetFileName(path)}: error {Marshal.GetLastWin32Error()}";
                return null;
            }
            state.Tag = 0;
            state.Cached = null;
        }

        if (state.Tag == 0 && !RefreshTag(state))
        {
            return null;
        }

        var status = new byte[16];
        if (!TryWithRetag(state, () => BatteryNative.TryQuery(
                state.Handle, BatteryNative.IoctlBatteryQueryStatus,
                EncodeWaitStatus(state.Tag), status)))
        {
            return null;
        }

        var info = new byte[32];
        if (!TryWithRetag(state, () => BatteryNative.TryQuery(
                state.Handle, BatteryNative.IoctlBatteryQueryInformation,
                EncodeQueryInfo(state.Tag, BatteryNative.BatteryInfoLevelInformation, 0), info)))
        {
            return null;
        }

        state.Cached ??= LoadStatics(state);

        uint powerState = BitConverter.ToUInt32(status, 0);
        uint capacity = BitConverter.ToUInt32(status, 4);
        int voltageMv = BitConverter.ToInt32(status, 8);
        int rateMw = BitConverter.ToInt32(status, 12);

        uint capabilities = BitConverter.ToUInt32(info, 0);
        if ((capabilities & BatteryNative.BatteryIsShortTerm) != 0)
        {
            return null; // UPS / short-term power source, not a system battery
        }

        var data = new BatteryData { Id = state.Cached?.UniqueId ?? $"IOCTL-{index + 1}" };
        const DataProviderSource src = DataProviderSource.Ioctl;

        bool acOnline = (powerState & BatteryNative.BatteryPowerOnLine) != 0;
        bool charging = (powerState & BatteryNative.BatteryCharging) != 0;
        bool discharging = (powerState & BatteryNative.BatteryDischarging) != 0;

        data.AcOnline = Sourced<bool?>.From(acOnline, src);
        data.ChargeState = Sourced<ChargeState?>.From(
            BatteryMath.ResolveChargeState(acOnline, charging, discharging, null), src);
        data.Critical = Sourced<bool?>.From((powerState & BatteryNative.BatteryCritical) != 0, src);

        if (BatterySentinels.IsKnownVoltage((uint)voltageMv))
        {
            data.VoltageMillivolts = Sourced<int?>.From(voltageMv, src);
        }
        if (BatterySentinels.IsKnownRate(rateMw))
        {
            data.RateMilliwatts = Sourced<int?>.From(rateMw, src);
        }

        uint designed = state.Cached?.DesignedCapacity ?? 0;
        uint full = state.Cached?.FullChargedCapacity ?? 0;

        if ((capabilities & BatteryNative.BatteryRelativeCapacity) != 0)
        {
            // capacity member carries a percentage (0-100) instead of mWh
            data.Percent = Sourced<double?>.From(Math.Clamp(capacity, 0, 100), src, "Relative capacity reported by the firmware");
        }
        else if (BatterySentinels.IsKnownCapacity(capacity))
        {
            data.CurrentCapacityMWh = Sourced<uint?>.From(capacity, src);
            if (full > 0)
            {
                data.Percent = Sourced<double?>.From(Math.Clamp(capacity * 100.0 / full, 0, 100), src);
            }
        }

        if (BatterySentinels.IsKnownCapacity(designed))
        {
            data.DesignedCapacityMWh = Sourced<uint?>.From(designed, src);
        }
        if (BatterySentinels.IsKnownCapacity(full))
        {
            data.FullChargeCapacityMWh = Sourced<uint?>.From(full, src);
        }

        if (state.Cached?.Chemistry is { Length: > 0 } chem)
        {
            data.Chemistry = Sourced<string?>.From(chem, src);
        }
        if (state.Cached?.Technology is { Length: > 0 } tech)
        {
            data.Technology = Sourced<string?>.From(tech, src);
        }
        if (state.Cached?.ManufacturerName is { Length: > 0 } mfr)
        {
            data.Manufacturer = Sourced<string?>.From(mfr, src);
        }
        if (state.Cached?.DeviceName is { Length: > 0 } name)
        {
            data.DeviceName = Sourced<string?>.From(name, src);
        }
        if (state.Cached?.SerialNumber is { Length: > 0 } sn)
        {
            data.SerialNumber = Sourced<string?>.From(sn, src);
        }
        if (state.Cached?.ManufactureDate is { } date)
        {
            data.ManufactureDate = Sourced<DateTime?>.From(date, src);
        }

        var tempBuf = new byte[4];
        if (BatteryNative.TryQuery(state.Handle, BatteryNative.IoctlBatteryQueryInformation,
                EncodeQueryInfo(state.Tag, BatteryNative.BatteryInfoLevelTemperature, 0), tempBuf) >= 4)
        {
            uint tenthsKelvin = BitConverter.ToUInt32(tempBuf, 0);
            if (tenthsKelvin is > 0 and < 20000) // 0..2000 K sanity window
            {
                data.TemperatureCelsius = Sourced<double?>.From(Math.Round(tenthsKelvin / 10.0 - 273.15, 1), src);
            }
        }

        var timeBuf = new byte[4];
        if (BatteryNative.TryQuery(state.Handle, BatteryNative.IoctlBatteryQueryInformation,
                EncodeQueryInfo(state.Tag, BatteryNative.BatteryInfoLevelEstimatedTime, 0), timeBuf) >= 4)
        {
            uint seconds = BitConverter.ToUInt32(timeBuf, 0);
            if (BatterySentinels.IsKnownTime(seconds))
            {
                data.EstimatedRunTimeSeconds = Sourced<long?>.From(seconds, src);
            }
        }

        return data;
    }

    private Statics? LoadStatics(DeviceState state)
    {
        var statics = new Statics();

        var info = new byte[32];
        if (BatteryNative.TryQuery(state.Handle, BatteryNative.IoctlBatteryQueryInformation,
                EncodeQueryInfo(state.Tag, BatteryNative.BatteryInfoLevelInformation, 0), info) > 0)
        {
            statics.Capabilities = BitConverter.ToUInt32(info, 0);
            statics.DesignedCapacity = BitConverter.ToUInt32(info, 8);
            statics.FullChargedCapacity = BitConverter.ToUInt32(info, 12);
            statics.Chemistry = DecodeChemistry(info);
            statics.Technology = info[4] switch
            {
                0 => "Non-rechargeable",
                1 => "Rechargeable",
                _ => null,
            };
        }

        statics.DeviceName = QueryString(state, BatteryNative.BatteryInfoLevelDeviceName);
        statics.ManufacturerName = QueryString(state, BatteryNative.BatteryInfoLevelManufactureName);
        statics.SerialNumber = QueryString(state, BatteryNative.BatteryInfoLevelSerialNumber);
        statics.UniqueId = QueryString(state, BatteryNative.BatteryInfoLevelUniqueId);
        statics.ManufactureDate = QueryManufactureDate(state);

        return statics;
    }

    private string? QueryString(DeviceState state, int level)
    {
        var buffer = new byte[256];
        if (!TryWithRetag(state, () => BatteryNative.TryQuery(state.Handle, BatteryNative.IoctlBatteryQueryInformation,
                EncodeQueryInfo(state.Tag, level, 0), buffer)))
        {
            return null;
        }
        string text = Encoding.Unicode.GetString(buffer, 0, Math.Max(0, bytesTerminated(buffer)));
        text = text.Trim('\0', ' ');
        return text.Length == 0 ? null : text;
    }

    private static int bytesTerminated(byte[] buffer)
    {
        for (int i = 0; i + 1 < buffer.Length; i += 2)
        {
            if (buffer[i] == 0 && buffer[i + 1] == 0) return i;
        }
        return buffer.Length;
    }

    private DateTime? QueryManufactureDate(DeviceState state)
    {
        var buffer = new byte[64];
        if (!TryWithRetag(state, () => BatteryNative.TryQuery(state.Handle, BatteryNative.IoctlBatteryQueryInformation,
                EncodeQueryInfo(state.Tag, BatteryNative.BatteryInfoLevelManufactureDate, 0), buffer)))
        {
            return null;
        }

        // Documented layout: UCHAR Day, UCHAR Month, USHORT Year (full year).
        if (buffer.Length >= 4)
        {
            int day = buffer[0];
            int month = buffer[1];
            int year = BitConverter.ToUInt16(buffer, 2);
            if (year is >= 1980 and <= 2100 && day is >= 1 and <= 31 && month is >= 1 and <= 12)
            {
                try
                {
                    return new DateTime(year, month, day);
                }
                catch
                {
                    // fall through to string fallback
                }
            }
            if (year is >= 0 and <= 99 && day is >= 1 and <= 31 && month is >= 1 and <= 12)
            {
                try
                {
                    return new DateTime(1980 + year, month, day);
                }
                catch
                {
                    // fall through to string fallback
                }
            }
        }

        // Some drivers return a formatted string instead of the structure.
        string text = Encoding.Unicode.GetString(buffer).Trim('\0', ' ');
        if (DateTime.TryParse(text, out var parsed) && parsed.Year is >= 1980 and <= 2100)
        {
            return parsed.Date;
        }
        return null;
    }

    private bool TryWithRetag(DeviceState state, Func<int> attempt)
    {
        int bytes = attempt();
        if (bytes > 0) return true;
        int error = Marshal.GetLastWin32Error();
        if (error is BatteryNative.ErrorNoSuchDevice or BatteryNative.ErrorInvalidParameter)
        {
            // tag changed (battery swap): re-query once and retry
            if (RefreshTag(state) && attempt() > 0) return true;
        }
        LastError = bytes == 0
            ? "Driver accepted the request but returned no data"
            : $"DeviceIoControl failed (error {error})";
        return false;
    }

    private bool RefreshTag(DeviceState state)
    {
        var output = new byte[4];
        if (BatteryNative.TryQuery(state.Handle, BatteryNative.IoctlBatteryQueryTag, [0, 0, 0, 0], output) != 4)
        {
            LastError = $"Cannot query battery tag (error {Marshal.GetLastWin32Error()})";
            return false;
        }
        uint tag = BitConverter.ToUInt32(output, 0);
        if (tag == 0)
        {
            LastError = "Battery tag is 0";
            return false;
        }
        state.Tag = tag;
        state.Cached = null; // statics belong to a specific battery
        return true;
    }

    private static byte[] EncodeWaitStatus(uint tag) => EncodeU32s(tag, 0, 0, 0);

    private static byte[] EncodeQueryInfo(uint tag, int level, int atRate) => EncodeU32s(tag, (uint)level, unchecked((uint)atRate));

    private static byte[] EncodeU32s(params uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
        {
            BitConverter.GetBytes(values[i]).CopyTo(bytes, i * 4);
        }
        return bytes;
    }

    private static string? DecodeChemistry(byte[] info)
    {
        // BATTERY_INFORMATION: Capabilities(4) Technology(1) Reserved(3) Chemistry(4) at offset 8
        const int offset = 8;
        var chars = new char[4];
        for (int i = 0; i < 4; i++)
        {
            chars[i] = (char)info[offset + i];
        }
        string text = new string(chars).Trim('\0', ' ');
        return text.Length == 0 ? null : text;
    }

    private static List<string> EnumerateBatteryDevicePaths()
    {
        var paths = new List<string>();
        var guid = BatteryNative.GuidDevclassBattery;
        IntPtr set = BatteryNative.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero,
            BatteryNative.DigcfPresent | BatteryNative.DigcfDeviceinterface);
        if (set == BatteryNative.InvalidHandle)
        {
            throw new InvalidOperationException($"SetupDiGetClassDevs failed (error {Marshal.GetLastWin32Error()})");
        }

        try
        {
            for (int i = 0; i < MaxBatteries; i++)
            {
                var did = new BatteryNative.SP_DEVICE_INTERFACE_DATA
                {
                    cbSize = Marshal.SizeOf<BatteryNative.SP_DEVICE_INTERFACE_DATA>(),
                };
                if (!BatteryNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == BatteryNative.ErrorNoMoreItems) break;
                    continue;
                }

                string? path = GetDevicePath(set, ref did);
                if (path is not null) paths.Add(path);
            }
        }
        finally
        {
            _ = BatteryNative.SetupDiDestroyDeviceInfoList(set);
        }
        return paths;
    }

    private static string? GetDevicePath(IntPtr set, ref BatteryNative.SP_DEVICE_INTERFACE_DATA did)
    {
        // Two-call pattern. cbSize for SP_DEVICE_INTERFACE_DETAIL_DATA_W differs between
        // 32-bit (6) and 64-bit (8); retry the other value if the first is rejected.
        uint[] candidates = IntPtr.Size == 8 ? [8u, 6u] : [6u, 8u];

        _ = BatteryNative.SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out uint required, IntPtr.Zero);
        if (Marshal.GetLastWin32Error() != BatteryNative.ErrorInsufficientBuffer || required < 8)
        {
            return null;
        }

        foreach (uint cbSize in candidates)
        {
            IntPtr buffer = Marshal.AllocHGlobal((int)required);
            try
            {
                Marshal.WriteInt32(buffer, 0, unchecked((int)cbSize));
                if (BatteryNative.SetupDiGetDeviceInterfaceDetail(set, ref did, buffer, required, out _, IntPtr.Zero))
                {
                    return Marshal.PtrToStringUni(buffer + 4)?.TrimEnd('\0');
                }
                int error = Marshal.GetLastWin32Error();
                if (error != BatteryNative.ErrorInvalidParameter)
                {
                    return null;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return null;
    }

    private void SetError(string message)
    {
        if (LastError != message)
        {
            _logger.Debug("IOCTL provider: {Message}", message);
        }
        LastError = message;
        IsAvailable = false;
    }

    private static void CloseDevice(DeviceState state)
    {
        if (state.Handle != BatteryNative.InvalidHandle && state.Handle != IntPtr.Zero)
        {
            _ = BatteryNative.CloseHandle(state.Handle);
            state.Handle = IntPtr.Zero;
            state.Tag = 0;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var state in _devices.Values)
            {
                CloseDevice(state);
            }
            _devices.Clear();
        }
    }
}
