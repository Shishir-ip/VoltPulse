namespace VoltPulse.Core.Models;

/// <summary>A user-facing alert that a rule fired.</summary>
public sealed record AlertEvent(
    AlertKind Kind,
    string Title,
    string Message,
    DateTime TimeUtc,
    bool PlaySound);
