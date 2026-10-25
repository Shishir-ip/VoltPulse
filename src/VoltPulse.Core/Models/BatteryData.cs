using VoltPulse.Core.Calculations;

namespace VoltPulse.Core.Models;

/// <summary>
/// Mutable bag of battery facts as reported by one provider, or merged from several providers.
/// Every factual field is <see cref="Sourced{T}"/> so the UI can show where each value came from.
/// </summary>
public sealed class BatteryData
{
    public string Id { get; set; } = string.Empty;

    public Sourced<string?> DisplayName { get; set; } = Sourced<string?>.Unknown;
    public Sourced<ChargeState?> ChargeState { get; set; } = Sourced<VoltPulse.Core.Models.ChargeState?>.Unknown;
    public Sourced<bool?> AcOnline { get; set; } = Sourced<bool?>.Unknown;
    public Sourced<double?> Percent { get; set; } = Sourced<double?>.Unknown;
    public Sourced<bool?> Critical { get; set; } = Sourced<bool?>.Unknown;

    public Sourced<int?> VoltageMillivolts { get; set; } = Sourced<int?>.Unknown;
    /// <summary>Positive while charging, negative while discharging, in mW.</summary>
    public Sourced<int?> RateMilliwatts { get; set; } = Sourced<int?>.Unknown;
    public Sourced<double?> TemperatureCelsius { get; set; } = Sourced<double?>.Unknown;

    public Sourced<uint?> DesignedCapacityMWh { get; set; } = Sourced<uint?>.Unknown;
    public Sourced<uint?> FullChargeCapacityMWh { get; set; } = Sourced<uint?>.Unknown;
    public Sourced<uint?> CurrentCapacityMWh { get; set; } = Sourced<uint?>.Unknown;
    public Sourced<int?> CycleCount { get; set; } = Sourced<int?>.Unknown;
    /// <summary>True when the cycle count is an estimate derived from energy throughput, not a hardware counter.</summary>
    public bool CycleCountEstimated { get; set; }

    public Sourced<string?> Chemistry { get; set; } = Sourced<string?>.Unknown;
    public Sourced<string?> Technology { get; set; } = Sourced<string?>.Unknown;
    public Sourced<string?> Manufacturer { get; set; } = Sourced<string?>.Unknown;
    public Sourced<string?> DeviceName { get; set; } = Sourced<string?>.Unknown;
    public Sourced<string?> SerialNumber { get; set; } = Sourced<string?>.Unknown;
    public Sourced<DateTime?> ManufactureDate { get; set; } = Sourced<DateTime?>.Unknown;
    public Sourced<long?> EstimatedRunTimeSeconds { get; set; } = Sourced<long?>.Unknown;
    public Sourced<int?> DesignedVoltageMillivolts { get; set; } = Sourced<int?>.Unknown;

    public double? CurrentMilliamps(bool invertSign = false)
    {
        var ma = BatteryMath.CurrentMilliamps(RateMilliwatts.Value, VoltageMillivolts.Value);
        if (ma is null) return null;
        return invertSign ? -ma.Value : ma.Value;
    }

    /// <summary>Instantaneous power in mW (identical to the signed rate).</summary>
    public double? PowerMilliwatts() => RateMilliwatts.Value;

    public double? HealthPercent() => BatteryMath.HealthPercent(FullChargeCapacityMWh.Value, DesignedCapacityMWh.Value);

    public double? WearPercent() => BatteryMath.WearPercent(HealthPercent());

    public double? DesignedCapacityMah() => BatteryMath.CapacityMah(DesignedCapacityMWh.Value, DesignedVoltageOrCurrentVoltageMv());

    public double? FullChargeCapacityMah() => BatteryMath.CapacityMah(FullChargeCapacityMWh.Value, DesignedVoltageOrCurrentVoltageMv());

    public double? CurrentCapacityMah() => BatteryMath.CapacityMah(CurrentCapacityMWh.Value, DesignedVoltageOrCurrentVoltageMv());

    public double? PercentPerHour() => BatteryMath.PercentPerHour(RateMilliwatts.Value, FullChargeCapacityMWh.Value);

    public int DesignedVoltageOrCurrentVoltageMv() =>
        DesignedVoltageMillivolts.Value ?? VoltageMillivolts.Value ?? 0;

    public BatteryLiveState LiveState(double hotThresholdC = 55, double criticalPercent = 10) =>
        BatteryMath.ResolveLiveState(ChargeState.Value ?? VoltPulse.Core.Models.ChargeState.Unknown, Percent.Value, TemperatureCelsius.Value, criticalPercent, hotThresholdC);

    /// <summary>Fills every field of this instance that is still unknown from <paramref name="other"/> (the higher-priority source wins).</summary>
    public void FillMissingFrom(BatteryData other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!DisplayName.HasValue) DisplayName = other.DisplayName;
        if (!ChargeState.HasValue) ChargeState = other.ChargeState;
        if (!AcOnline.HasValue) AcOnline = other.AcOnline;
        if (!Percent.HasValue) Percent = other.Percent;
        if (!Critical.HasValue) Critical = other.Critical;
        if (!VoltageMillivolts.HasValue) VoltageMillivolts = other.VoltageMillivolts;
        if (!RateMilliwatts.HasValue) RateMilliwatts = other.RateMilliwatts;
        if (!TemperatureCelsius.HasValue) TemperatureCelsius = other.TemperatureCelsius;
        if (!DesignedCapacityMWh.HasValue) DesignedCapacityMWh = other.DesignedCapacityMWh;
        if (!FullChargeCapacityMWh.HasValue) FullChargeCapacityMWh = other.FullChargeCapacityMWh;
        if (!CurrentCapacityMWh.HasValue) CurrentCapacityMWh = other.CurrentCapacityMWh;
        if (!CycleCount.HasValue) { CycleCount = other.CycleCount; CycleCountEstimated = other.CycleCountEstimated; }
        if (!Chemistry.HasValue) Chemistry = other.Chemistry;
        if (!Technology.HasValue) Technology = other.Technology;
        if (!Manufacturer.HasValue) Manufacturer = other.Manufacturer;
        if (!DeviceName.HasValue) DeviceName = other.DeviceName;
        if (!SerialNumber.HasValue) SerialNumber = other.SerialNumber;
        if (!ManufactureDate.HasValue) ManufactureDate = other.ManufactureDate;
        if (!EstimatedRunTimeSeconds.HasValue) EstimatedRunTimeSeconds = other.EstimatedRunTimeSeconds;
        if (!DesignedVoltageMillivolts.HasValue) DesignedVoltageMillivolts = other.DesignedVoltageMillivolts;
    }

    public BatteryData Clone() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        ChargeState = ChargeState,
        AcOnline = AcOnline,
        Percent = Percent,
        Critical = Critical,
        VoltageMillivolts = VoltageMillivolts,
        RateMilliwatts = RateMilliwatts,
        TemperatureCelsius = TemperatureCelsius,
        DesignedCapacityMWh = DesignedCapacityMWh,
        FullChargeCapacityMWh = FullChargeCapacityMWh,
        CurrentCapacityMWh = CurrentCapacityMWh,
        CycleCount = CycleCount,
        CycleCountEstimated = CycleCountEstimated,
        Chemistry = Chemistry,
        Technology = Technology,
        Manufacturer = Manufacturer,
        DeviceName = DeviceName,
        SerialNumber = SerialNumber,
        ManufactureDate = ManufactureDate,
        EstimatedRunTimeSeconds = EstimatedRunTimeSeconds,
        DesignedVoltageMillivolts = DesignedVoltageMillivolts,
    };
}
