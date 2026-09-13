using Microsoft.Win32;
using WindowsMetrics.Models;
using WindowsMetrics.Services;

namespace WindowsMetrics.Tests;

/// <summary>
/// Exercises AppSettings against a throwaway HKCU test key so these tests never touch the
/// real "Software\DIOTLPGUI" key a developer's machine might already have configured.
/// </summary>
public sealed class AppSettingsTests : IDisposable
{
    private const string TestRegPath = @"Software\WindowsMetricsTests_AppSettings";

    public AppSettingsTests()
    {
        Registry.CurrentUser.DeleteSubKeyTree(TestRegPath, throwOnMissingSubKey: false);
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(TestRegPath, throwOnMissingSubKey: false);
    }

    [Fact]
    public void CreateScrapeTargets_WithDefaults_ProducesOneTargetPerProvider()
    {
        var settings = new AppSettings();
        var targets = settings.CreateScrapeTargets();

        Assert.Equal(2, targets.Count);
        Assert.Contains(targets, t => t.Provider == MetricsProvider.WindowsExporter &&
                                       t.Url == new Uri("http://127.0.0.1:9182/metrics") &&
                                       t.Interval == TimeSpan.FromMilliseconds(1000));
        Assert.Contains(targets, t => t.Provider == MetricsProvider.LibreHardwareMonitor &&
                                       t.Url == new Uri("http://127.0.0.1:8085/metrics") &&
                                       t.Interval == TimeSpan.FromMilliseconds(5000));
    }

    [Fact]
    public void IntervalOutOfRange_IsClampedAndReported()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(TestRegPath))
            key!.SetValue("WindowsExporterIntervalMs", 100, RegistryValueKind.DWord); // below the 250ms floor

        var settings = new AppSettings(TestRegPath);
        settings.Load();

        Assert.Equal(250, settings.WindowsExporterIntervalMs);
        Assert.Contains(settings.LoadWarnings, w => w.Contains("WindowsExporterIntervalMs"));
    }

    [Fact]
    public void InvalidUrl_FallsBackToDefaultAndReportsWarning()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(TestRegPath))
            key!.SetValue("WindowsExporterUrl", "not a url", RegistryValueKind.String);

        var settings = new AppSettings(TestRegPath);
        settings.Load();

        Assert.Equal(new Uri("http://127.0.0.1:9182/metrics"), settings.WindowsExporterUrl);
        Assert.Contains(settings.LoadWarnings, w => w.Contains("WindowsExporterUrl"));
    }

    [Fact]
    public void ValidCustomInterval_IsUsedAsIs()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(TestRegPath))
            key!.SetValue("LhmIntervalMs", 2000, RegistryValueKind.DWord);

        var settings = new AppSettings(TestRegPath);
        settings.Load();

        Assert.Equal(2000, settings.LhmIntervalMs);
        Assert.Empty(settings.LoadWarnings);
    }

    [Fact]
    public void NoRegistryKey_UsesAllDefaultsWithoutWarnings()
    {
        var settings = new AppSettings(TestRegPath);
        settings.Load();

        Assert.Equal(1000, settings.WindowsExporterIntervalMs);
        Assert.Equal(5000, settings.LhmIntervalMs);
        Assert.Empty(settings.LoadWarnings);
    }
}
