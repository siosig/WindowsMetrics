namespace WindowsMetrics.Models;

/// <summary>
/// Identifies which provider produced a <see cref="Display.MetricsSnapshot"/> and whether that
/// scrape succeeded. Used to decide which trend series (if any) should advance when the
/// snapshot is applied -- a snapshot from a failed scrape, or one not produced by
/// <see cref="Services.Scraping.ScrapeHostedService"/> at all (e.g. in unit tests that call
/// <see cref="Services.MetricsParser.Parse"/> directly), must never advance a trend.
/// </summary>
/// <param name="Provider">The provider whose scrape produced this snapshot.</param>
/// <param name="Succeeded">
/// True when the scrape completed without an exception and, after prefix filtering, yielded at
/// least one sample -- the same success condition <see cref="Services.Scraping.ScrapeHostedService"/>
/// already uses to flip a provider online/offline.
/// </param>
public readonly record struct SnapshotOrigin(MetricsProvider Provider, bool Succeeded);
