using Microsoft.Win32;

namespace OtlpMonitor.Services;

public static class WindowPositionService
{
    private const string RegPath = @"Software\DIOTLPGUI";

    public static (double Left, double Top)? Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegPath);
        if (key == null) return null;
        if (key.GetValue("Left") is int l && key.GetValue("Top") is int t)
            return (l, t);
        return null;
    }

    public static void Save(double left, double top)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegPath);
        key.SetValue("Left", (int)left, RegistryValueKind.DWord);
        key.SetValue("Top", (int)top, RegistryValueKind.DWord);
    }
}
