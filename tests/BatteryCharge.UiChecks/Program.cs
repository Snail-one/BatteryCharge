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
        Assert(((FluentModePanel)normal.Parent!).Stacked, "Narrow windows must stack the mode cards.");
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
        await CheckLayoutsAsync(form, screenshots);
        await CheckSlowReadAsync(form, transport, refresh, settings, overview);
    }

    private static async Task CheckLayoutsAsync(MainForm form, string screenshots)
    {
        var scale = form.DeviceDpi / 96f;
        var language = Find<ComboBox>(form, "LanguagePicker");
        var refresh = Find<Button>(form, "RefreshButton");
        var settings = Find<Button>(form, "SettingsNavigation");
        var overview = Find<Button>(form, "OverviewNavigation");
        var viewport = Find<Panel>(form, "PageViewport");
        foreach (var languageIndex in new[] { 0, 1 })
        {
            settings.PerformClick();
            language.SelectedIndex = languageIndex;
            await UntilAsync(() => language.Enabled && refresh.Enabled);
            foreach (var width in new[] { 640, 800, 819, 820, 900, 1020 })
            {
                form.ClientSize = new Size((int)(width * scale), (int)(620 * scale));
                foreach (var settingsPage in new[] { false, true })
                {
                    (settingsPage ? settings : overview).PerformClick();
                    await Task.Delay(20);
                    CheckGeometry(form);
                    var page = Find<Panel>(form, settingsPage ? "SettingsPage" : "OverviewPage");
                    Assert(!viewport.HorizontalScroll.Visible && page.Width <= viewport.ClientSize.Width,
                        $"Page overflows horizontally at width {width} in {UiText.Language}.");
                    Assert(page.Height >= page.GetPreferredSize(new Size(page.Width, 0)).Height,
                        "The page height does not include all its content.");
                    Assert(Find<Button>(form, "ExitButton").Visible && Find<Button>(form, "HideButton").Visible,
                        "Footer actions disappeared during reflow.");
                    if (width == 640)
                        Capture(form, Path.Combine(screenshots, $"{(settingsPage ? "settings" : "overview")}-{UiText.Language}-narrow.png"));
                }
            }
        }

        overview.PerformClick();
        form.ClientSize = new Size((int)(640 * scale), (int)(620 * scale));
        var card = Find<ModeCard>(form, "ModeCardConservation");
        var description = card.Description;
        var originalFont = card.Font;
        try
        {
            card.Description = string.Join(" ", Enumerable.Repeat(description, 6));
            foreach (var fontScale in new[] { 1.25f, 1.5f, 2f })
            {
                using var largerFont = new Font(originalFont.FontFamily, originalFont.Size * fontScale);
                card.Font = largerFont;
                card.Parent!.PerformLayout();
                await Task.Delay(20);
                Assert(card.Height >= card.GetPreferredSize(new Size(card.Width, 0)).Height,
                    "A long mode description or enlarged font clips the card contents.");
                CheckGeometry(form);
                card.Font = originalFont;
            }
        }
        finally { card.Font = originalFont; card.Description = description; card.Parent!.PerformLayout(); }

        var details = Find<TextBox>(form, "DiagnosticDetails");
        viewport.ScrollControlIntoView(details);
        await Task.Delay(20);
        var visibleDetails = viewport.RectangleToClient(details.RectangleToScreen(details.ClientRectangle));
        Assert(viewport.ClientRectangle.IntersectsWith(visibleDetails), "Expanded diagnostics cannot be reached by scrolling.");
        settings.PerformClick();
        overview.PerformClick();
        await Task.Delay(20);
        Assert(viewport.AutoScrollPosition == Point.Empty, "Changing pages must reset the old scroll offset.");

        var layouts = 0;
        LayoutEventHandler count = (_, _) => layouts++;
        viewport.Layout += count;
        try
        {
            for (var index = 0; index < 20; index++) form.PerformLayout();
            Assert(layouts < 100, "Repeated layout causes an unstable scrollbar or responsive-layout loop.");
        }
        finally { viewport.Layout -= count; }
        Console.WriteLine("PASS UI containment, wrapping, scrolling, breakpoints and enlarged fonts.");
    }

    private static void CheckGeometry(Control parent)
    {
        var children = parent.Controls.Cast<Control>().Where(child => child.Visible).ToArray();
        foreach (var child in children)
        {
            // Pages extend vertically beyond a scrolling viewport by design.
            if (parent is not Form && parent is not FluentViewport)
                Assert(parent.ClientRectangle.Contains(child.Bounds),
                    $"{child.Name} ({child.GetType().Name}) is clipped by {parent.GetType().Name}: {child.Bounds} / {parent.ClientRectangle}.");
            if (child is Label label)
                Assert(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height,
                    $"Label is vertically clipped: {label.Text}.");
            CheckGeometry(child);
        }
        for (var first = 0; first < children.Length; first++)
        for (var second = first + 1; second < children.Length; second++)
            Assert(!children[first].Bounds.IntersectsWith(children[second].Bounds),
                $"Sibling controls overlap: {children[first].GetType().Name} and {children[second].GetType().Name} in {parent.GetType().Name}.");
    }

    private static async Task CheckSlowReadAsync(MainForm form, PreviewTransport transport, Button refresh, Button settings, Button overview)
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        transport.ReadStarted = started;
        transport.ReadRelease = release;
        try
        {
            refresh.PerformClick();
            await UntilAsync(() => started.IsSet);
            var ticks = 0;
            using var heartbeat = new System.Windows.Forms.Timer { Interval = 20 };
            heartbeat.Tick += (_, _) => ticks++;
            heartbeat.Start();
            settings.PerformClick();
            Assert(Find<Panel>(form, "SettingsPage").Visible, "Navigation is blocked while reading a slow device.");
            await Task.Delay(150);
            Assert(ticks > 0 && !refresh.Enabled, "The UI message loop stopped while a device read was pending.");
            overview.PerformClick();
        }
        finally { transport.ReadStarted = null; transport.ReadRelease = null; release.Set(); }
        await UntilAsync(() => refresh.Enabled);
        Console.WriteLine("PASS UI stays responsive while device I/O is pending.");
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
        CheckGeometry(form);
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
    internal ManualResetEventSlim? ReadStarted { get; set; }
    internal ManualResetEventSlim? ReadRelease { get; set; }
    public uint Query(uint controlCode, uint input)
    {
        var release = ReadRelease;
        if (release is not null)
        {
            ReadStarted?.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Slow UI check did not release the device read.");
        }
        return controlCode switch
        {
        ChargeProtocol.ModeControlCode => ModeError is null ? _mode : throw ModeError,
        ChargeProtocol.NightControlCode => _night,
        _ => throw new InvalidOperationException("Unexpected query.")
        };
    }
    public void Send(uint controlCode, uint input)
    {
        Writes.Add(input);
        if (controlCode == ChargeProtocol.NightControlCode) { _night = input == 0x80000012 ? 0x11u : 1u; return; }
        _mode = input switch
        { 0x05 => _mode & ~0x20u, 0x08 => _mode & ~0x04u, 0x03 => _mode | 0x20u, 0x07 => _mode | 0x04u, _ => throw new InvalidOperationException("Unexpected mode command.") };
    }
}
