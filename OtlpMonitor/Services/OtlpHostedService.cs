using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using OtlpMonitor.Models.Otlp;

namespace OtlpMonitor.Services;

public class OtlpHostedService : BackgroundService
{
    private readonly MetricsChannel _channel;
    private readonly MetricsParser _parser;
    private WebApplication? _app;

    public OtlpHostedService(MetricsChannel channel, MetricsParser parser)
    {
        _channel = channel;
        _parser = parser;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var builder = WebApplication.CreateSlimBuilder();
        _app = builder.Build();
        _app.Urls.Add("http://127.0.0.1:14318");

        _app.MapPost("/v1/metrics", async (HttpRequest req) =>
        {
            try
            {
                using var reader = new System.IO.StreamReader(req.Body);
                var json = await reader.ReadToEndAsync().ConfigureAwait(false);

                var payload = JsonSerializer.Deserialize<OtlpExportRequest>(json);
                if (payload != null)
                {
                    var snapshot = _parser.Parse(payload);
                    await _channel.Writer.WriteAsync(snapshot, stoppingToken).ConfigureAwait(false);
                }
            }
            catch
            {
                // silently ignore parse errors
            }
            return Results.Ok();
        });

        await _app.RunAsync(stoppingToken).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app != null)
            await _app.StopAsync(cancellationToken).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
