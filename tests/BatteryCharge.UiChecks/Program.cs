using BatteryCharge.App;
using BatteryCharge.Core;
using System.Drawing.Imaging;

namespace BatteryCharge.UiChecks;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var defaultFont = new Font("Segoe UI", 10);
        Application.SetDefaultFont(defaultFont);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        var screenshots = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/ui");
        var temporary = Path.Combine(Path.GetTempPath(), $"BatteryCharge-ui-{Guid.NewGuid():N}");
        var exitCode = 1;
        try
        {
            UiText.SetLanguage("zh-CN");
            var transport = new PreviewTransport();
            var startup = new PreviewStartup();
            using var form = new MainForm(new ChargeController(transport, 3, TimeSpan.Zero),
                startupManager: startup, preferencesPath: Path.Combine(temporary, "settings.json"));
            form.Shown += async (_, _) =>
            {
                try
                {
                    await CheckAsync(form, transport, startup, screenshots);
                    exitCode = 0;
                    Console.WriteLine("PASS Windows UI interactions and screenshot capture.");
                }
                catch (Exception error) { Console.Error.WriteLine(error); }
                finally { form.Dispose(); Application.ExitThread(); }
            };
            Application.Run(form);
        }
        catch (Exception error) { Console.Error.WriteLine(error); }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
        return exitCode;
    }

    private static async Task CheckAsync(MainForm form, PreviewTransport transport, PreviewStartup startup, string screenshots)
    {
        var normal = Find<ModeCard>(form, "ModeCardNormal");
        var conservation = Find<ModeCard>(form, "ModeCardConservation");
        var rapid = Find<ModeCard>(form, "ModeCardRapidCharge");
        var apply = Find<Button>(form, "ApplyModeButton");
        var refresh = Find<Button>(form, "RefreshButton");
        var settings = Find<Button>(form, "SettingsNavigation");
        var overview = Find<Button>(form, "OverviewNavigation");
        await UntilAsync(() => refresh.Enabled && normal.IsCurrent);
        var scale = form.DeviceDpi / 96f;
        form.ClientSize = new Size((int)(1020 * scale), (int)(760 * scale));
        form.PerformLayout();
        Assert(transport.Writes.Count == 0 && startup.Changes == 0, "Opening the UI must only read state.");
        Assert(normal.Checked && !apply.Enabled, "The active mode must start selected without a redundant apply action.");
        conservation.PerformClick();
        Assert(conservation.Checked && normal.IsCurrent && !conservation.IsCurrent && apply.Enabled,
            "Selecting a card must keep the actual device mode visible until apply.");
        Assert(transport.Writes.Count == 0, "Selecting a card wrote to firmware.");
        apply.PerformClick();
        await UntilAsync(() => refresh.Enabled && conservation.IsCurrent);
        Assert(transport.Writes.SequenceEqual(new uint[] { 0x08, 0x03 }), "Applying a card changed the firmware command sequence.");
        Assert(!apply.Enabled, "Apply should be disabled once the selected mode is verified.");
        var night = Find<CheckBox>(form, "NightToggle");
        night.AccessibilityObject.DoDefaultAction();
        await UntilAsync(() => refresh.Enabled && night.Checked);
        Assert(transport.Writes.Last() == 0x80000012, "The switch did not enable night charging.");
        rapid.PerformClick();
        settings.PerformClick();
        var startupToggle = Find<CheckBox>(form, "StartupToggle");
        startupToggle.AccessibilityObject.DoDefaultAction();
        await UntilAsync(() => startupToggle.Enabled && startupToggle.Checked);
        Assert(startup.Enabled && startup.Changes == 1, "The startup switch did not update the fake registration.");
        var language = Find<ComboBox>(form, "LanguagePicker");
        var writes = transport.Writes.Count;
        language.SelectedIndex = 1;
        await UntilAsync(() => UiText.Language == "en-US" && refresh.Enabled && language.Enabled);
        Assert(rapid.Checked && conservation.IsCurrent, "Changing language discarded the pending choice or altered actual state.");
        Assert(transport.Writes.Count == writes && startup.Changes == 1, "Changing language wrote charging or startup settings.");
        overview.PerformClick();
        Assert(rapid.Text == UiText.ModeName(ChargeMode.RapidCharge), "The mode cards did not change language.");
        Directory.CreateDirectory(screenshots);
        foreach (var palette in new[] { FluentPalette.Light, FluentPalette.Dark })
        {
            form.ApplyPalette(palette);
            var name = palette.IsDark ? "dark" : "light";
            Capture(form, Path.Combine(screenshots, $"overview-en-{name}.png"));
            settings.PerformClick();
            Capture(form, Path.Combine(screenshots, $"settings-en-{name}.png"));
            language.SelectedIndex = 0;
            await UntilAsync(() => UiText.Language == "zh-CN" && refresh.Enabled && language.Enabled);
            overview.PerformClick();
            Capture(form, Path.Combine(screenshots, $"overview-zh-{name}.png"));
            settings.PerformClick();
            Capture(form, Path.Combine(screenshots, $"settings-zh-{name}.png"));
            language.SelectedIndex = 1;
            await UntilAsync(() => UiText.Language == "en-US" && refresh.Enabled && language.Enabled);
            overview.PerformClick();
        }
        form.ClientSize = new Size((int)(640 * scale), (int)(620 * scale));
        form.PerformLayout();
        await Task.Delay(50);
        Assert(((TableLayoutPanel)normal.Parent!).ColumnCount == 1, "Narrow windows must stack the mode cards.");
        Assert(rapid.Checked && conservation.IsCurrent, "Responsive layout changed the selection or actual device state.");
        Capture(form, Path.Combine(screenshots, "overview-narrow.png"));
        form.Hide();
        form.RequestShowWindow();
        await UntilAsync(() => form.Visible);
        Assert(transport.Writes.Count == writes, "Restoring the window must not apply a mode.");
        transport.ModeError = new IOException("Simulated device disconnected.");
        refresh.PerformClick();
        await UntilAsync(() => refresh.Enabled && !normal.Enabled);
        Assert(!apply.Enabled && !normal.IsCurrent && !conservation.IsCurrent && !rapid.IsCurrent,
            "Unavailable mode controls must not show a stale active badge or allow apply.");
        Assert(night.Enabled, "A mode failure must not disable an independently supported night switch.");
        Assert(Find<TextBox>(form, "DiagnosticDetails").Text.Contains("Simulated device disconnected"),
            "The underlying error must be available in the diagnostic panel.");
        Capture(form, Path.Combine(screenshots, "overview-unavailable.png"));
    }

    private static T Find<T>(Control parent, string name) where T : Control =>
        parent.Controls.Find(name, searchAllChildren: true).OfType<T>().Single();

    private static async Task UntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Timed out waiting for UI state.");
            await Task.Delay(20);
        }
    }

    private static void Capture(Form form, string path)
    {
        form.Refresh();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}

internal sealed class PreviewStartup : IStartupManager
{
    internal bool Enabled { get; private set; }
    internal int Changes { get; private set; }
    public StartupRegistration Read() => new(Enabled, true);
    public void SetEnabled(bool enabled) { Enabled = enabled; Changes++; }
    public void RemoveForCleanup() => SetEnabled(false);
}

internal sealed class PreviewTransport : IEnergyTransport
{
    private uint _mode;
    private uint _night = 1;
    internal Exception? ModeError { get; set; }
    internal List<uint> Writes { get; } = [];
    public uint Query(uint controlCode, uint input) => controlCode switch
    {
        ChargeProtocol.ModeControlCode => ModeError is null ? _mode : throw ModeError,
        ChargeProtocol.NightControlCode => _night,
        _ => throw new InvalidOperationException("Unexpected query.")
    };
    public void Send(uint controlCode, uint input)
    {
        Writes.Add(input);
        if (controlCode == ChargeProtocol.NightControlCode) { _night = input == 0x80000012 ? 0x11u : 1u; return; }
        _mode = input switch
        { 0x05 => _mode & ~0x20u, 0x08 => _mode & ~0x04u, 0x03 => _mode | 0x20u, 0x07 => _mode | 0x04u, _ => throw new InvalidOperationException("Unexpected mode command.") };
    }
}
