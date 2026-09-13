using Microsoft.Win32;
using WindowsMetrics.Models;
using WindowsMetrics.Services.Scraping;

namespace WindowsMetrics.Services;

public class AppSettings
{
    public const string DefaultRegPath = @"Software\DIOTLPGUI";

    private readonly string _regPath;

    public AppSettings() : this(DefaultRegPath)
    {
    }

    /// <summary>Injectable for tests; production code should use the parameterless constructor.</summary>
    public AppSettings(string regPath)
    {
        _regPath = regPath;
    }

    public string FontFamilyName { get; set; } = "Cascadia Mono";
    public double FontSize { get; set; } = 11.0;
    public bool IsCalendarExpanded { get; set; } = false;

    public int WindowsExporterIntervalMs { get; set; } = (int)ScrapeTarget.DefaultWindowsExporterInterval.TotalMilliseconds;
    public int LhmIntervalMs { get; set; } = (int)ScrapeTarget.DefaultLhmInterval.TotalMilliseconds;
    public Uri WindowsExporterUrl { get; set; } = ScrapeTarget.DefaultWindowsExporterUrl;
    public Uri LhmUrl { get; set; } = ScrapeTarget.DefaultLhmUrl;

    /// <summary>
    /// Human-readable warnings recorded the last time <see cref="Load"/> fell back to a default
    /// value because a registry value was missing, malformed, or out of range. The caller is
    /// expected to log these once at startup.
    /// </summary>
    public IReadOnlyList<string> LoadWarnings { get; private set; } = [];

    public void Load()
    {
        var warnings = new List<string>();
        using var key = Registry.CurrentUser.OpenSubKey(_regPath);
        if (key == null)
        {
            LoadWarnings = warnings;
            return;
        }

        if (key.GetValue("FontFamilyName") is string fontName && !string.IsNullOrWhiteSpace(fontName))
            FontFamilyName = fontName;

        if (key.GetValue("FontSize") is string fontSizeStr
            && double.TryParse(fontSizeStr, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out var fontSize))
            FontSize = Math.Clamp(fontSize, 8.0, 24.0);

        if (key.GetValue("IsCalendarExpanded") is int expanded)
            IsCalendarExpanded = expanded != 0;

        WindowsExporterIntervalMs = LoadIntervalMs(key, "WindowsExporterIntervalMs", WindowsExporterIntervalMs, warnings);
        LhmIntervalMs = LoadIntervalMs(key, "LhmIntervalMs", LhmIntervalMs, warnings);
        WindowsExporterUrl = LoadUrl(key, "WindowsExporterUrl", WindowsExporterUrl, warnings);
        LhmUrl = LoadUrl(key, "LhmUrl", LhmUrl, warnings);

        LoadWarnings = warnings;
    }

    private static int LoadIntervalMs(RegistryKey key, string valueName, int fallback, List<string> warnings)
    {
        if (key.GetValue(valueName) is not int raw)
            return fallback;

        var clamped = Math.Clamp(raw, ScrapeTarget.MinIntervalMs, ScrapeTarget.MaxIntervalMs);
        if (clamped != raw)
            warnings.Add($"{valueName}={raw} is out of range [{ScrapeTarget.MinIntervalMs}, {ScrapeTarget.MaxIntervalMs}]; clamped to {clamped}.");
        return clamped;
    }

    private static Uri LoadUrl(RegistryKey key, string valueName, Uri fallback, List<string> warnings)
    {
        if (key.GetValue(valueName) is not string raw || string.IsNullOrWhiteSpace(raw))
            return fallback;

        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return uri;

        warnings.Add($"{valueName}=\"{raw}\" is not a valid absolute http(s) URL; using default {fallback}.");
        return fallback;
    }

    /// <summary>Builds the two scrape targets this instance's settings describe.</summary>
    public IReadOnlyList<ScrapeTarget> CreateScrapeTargets() =>
    [
        new ScrapeTarget(MetricsProvider.WindowsExporter, WindowsExporterUrl, TimeSpan.FromMilliseconds(WindowsExporterIntervalMs)),
        new ScrapeTarget(MetricsProvider.LibreHardwareMonitor, LhmUrl, TimeSpan.FromMilliseconds(LhmIntervalMs)),
    ];

    public void Save()
    {
        using var key = Registry.CurrentUser.CreateSubKey(_regPath);
        key.SetValue("FontFamilyName", FontFamilyName, RegistryValueKind.String);
        key.SetValue("FontSize",
            FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            RegistryValueKind.String);
        key.SetValue("IsCalendarExpanded", IsCalendarExpanded ? 1 : 0, RegistryValueKind.DWord);
    }
}
