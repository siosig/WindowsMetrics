using WindowsMetrics.Models;

namespace WindowsMetrics.Services.Scraping;

/// <summary>
/// Where and how often to scrape one metrics provider. Instances are normally built
/// from <see cref="AppSettings.CreateScrapeTargets"/>, which applies the clamping and
/// fallback rules documented here.
/// </summary>
public sealed class ScrapeTarget
{
    public const int MinIntervalMs = 250;
    public const int MaxIntervalMs = 60_000;
    private static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromSeconds(10);

    public MetricsProvider Provider { get; }
    public Uri Url { get; }
    public TimeSpan Interval { get; }

    /// <summary>Derived per-request timeout: interval doubled, clamped to [2s, 10s].</summary>
    public TimeSpan Timeout { get; }

    public ScrapeTarget(MetricsProvider provider, Uri url, TimeSpan interval)
    {
        if (url.Scheme is not ("http" or "https"))
            throw new ArgumentException("Scrape target URL must be http or https.", nameof(url));

        Provider = provider;
        Url = url;

        var clampedMs = Math.Clamp(interval.TotalMilliseconds, MinIntervalMs, MaxIntervalMs);
        Interval = TimeSpan.FromMilliseconds(clampedMs);

        var timeoutMs = Math.Clamp(Interval.TotalMilliseconds * 2, MinTimeout.TotalMilliseconds, MaxTimeout.TotalMilliseconds);
        Timeout = TimeSpan.FromMilliseconds(timeoutMs);
    }

    public static readonly Uri DefaultWindowsExporterUrl = new("http://127.0.0.1:9182/metrics");
    public static readonly TimeSpan DefaultWindowsExporterInterval = TimeSpan.FromMilliseconds(1000);

    public static readonly Uri DefaultLhmUrl = new("http://127.0.0.1:8085/metrics");
    public static readonly TimeSpan DefaultLhmInterval = TimeSpan.FromMilliseconds(5000);
}
