using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WindowsMetrics.Models;
using WindowsMetrics.Models.Display;

namespace WindowsMetrics.Services.Scraping;

/// <summary>
/// Runs one independent <see cref="PeriodicTimer"/> polling loop per <see cref="ScrapeTarget"/>
/// and publishes a <see cref="MetricsSnapshot"/> to <see cref="MetricsChannel"/> after every
/// attempt (success or failure). A failed or empty scrape still produces a snapshot — via
/// <see cref="MetricsParser"/>'s own partial-update behavior it carries forward whatever values
/// were last observed, with only <see cref="MetricsSnapshot.ProviderStates"/> reflecting the
/// new failure — so the UI can show "still showing the last known value" instead of blanking out.
/// Calls into the shared <see cref="MetricsParser"/> are serialized with a lock because two
/// providers' loops run concurrently against the same parser instance.
/// </summary>
public sealed class ScrapeHostedService : BackgroundService
{
    private readonly IReadOnlyList<ScrapeTarget> _targets;
    private readonly PrometheusScraper _scraper;
    private readonly MetricsParser _parser;
    private readonly MetricsChannel _channel;
    private readonly ILogger<ScrapeHostedService> _logger;
    private readonly Lock _lock = new();
    private readonly Dictionary<MetricsProvider, ProviderHealth> _health;

    public ScrapeHostedService(
        IReadOnlyList<ScrapeTarget> targets,
        PrometheusScraper scraper,
        MetricsParser parser,
        MetricsChannel channel,
        ILogger<ScrapeHostedService> logger)
    {
        _targets = targets;
        _scraper = scraper;
        _parser = parser;
        _channel = channel;
        _logger = logger;
        _health = targets.ToDictionary(t => t.Provider, t => ProviderHealth.CreateInitial(t.Provider));
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(_targets.Select(target => RunLoopAsync(target, stoppingToken)));

    private async Task RunLoopAsync(ScrapeTarget target, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(target.Interval);
        do
        {
            await ScrapeOnceAsync(target, stoppingToken).ConfigureAwait(false);
        }
        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task ScrapeOnceAsync(ScrapeTarget target, CancellationToken stoppingToken)
    {
        var timeNano = GetTimeNano();
        List<FlatMetric> flat;
        Exception? failure = null;

        try
        {
            flat = await _scraper.ScrapeAsync(target, timeNano, stoppingToken).ConfigureAwait(false);
            if (flat.Count == 0)
                failure = new InvalidOperationException(
                    $"Scrape of {target.Provider} at {target.Url} returned no samples matching the required metric prefixes.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Application shutdown, not a provider failure: propagate so the loop can exit.
            throw;
        }
        catch (Exception ex)
        {
            flat = [];
            failure = ex;
        }

        var snapshot = RecordOutcomeAndParse(target.Provider, flat, failure);

        try
        {
            await _channel.Writer.WriteAsync(snapshot, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; nothing left to do with this snapshot.
        }
    }

    private MetricsSnapshot RecordOutcomeAndParse(MetricsProvider provider, List<FlatMetric> flat, Exception? failure)
    {
        var now = DateTimeOffset.Now;
        lock (_lock)
        {
            var previous = _health[provider];
            var updated = failure is null
                ? previous.WithSuccess(now)
                : previous.WithFailure(now, Truncate(failure.Message));
            _health[provider] = updated;

            if (previous.IsOnline && !updated.IsOnline)
                _logger.LogWarning("{Provider} went offline: {Error}", provider, updated.LastError);
            else if (!previous.IsOnline && updated.IsOnline)
                _logger.LogInformation("{Provider} back online", provider);

            var snapshot = _parser.Parse(flat);
            return snapshot with { ProviderStates = new Dictionary<MetricsProvider, ProviderHealth>(_health) };
        }
    }

    private static string Truncate(string message) => message.Length <= 80 ? message : message[..80];

    // Stopwatch.Frequency is machine-dependent, so it must not be hardcoded.
    private static ulong GetTimeNano() =>
        (ulong)(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency * 1_000_000_000.0);
}
