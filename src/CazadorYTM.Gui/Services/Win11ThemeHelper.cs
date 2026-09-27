namespace CazadorYTM.Gui.Services;

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ModernWpf;

public static class Win11ThemeHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private const int DWMWCP_DEFAULT = 0;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;

    private const int DWMSBT_AUTO = 0;
    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_MAINWINDOW = 2; // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
    private const int DWMSBT_TABBEDWINDOW = 4; // Mica Alt

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void ApplyWin11Backdrop(Window window, bool isDialog = false)
    {
        if (Environment.OSVersion.Version.Major < 10 || Environment.OSVersion.Version.Build < 22000)
            return; // Windows 11 is build 22000+

        void Apply(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            try
            {
                // 1. Windows 11 Rounded Corners Preference
                var cornerPref = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

                // 2. Windows 11 Mica / Acrylic System Backdrop
                var backdrop = isDialog ? DWMSBT_TRANSIENTWINDOW : DWMSBT_MAINWINDOW;
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));

                // 3. Immersive Dark Mode Synchronization
                var isDark = ThemeManager.Current.ActualApplicationTheme == ApplicationTheme.Dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref isDark, sizeof(int));
            }
            catch { }
        }

        var helper = new WindowInteropHelper(window);
        if (helper.Handle != IntPtr.Zero)
        {
            Apply(helper.Handle);
        }
        else
        {
            window.SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(window).Handle;
                Apply(h);
            };
        }
    }
}
