using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BatteryCharge.App;

internal static class WindowsAppearance
{
    internal static FluentPalette ReadPalette()
    {
        if (SystemInformation.HighContrast)
            return new FluentPalette(SystemColors.Control, SystemColors.Window, SystemColors.Control,
                SystemColors.WindowText, SystemColors.WindowText, SystemColors.WindowText,
                SystemColors.Highlight, SystemColors.Control, SystemColors.HighlightText,
                SystemColors.WindowText, SystemColors.WindowText);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0
                ? FluentPalette.Dark : FluentPalette.Light;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return FluentPalette.Light;
        }
    }

    internal static void Apply(IntPtr handle, FluentPalette palette)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            return;
        var dark = palette.IsDark ? 1 : 0;
        // Unsupported attributes are ignored by DWM on older Windows versions.
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var rounded = 2; // DWMWCP_ROUND.
            _ = DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
