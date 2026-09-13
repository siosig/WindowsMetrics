namespace WindowsMetrics.Models;

/// <summary>
/// The combined connection state shown by the single title-bar indicator,
/// derived from all providers' <see cref="ProviderHealth"/>.
/// </summary>
public enum ConnectionState
{
    /// <summary>Every provider answered its most recent scrape.</summary>
    AllOnline,

    /// <summary>At least one provider is online and at least one is offline.</summary>
    Degraded,

    /// <summary>No provider is online (including before the first scrape completes).</summary>
    AllOffline,
}
