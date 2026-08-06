using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AIOrchestrator.Views;

/// <summary>
/// Turns the native window title bar dark via DWM's immersive-dark attribute (Windows 10 1809+).
/// Attribute id 20 on current builds, 19 on older ones — both are tried.
/// </summary>
public static class DarkTitleBar_Enabler
{
    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY = 19;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            var enabled = 1;

            var result = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));

            if (result != 0)
                DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY, ref enabled, sizeof(int));
        }
        catch
        {
            // A light title bar is cosmetic — never let it break startup.
        }
    }
}
