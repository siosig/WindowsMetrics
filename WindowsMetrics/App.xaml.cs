using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using WindowsMetrics.Models;
using WindowsMetrics.Services;
using WindowsMetrics.Services.Scraping;
using WindowsMetrics.ViewModels;

namespace WindowsMetrics;

public partial class App : Application
{
    private IHost? _host;
    private Mutex? _mutex;
    private TrayIconService? _trayIcon;

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        bool isNew;
        try
        {
            _mutex = new Mutex(true, "OtlpMonitor_SingleInstance", out isNew);
        }
        catch (AbandonedMutexException)
        {
            // The previous instance crashed without releasing the mutex, but ownership
            // still transfers to us in that case.
            isNew = true;
        }
        if (!isNew)
        {
            _mutex?.Dispose();
            Shutdown();
            return;
        }

        ConfigureSerilog();

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(dispose: true);

        builder.Services.AddSingleton(Dispatcher);
        builder.Services.AddSingleton<AppSettings>();
        builder.Services.AddSingleton<MetricsChannel>();
        builder.Services.AddSingleton<MetricsParser>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        builder.Services
            .AddHttpClient("scrape")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                UseProxy = false, // targets are always loopback; no proxy resolution needed.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });
        builder.Services.AddSingleton<PrometheusScraper>();
        builder.Services.AddSingleton<IReadOnlyList<ScrapeTarget>>(sp =>
            sp.GetRequiredService<AppSettings>().CreateScrapeTargets());
        builder.Services.AddHostedService<ScrapeHostedService>();

        _host = builder.Build();

        var settings = _host.Services.GetRequiredService<AppSettings>();
        settings.Load();
        foreach (var warning in settings.LoadWarnings)
            Log.Warning("AppSettings: {Warning}", warning);
        Resources["AppFontFamily"] = new FontFamily(settings.FontFamilyName);
        Resources["AppFontSize"] = settings.FontSize;
        Resources["AppFontSizeSmall"] = Math.Max(6.0, settings.FontSize - 2);

        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        _trayIcon = new TrayIconService(mainWindow);
        mainWindow.Show();
    }

    private static void ConfigureSerilog()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsMetrics", "logs");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .WriteTo.File(
                Path.Combine(logDirectory, "windowsmetrics-.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 1_048_576,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 5)
            .CreateLogger();
    }

    private async void Application_Exit(object sender, ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        using (_host)
        {
            if (_host != null)
                await _host.StopAsync(TimeSpan.FromSeconds(5));
        }
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        await Log.CloseAndFlushAsync();
    }
}
