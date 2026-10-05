using System.Drawing;

namespace BatteryCharge.App;

internal sealed record FluentPalette(
    Color Background, Color Surface, Color Hover, Color Border,
    Color Text, Color Secondary, Color Accent, Color AccentSoft,
    Color OnAccent, Color Success, Color Error, bool IsDark = false)
{
    internal static FluentPalette Light { get; } = new(
        Color.FromArgb(243, 243, 243), Color.White, Color.FromArgb(249, 249, 249),
        Color.FromArgb(222, 222, 222), Color.FromArgb(27, 27, 27), Color.FromArgb(96, 96, 96),
        Color.FromArgb(0, 95, 184), Color.FromArgb(232, 242, 252), Color.White,
        Color.FromArgb(15, 112, 57), Color.FromArgb(179, 38, 30));

    internal static FluentPalette Dark { get; } = new(
        Color.FromArgb(32, 32, 32), Color.FromArgb(43, 43, 43), Color.FromArgb(51, 51, 51),
        Color.FromArgb(64, 64, 64), Color.FromArgb(245, 245, 245), Color.FromArgb(190, 190, 190),
        Color.FromArgb(96, 205, 255), Color.FromArgb(36, 57, 69), Color.FromArgb(0, 44, 66),
        Color.FromArgb(108, 203, 143), Color.FromArgb(255, 153, 164), true);
}
