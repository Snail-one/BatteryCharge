using BatteryCharge.Core;

namespace BatteryCharge.App;

internal sealed class ChargeIcons : IDisposable
{
    private readonly Dictionary<ChargeMode, Icon> _windowIcons = [];
    private readonly Dictionary<ChargeMode, Icon> _trayIcons = [];
    private readonly Icon _unknownWindow;
    private readonly Icon _unknownTray;

    internal ChargeIcons()
    {
        _unknownWindow = Load("Unknown");
        _unknownTray = new Icon(_unknownWindow, SystemInformation.SmallIconSize);
        foreach (var mode in Enum.GetValues<ChargeMode>())
        {
            var icon = Load(mode.ToString());
            _windowIcons.Add(mode, icon);
            _trayIcons.Add(mode, new Icon(icon, SystemInformation.SmallIconSize));
        }
    }

    internal Icon WindowFor(ChargeMode? mode) => mode.HasValue ? _windowIcons[mode.Value] : _unknownWindow;
    internal Icon TrayFor(ChargeMode? mode) => mode.HasValue ? _trayIcons[mode.Value] : _unknownTray;

    private static Icon Load(string name)
    {
        using var stream = typeof(ChargeIcons).Assembly.GetManifestResourceStream(
            $"BatteryCharge.App.Assets.BatteryCharge.{name}.ico")
            ?? throw new InvalidOperationException(UiText.Get("IconMissing", name));
        using var icon = new Icon(stream, SystemInformation.IconSize);
        return (Icon)icon.Clone();
    }

    public void Dispose()
    {
        foreach (var icon in _trayIcons.Values)
            icon.Dispose();
        foreach (var icon in _windowIcons.Values)
            icon.Dispose();
        _unknownTray.Dispose();
        _unknownWindow.Dispose();
    }
}
