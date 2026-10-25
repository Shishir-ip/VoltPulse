using VoltPulse.Core.Abstractions;

namespace VoltPulse.Core.Models;

/// <summary>Implemented by providers that can re-initialize themselves (after resume, device swap...).</summary>
public interface IRefreshable
{
    /// <summary>Drops cached handles/connections; the next read starts fresh.</summary>
    void Invalidate();
}
