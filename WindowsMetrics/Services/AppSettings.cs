using Microsoft.Win32;

namespace OtlpMonitor.Services;

public class AppSettings
{
    private const string RegPath = @"Software\DIOTLPGUI";

    public string FontFamilyName { get; set; } = "Cascadia Mono";
    public double FontSize { get; set; } = 11.0;
    public bool IsCalendarExpanded { get; set; } = false;

    public void Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegPath);
        if (key == null) return;

        if (key.GetValue("FontFamilyName") is string fontName && !string.IsNullOrWhiteSpace(fontName))
            FontFamilyName = fontName;

        if (key.GetValue("FontSize") is string fontSizeStr
            && double.TryParse(fontSizeStr, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out var fontSize))
            FontSize = Math.Clamp(fontSize, 8.0, 24.0);

        if (key.GetValue("IsCalendarExpanded") is int expanded)
            IsCalendarExpanded = expanded != 0;
    }

    public void Save()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegPath);
        key.SetValue("FontFamilyName", FontFamilyName, RegistryValueKind.String);
        key.SetValue("FontSize",
            FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            RegistryValueKind.String);
        key.SetValue("IsCalendarExpanded", IsCalendarExpanded ? 1 : 0, RegistryValueKind.DWord);
    }
}
