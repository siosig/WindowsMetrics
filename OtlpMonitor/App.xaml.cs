using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OtlpMonitor.Services;
using OtlpMonitor.ViewModels;

namespace OtlpMonitor;

public partial class App : Application
{
    private IHost? _host;

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddSingleton(Dispatcher);
        builder.Services.AddSingleton<MetricsChannel>();
        builder.Services.AddSingleton<MetricsParser>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddHostedService<OtlpHostedService>();

        _host = builder.Build();
        await _host.StartAsync();

        _host.Services.GetRequiredService<MainWindow>().Show();
    }

    private async void Application_Exit(object sender, ExitEventArgs e)
    {
        using (_host)
        {
            if (_host != null)
                await _host.StopAsync(TimeSpan.FromSeconds(5));
        }
    }
}
