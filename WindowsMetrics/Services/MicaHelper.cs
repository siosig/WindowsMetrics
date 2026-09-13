using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WindowsMetrics.Services;

public static class MicaHelper
{
    private enum DWMWINDOWATTRIBUTE
    {
        DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
        DWMWA_SYSTEMBACKDROP_TYPE = 38,
    }

    private enum DWM_SYSTEMBACKDROP_TYPE
    {
        DWMSBT_AUTO = 0,
        DWMSBT_NONE = 1,
        DWMSBT_MAINWINDOW = 2, // Mica
        DWMSBT_TRANSIENTWINDOW = 3, // Acrylic
        DWMSBT_TABBEDWINDOW = 4, // Mica Alt
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        // ダークモード有効化
        int dark = 1;
        DwmSetWindowAttribute(hwnd, (int)DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        // Mica バックドロップ適用
        int mica = (int)DWM_SYSTEMBACKDROP_TYPE.DWMSBT_MAINWINDOW;
        DwmSetWindowAttribute(hwnd, (int)DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, ref mica, sizeof(int));
    }
}
