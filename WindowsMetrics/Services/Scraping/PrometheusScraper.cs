using System.IO;
using System.Net.Http;
using WindowsMetrics.Models;
using WindowsMetrics.Services.Parsing;

namespace WindowsMetrics.Services.Scraping;

/// <summary>
/// Fetches one provider's Prometheus text exposition body over HTTP and parses it into
/// <see cref="FlatMetric"/> samples, filtered down to <see cref="MetricsParser.RequiredMetricPrefixes"/>.
/// Any HTTP failure (non-2xx, timeout, connection refused) propagates as an exception for the
/// caller (<see cref="ScrapeHostedService"/>) to translate into provider health state; this type
/// has no health-tracking responsibility of its own.
/// </summary>
public sealed class PrometheusScraper
{
    private const string HttpClientName = "scrape";
    private readonly IHttpClientFactory _httpClientFactory;

    public PrometheusScraper(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<List<FlatMetric>> ScrapeAsync(ScrapeTarget target, ulong timeNano, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient(HttpClientName);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(target.Timeout);

        using var response = await client
            .GetAsync(target.Url, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        return PrometheusTextParser.Parse(reader, timeNano, MetricsParser.RequiredMetricPrefixes);
    }
}
