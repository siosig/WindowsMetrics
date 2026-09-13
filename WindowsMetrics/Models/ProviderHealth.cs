namespace WindowsMetrics.Models;

/// <summary>
/// An immutable snapshot of one provider's connection health, produced by
/// <see cref="Services.Scraping.ScrapeHostedService"/> after each scrape attempt.
/// </summary>
public sealed record ProviderHealth(
    MetricsProvider Provider,
    bool IsOnline,
    DateTimeOffset? LastSuccess,
    DateTimeOffset? OfflineSince,
    string? LastError,
    int ConsecutiveFailures)
{
    /// <summary>The initial state before the first scrape attempt has completed.</summary>
    public static ProviderHealth CreateInitial(MetricsProvider provider) =>
        new(provider, IsOnline: false, LastSuccess: null, OfflineSince: null, LastError: null, ConsecutiveFailures: 0);

    /// <summary>Derives the next health state after a successful scrape.</summary>
    public ProviderHealth WithSuccess(DateTimeOffset at) =>
        this with { IsOnline = true, LastSuccess = at, OfflineSince = null, LastError = null, ConsecutiveFailures = 0 };

    /// <summary>Derives the next health state after a failed scrape.</summary>
    public ProviderHealth WithFailure(DateTimeOffset at, string error) =>
        this with
        {
            IsOnline = false,
            OfflineSince = OfflineSince ?? at,
            LastError = error,
            ConsecutiveFailures = ConsecutiveFailures + 1,
        };
}
