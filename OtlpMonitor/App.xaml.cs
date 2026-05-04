using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OtlpMonitor.Services;
using OtlpMonitor.ViewModels;

namespace OtlpMonitor;

public partial class App : Application
{
    private IHost? _host;
    private Mutex? _mutex;

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        bool isNew;
        try
        {
            _mutex = new Mutex(true, "OtlpMonitor_SingleInstance", out isNew);
        }
        catch (AbandonedMutexException)
        {
            // 前回強制終了で Abandoned になった場合でも所有権は取得済み
            isNew = true;
        }
        if (!isNew)
        {
            _mutex?.Dispose();
            Shutdown();
            return;
        }

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
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
