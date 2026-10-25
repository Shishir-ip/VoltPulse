namespace VoltPulse.Core.Models;

/// <summary>Identifies which data source supplied a value (shown in "About this value" tooltips).</summary>
public enum DataProviderSource
{
    None,
    Mock,
    Ioctl,
    Wmi,
    Win32Power,
    PowerCfg,
    Derived,
}

/// <summary>
/// A value plus the data source that supplied it. <typeparamref name="T"/> is expected to be a
/// nullable type (e.g. <see cref="Sourced{T}"/> of <c>int?</c>) so that "unknown" is representable.
/// </summary>
public sealed record Sourced<T>(T? Value, DataProviderSource Source = DataProviderSource.None, string? Note = null)
{
    public bool HasValue => Value is not null;

    public static Sourced<T> Unknown { get; } = new(default(T)!);

    public static Sourced<T> From(T value, DataProviderSource source, string? note = null) => new(value, source, note);

    public override string ToString() => HasValue ? $"{Value} [{Source}]" : "unknown";
}
