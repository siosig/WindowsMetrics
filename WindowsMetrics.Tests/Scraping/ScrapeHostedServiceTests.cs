using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;
using WindowsMetrics.Models;
using WindowsMetrics.Models.Display;
using WindowsMetrics.Services;
using WindowsMetrics.Services.Scraping;

namespace WindowsMetrics.Tests.Scraping;

public class ScrapeHostedServiceTests
{
    private const string WeUrl = "http://scrape.test/we/metrics";
    private const string LhmUrl = "http://scrape.test/lhm/metrics";
    private static readonly TimeSpan ShortInterval = TimeSpan.FromMilliseconds(50);

    private static PrometheusScraper BuildScraper(MockHttpMessageHandler mockHttp)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("scrape").ConfigurePrimaryHttpMessageHandler(() => mockHttp);
        var provider = services.BuildServiceProvider();
        return new PrometheusScraper(provider.GetRequiredService<IHttpClientFactory>());
    }

    private static async Task<MetricsSnapshot> ReadNextAsync(MetricsChannel channel, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        return await channel.Reader.ReadAsync(cts.Token);
    }

    private static ScrapeTarget[] Targets() =>
    [
        new ScrapeTarget(MetricsProvider.WindowsExporter, new Uri(WeUrl), ShortInterval),
        new ScrapeTarget(MetricsProvider.LibreHardwareMonitor, new Uri(LhmUrl), ShortInterval),
    ];

    [Fact]
    public async Task SuccessfulScrape_MarksProviderOnlineAndDeliversSnapshot()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(WeUrl).Respond("text/plain", "windows_memory_available_bytes 123\n");
        mockHttp.When(LhmUrl).Respond("text/plain", "lhm_memory_temperature_celsius{hardwareId=\"/memory/dimm/0\"} 40\n");

        var channel = new MetricsChannel();
        var service = new ScrapeHostedService(Targets(), BuildScraper(mockHttp), new MetricsParser(), channel,
            NullLogger<ScrapeHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            // ProviderStates always has both keys from the first snapshot (pre-populated at
            // construction time), so wait for both to actually flip to IsOnline rather than
            // just checking dictionary size.
            var snapshot = await ReadNextAsync(channel, TimeSpan.FromSeconds(5));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            bool BothOnline() =>
                snapshot.ProviderStates.TryGetValue(MetricsProvider.WindowsExporter, out var we) && we.IsOnline &&
                snapshot.ProviderStates.TryGetValue(MetricsProvider.LibreHardwareMonitor, out var lhm) && lhm.IsOnline;
            while (!BothOnline() && DateTime.UtcNow < deadline)
                snapshot = await ReadNextAsync(channel, TimeSpan.FromSeconds(5));

            Assert.True(snapshot.ProviderStates[MetricsProvider.WindowsExporter].IsOnline);
            Assert.True(snapshot.ProviderStates[MetricsProvider.LibreHardwareMonitor].IsOnline);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task HttpServerError_MarksProviderOfflineWithOfflineSinceSet()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(WeUrl).Respond(System.Net.HttpStatusCode.InternalServerError);
        mockHttp.When(LhmUrl).Respond("text/plain", "lhm_memory_temperature_celsius{hardwareId=\"/memory/dimm/0\"} 40\n");

        var channel = new MetricsChannel();
        var service = new ScrapeHostedService(Targets(), BuildScraper(mockHttp), new MetricsParser(), channel,
            NullLogger<ScrapeHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            ProviderHealth? weHealth = null;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var snapshot = await ReadNextAsync(channel, TimeSpan.FromSeconds(5));
                if (snapshot.ProviderStates.TryGetValue(MetricsProvider.WindowsExporter, out var health) &&
                    health.ConsecutiveFailures > 0)
                {
                    weHealth = health;
                    break;
                }
            }

            Assert.NotNull(weHealth);
            Assert.False(weHealth!.IsOnline);
            Assert.NotNull(weHealth.OfflineSince);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Timeout_MarksProviderOffline()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(WeUrl).Respond(async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            throw new TaskCanceledException("simulated timeout");
        });
        mockHttp.When(LhmUrl).Respond("text/plain", "lhm_memory_temperature_celsius{hardwareId=\"/memory/dimm/0\"} 40\n");

        // Interval/timeout of 50ms doubled and clamped to the 2s minimum would be too slow for
        // a unit test, so this target uses a target built with a tiny explicit interval; the
        // clamp floor (2s minimum timeout) is exercised separately by ScrapeTargetTests-style
        // assertions inside PrometheusTextParserTests' sibling coverage. Here we only need the
        // request to eventually be cancelled and observed as a failure.
        var targets = new[]
        {
            new ScrapeTarget(MetricsProvider.WindowsExporter, new Uri(WeUrl), ShortInterval),
            new ScrapeTarget(MetricsProvider.LibreHardwareMonitor, new Uri(LhmUrl), ShortInterval),
        };

        var channel = new MetricsChannel();
        var service = new ScrapeHostedService(targets, BuildScraper(mockHttp), new MetricsParser(), channel,
            NullLogger<ScrapeHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            ProviderHealth? weHealth = null;
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                var snapshot = await ReadNextAsync(channel, TimeSpan.FromSeconds(15));
                if (snapshot.ProviderStates.TryGetValue(MetricsProvider.WindowsExporter, out var health) &&
                    health.ConsecutiveFailures > 0)
                {
                    weHealth = health;
                    break;
                }
            }

            Assert.NotNull(weHealth);
            Assert.False(weHealth!.IsOnline);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task SuccessfulScrapeWithNoMatchingSamples_IsTreatedAsOffline()
    {
        var mockHttp = new MockHttpMessageHandler();
        // 200 OK, but nothing that survives MetricsParser.RequiredMetricPrefixes filtering.
        mockHttp.When(WeUrl).Respond("text/plain", "totally_unrelated_metric 1\n");
        mockHttp.When(LhmUrl).Respond("text/plain", "lhm_memory_temperature_celsius{hardwareId=\"/memory/dimm/0\"} 40\n");

        var channel = new MetricsChannel();
        var service = new ScrapeHostedService(Targets(), BuildScraper(mockHttp), new MetricsParser(), channel,
            NullLogger<ScrapeHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            ProviderHealth? weHealth = null;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var snapshot = await ReadNextAsync(channel, TimeSpan.FromSeconds(5));
                if (snapshot.ProviderStates.TryGetValue(MetricsProvider.WindowsExporter, out var health) &&
                    health.ConsecutiveFailures > 0)
                {
                    weHealth = health;
                    break;
                }
            }

            Assert.NotNull(weHealth);
            Assert.False(weHealth!.IsOnline);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ProviderRecoversAfterFailure_GoesBackOnlineWithOfflineSinceCleared()
    {
        var mockHttp = new MockHttpMessageHandler();
        var weRequestCount = 0;
        mockHttp.When(WeUrl).Respond(_ =>
        {
            weRequestCount++;
            var message = weRequestCount <= 2
                ? new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("windows_memory_available_bytes 123\n"),
                };
            return Task.FromResult(message);
        });
        mockHttp.When(LhmUrl).Respond("text/plain", "lhm_memory_temperature_celsius{hardwareId=\"/memory/dimm/0\"} 40\n");

        var channel = new MetricsChannel();
        var service = new ScrapeHostedService(Targets(), BuildScraper(mockHttp), new MetricsParser(), channel,
            NullLogger<ScrapeHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            var sawOffline = false;
            while (DateTime.UtcNow < deadline)
            {
                var snapshot = await ReadNextAsync(channel, TimeSpan.FromSeconds(10));
                if (!snapshot.ProviderStates.TryGetValue(MetricsProvider.WindowsExporter, out var health)) continue;

                if (!health.IsOnline) sawOffline = true;
                if (sawOffline && health.IsOnline)
                {
                    Assert.Null(health.OfflineSince);
                    return;
                }
            }

            Assert.Fail("windows_exporter never recovered to online within the deadline.");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task OneProviderFailing_DoesNotStopSnapshotsForTheOtherProvider()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(WeUrl).Respond(System.Net.HttpStatusCode.InternalServerError);
        mockHttp.When(LhmUrl).Respond("text/plain", "lhm_memory_temperature_celsius{hardwareId=\"/memory/dimm/0\"} 40\n");

        var channel = new MetricsChannel();
        var service = new ScrapeHostedService(Targets(), BuildScraper(mockHttp), new MetricsParser(), channel,
            NullLogger<ScrapeHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            bool lhmSeenOnline = false;
            while (DateTime.UtcNow < deadline && !lhmSeenOnline)
            {
                var snapshot = await ReadNextAsync(channel, TimeSpan.FromSeconds(5));
                if (snapshot.ProviderStates.TryGetValue(MetricsProvider.LibreHardwareMonitor, out var health) && health.IsOnline)
                    lhmSeenOnline = true;
            }

            Assert.True(lhmSeenOnline, "LibreHardwareMonitor snapshots should keep flowing even while windows_exporter fails.");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }
}
