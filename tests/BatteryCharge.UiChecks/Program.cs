using BatteryCharge.App;
using BatteryCharge.Core;
using System.Drawing.Imaging;
using System.Reflection;

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
        await UntilAsync(() => refresh.Enabled && normal.IsCurrent);
        CheckRendering();
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
        var night = Find<Button>(form, "NightChargeButton");
        night.PerformClick();
        await UntilAsync(() => refresh.Enabled && night.Text == UiText.Get("DisableNightCharge"));
        Assert(transport.Writes.Last() == 0x80000012, "The button did not enable night charging.");
        rapid.PerformClick();
        Navigate(form, true);
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
        Navigate(form, false);
        Assert(rapid.Text == UiText.ModeName(ChargeMode.RapidCharge), "The mode cards did not change language.");
        Directory.CreateDirectory(screenshots);
        await CheckHeaderLayoutAsync();
        Assert(rapid.Checked && conservation.IsCurrent && transport.Writes.Count == writes && startup.Changes == 1,
            "Header layout checks must leave the main scenario's pending selection, device and startup state untouched.");
        foreach (var palette in new[] { FluentPalette.Light, FluentPalette.Dark })
        {
            form.ApplyPalette(palette);
            var name = palette.IsDark ? "dark" : "light";
            Capture(form, Path.Combine(screenshots, $"overview-en-{name}.png"));
            Navigate(form, true);
            Capture(form, Path.Combine(screenshots, $"settings-en-{name}.png"));
            language.SelectedIndex = 0;
            await UntilAsync(() => UiText.Language == "zh-CN" && refresh.Enabled && language.Enabled);
            Navigate(form, false);
            Capture(form, Path.Combine(screenshots, $"overview-zh-{name}.png"));
            Navigate(form, true);
            Capture(form, Path.Combine(screenshots, $"settings-zh-{name}.png"));
            language.SelectedIndex = 1;
            await UntilAsync(() => UiText.Language == "en-US" && refresh.Enabled && language.Enabled);
            Navigate(form, false);
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
        await CheckSlowReadAsync(form, transport, refresh);
        await CheckRefreshPositionAsync(form, transport);
        await CheckNightChargeAsync(form, transport, screenshots);
        await CheckStartupWarningAsync(form, transport, startup, screenshots);
    }

    private static async Task CheckHeaderLayoutAsync()
    {
        // This check refreshes device state. Use a separate fixture so those
        // reads cannot reset the pending mode selection in the main scenario.
        var transport = new PreviewTransport();
        var startup = new PreviewStartup();
        using var form = new MainForm(new ChargeController(transport, 3, TimeSpan.Zero),
            startupManager: startup,
            preferencesPath: Path.Combine(Path.GetTempPath(), $"BatteryCharge-header-{Guid.NewGuid():N}.json"));
        form.Show();
        var refresh = Find<Button>(form, "RefreshButton");
        await UntilAsync(() => refresh.Enabled && Find<ModeCard>(form, "ModeCardNormal").IsCurrent);
        var compact = Find<FlowLayoutPanel>(form, "CompactNavigation");
        var warning = Find<FluentSurface>(form, "StartupPathWarning");
        var viewport = Find<Panel>(form, "PageViewport");
        var previousSize = form.ClientSize;
        var previousEnabled = startup.Enabled;
        var previousPath = startup.RegisteredPath;
        var changes = startup.Changes;
        var scale = form.DeviceDpi / 96f;
        try
        {
            foreach (var mismatch in new[] { false, true })
            {
                startup.SimulateTask(true, mismatch ? @"D:\Old\BatteryCharge.exe" : PreviewStartup.CurrentPath);
                refresh.PerformClick();
                await UntilAsync(() => refresh.Enabled);
                foreach (var width in new[] { 1020, 819, 820, 640, 900, 640, 1020 })
                {
                    form.ClientSize = new Size((int)(width * scale), (int)(620 * scale));
                    await Task.Delay(20);
                    foreach (var settings in new[] { false, true })
                    {
                        Navigate(form, settings);
                        CheckGeometry(form);
                        Assert(compact.Visible == (form.ClientSize.Width < 820 * scale),
                            "Resizing must select the navigation mode before measuring the header.");
                        Assert(warning.Visible == mismatch, "Resizing must preserve the startup warning state.");
                        Assert(viewport.Height > 0 && Find<Button>(form, "ExitButton").Visible,
                            "Header reflow must leave room for content and footer actions.");
                    }
                }
            }
            Assert(startup.Changes == changes, "Header reflow must not change the startup task.");
            Assert(transport.Writes.Count == 0, "Header reflow must not write charging settings.");
        }
        finally
        {
            startup.SimulateTask(previousEnabled, previousPath);
            form.ClientSize = previousSize;
            Navigate(form, false);
            refresh.PerformClick();
            await UntilAsync(() => refresh.Enabled);
        }
        Console.WriteLine("PASS Header, navigation and startup warnings do not overlap across width transitions.");
    }

    private static async Task CheckStartupWarningAsync(MainForm form, PreviewTransport transport,
        PreviewStartup startup, string screenshots)
    {
        const string oldPath = @"D:\Old\BatteryCharge.exe";
        var refresh = Find<Button>(form, "RefreshButton");
        var language = Find<ComboBox>(form, "LanguagePicker");
        var toggle = Find<CheckBox>(form, "StartupToggle");
        var warning = Find<FluentSurface>(form, "StartupPathWarning");
        var title = Find<Label>(form, "StartupPathWarningTitle");
        var paths = Find<Label>(form, "StartupPaths");
        var viewport = Find<Panel>(form, "PageViewport");
        var writes = transport.Writes.Count;
        var changes = startup.Changes;
        startup.SimulateTask(true, oldPath);
        foreach (var languageIndex in new[] { 0, 1 })
        {
            language.SelectedIndex = languageIndex;
            await UntilAsync(() => language.Enabled && refresh.Enabled);
            refresh.PerformClick();
            await UntilAsync(() => refresh.Enabled);
            foreach (var palette in new[] { FluentPalette.Light, FluentPalette.Dark })
            {
                form.ApplyPalette(palette);
                var scale = form.DeviceDpi / 96f;
                form.ClientSize = new Size((int)(640 * scale), (int)(620 * scale));
                foreach (var settings in new[] { false, true })
                {
                    Navigate(form, settings);
                    viewport.AutoScrollPosition = new Point(0, 10000);
                    await Task.Delay(20);
                    Assert(warning.Visible && title.Text == UiText.Get("StartupPathWarningTitle"),
                        "A stale path must have a visible warning on both pages, including after scrolling.");
                    Assert(title.Font.Bold && title.ForeColor == palette.Error && warning.Palette.Border == palette.Error,
                        "The path warning must use a bold title and the theme's error color.");
                    if (!SystemInformation.HighContrast)
                        Assert(warning.Palette.Surface != palette.Surface, "The warning background must stand out from normal cards.");
                    Assert(form.ClientRectangle.Contains(form.RectangleToClient(warning.RectangleToScreen(warning.ClientRectangle))),
                        "The path warning must fit inside the window at narrow widths.");
                }
                Navigate(form, false);
                Capture(form, Path.Combine(screenshots, $"startup-warning-{UiText.Language}-{(palette.IsDark ? "dark" : "light")}.png"));
            }
            Find<Button>(form, "ReviewStartupButton").PerformClick();
            Assert(Find<Panel>(form, "SettingsPage").Visible && paths.Text.Contains(oldPath)
                && paths.Text.Contains(PreviewStartup.CurrentPath), "Reviewing startup must show both actual paths in settings.");
            refresh.PerformClick();
            await UntilAsync(() => refresh.Enabled);
            Assert(warning.Visible, "A successful device refresh must not clear a stale startup path warning.");
        }
        Assert(startup.Changes == changes, "Displaying or reviewing a warning must not modify the startup task.");
        startup.SimulateTask(false, oldPath);
        refresh.PerformClick();
        await UntilAsync(() => refresh.Enabled);
        Assert(warning.Visible && !toggle.Checked, "A disabled task must not hide a path mismatch.");
        toggle.AccessibilityObject.DoDefaultAction();
        await UntilAsync(() => toggle.Enabled && toggle.Checked);
        Assert(!warning.Visible && startup.RegisteredPath == PreviewStartup.CurrentPath,
            "Enabling startup must update the path and clear the warning.");
        startup.SimulateTask(true, oldPath);
        refresh.PerformClick();
        await UntilAsync(() => refresh.Enabled);
        toggle.AccessibilityObject.DoDefaultAction();
        await UntilAsync(() => toggle.Enabled && !toggle.Checked);
        Assert(!warning.Visible && startup.RegisteredPath is null, "Removing the task must clear its path warning.");
        toggle.AccessibilityObject.DoDefaultAction();
        await UntilAsync(() => toggle.Enabled && toggle.Checked);
        Assert(!warning.Visible && startup.RegisteredPath == PreviewStartup.CurrentPath,
            "Turning startup off and on must register the current executable path.");
        Assert(transport.Writes.Count == writes, "Startup path checks must not write to the device.");
        startup.SimulateTask(false, PreviewStartup.CurrentPath);
        startup.UnsafeDisabled = true;
        foreach (var languageIndex in new[] { 0, 1 })
        {
            language.SelectedIndex = languageIndex;
            await UntilAsync(() => language.Enabled && refresh.Enabled);
            refresh.PerformClick();
            await UntilAsync(() => refresh.Enabled);
            foreach (var settings in new[] { false, true })
            {
                Navigate(form, settings);
                Assert(warning.Visible && !toggle.Checked
                    && Find<Label>(form, "StartupWarningMessage").Text == UiText.Get("UnsafeStartupDisabled"),
                    "A disabled unsafe target must retain a prominent localized warning even when its path matches.");
                CheckGeometry(form);
            }
            Navigate(form, false);
            Capture(form, Path.Combine(screenshots, $"startup-security-{UiText.Language}.png"));
        }
        startup.SimulateTask(true, PreviewStartup.CurrentPath);
        refresh.PerformClick();
        await UntilAsync(() => refresh.Enabled);
        Assert(!warning.Visible && transport.Writes.Count == writes, "Resolving the security warning must not write to the device.");
        Console.WriteLine("PASS Startup path warnings stay prominent on both pages and clear after reconfiguration.");
    }

    private static async Task CheckNightChargeAsync(MainForm form, PreviewTransport transport, string screenshots)
    {
        var night = Find<Button>(form, "NightChargeButton");
        var state = Find<Label>(form, "NightChargeState");
        var refresh = Find<Button>(form, "RefreshButton");
        var viewport = Find<Panel>(form, "PageViewport");
        var previousError = transport.NightError;
        var scale = form.DeviceDpi / 96f;
        Navigate(form, false);
        try
        {
            foreach (var width in new[] { 640, 1020 })
            {
                form.ClientSize = new Size((int)(width * scale), (int)(620 * scale));
                transport.NightError = null;
                refresh.PerformClick();
                await UntilAsync(() => refresh.Enabled);
                viewport.ScrollControlIntoView(night);
                Assert(night.Visible && night.Enabled && night.Width > 0 && night.Height > 0,
                    "A supported night charging action must be visible and clickable.");
                Assert(viewport.ClientRectangle.Contains(viewport.RectangleToClient(night.RectangleToScreen(night.ClientRectangle))),
                    "The night charging action cannot be fully reached by scrolling.");
                Assert(night.Text == UiText.Get("DisableNightCharge"), "The verified enabled state must offer a disable action.");
                night.PerformClick();
                await UntilAsync(() => refresh.Enabled && night.Text == UiText.Get("EnableNightCharge"));
                Assert(transport.Writes.Last() == 0x12, "The night charging button did not disable the feature.");
                night.PerformClick();
                await UntilAsync(() => refresh.Enabled && night.Text == UiText.Get("DisableNightCharge"));
                Assert(transport.Writes.Last() == 0x80000012, "The night charging button did not re-enable the feature.");

                transport.NightError = new IOException("Simulated night charging unavailable.");
                refresh.PerformClick();
                await UntilAsync(() => refresh.Enabled);
                viewport.ScrollControlIntoView(night);
                Assert(night.Visible && !night.Enabled && night.Text == UiText.Get("NightUnavailable"),
                    "An unavailable night charging action must remain visible with an explicit disabled label.");
                Assert(state.Text.Contains("Simulated night charging unavailable"),
                    "Night charging read failures must be explained beside the action.");
                var writes = transport.Writes.Count;
                night.PerformClick();
                Assert(transport.Writes.Count == writes, "An unavailable night charging action wrote to firmware.");
                Capture(form, Path.Combine(screenshots, $"night-unavailable-{width}.png"));
            }
        }
        finally
        {
            transport.NightError = previousError;
            refresh.PerformClick();
            await UntilAsync(() => refresh.Enabled);
        }
        Console.WriteLine("PASS Night charging actions stay reachable, reflect verified state and explain unavailability.");
    }

    private static async Task CheckRefreshPositionAsync(MainForm form, PreviewTransport transport)
    {
        var refresh = Find<Button>(form, "RefreshButton");
        var viewport = Find<Panel>(form, "PageViewport");
        var scale = form.DeviceDpi / 96f;
        var previousError = transport.ModeError;
        var writes = transport.Writes.Count;
        form.ClientSize = new Size((int)(640 * scale), (int)(480 * scale));
        try
        {
            foreach (var failure in new[] { false, true })
            {
                transport.ReadError = failure ? new InvalidOperationException("Simulated refresh failure.") : null;
                foreach (var settingsPage in new[] { false, true })
                {
                    Navigate(form, settingsPage);
                    await Task.Delay(20);
                    refresh.Focus();
                    viewport.AutoScrollPosition = new Point(0, (int)(40 * scale));
                    var position = viewport.AutoScrollPosition;
                    var bounds = form.Bounds;
                    var completedPage = settingsPage;
                    Assert(position.Y < 0, "Refresh regression requires an already scrolled page.");
                    using var started = new ManualResetEventSlim();
                    using var release = new ManualResetEventSlim();
                    transport.ReadStarted = started;
                    transport.ReadRelease = release;
                    try
                    {
                        refresh.PerformClick();
                        await UntilAsync(() => started.IsSet);
                        Assert(viewport.AutoScrollPosition == position,
                            "Disabling the focused refresh button moved the page before the read finished.");
                        Assert(Find<Panel>(form, settingsPage ? "SettingsPage" : "OverviewPage").Visible,
                            "Starting a refresh switched the page.");
                        // A user may scroll or navigate while I/O runs. Completion must
                        // preserve that newer position rather than restoring a stale one.
                        if (!failure && !settingsPage)
                        {
                            Navigate(form, true);
                            completedPage = true;
                        }
                        viewport.AutoScrollPosition = new Point(0, (int)(20 * scale));
                        position = viewport.AutoScrollPosition;
                    }
                    finally { transport.ReadStarted = null; transport.ReadRelease = null; release.Set(); }
                    await UntilAsync(() => refresh.Enabled);
                    Assert(viewport.AutoScrollPosition == position, "Completing a refresh changed the current scroll position.");
                    Assert(Find<Panel>(form, completedPage ? "SettingsPage" : "OverviewPage").Visible,
                        "A refresh failure forced navigation to another page.");
                    Assert(form.Bounds == bounds, "Refreshing moved or resized the window.");
                }
            }

            transport.ReadError = null;
            transport.ModeError = previousError;
            Navigate(form, false);
            refresh.PerformClick();
            await UntilAsync(() => refresh.Enabled);
            var details = Find<TextBox>(form, "DiagnosticDetails");
            if (details.Visible) Find<Button>(form, "DiagnosticToggleButton").PerformClick();
            refresh.Focus();
            viewport.AutoScrollPosition = new Point(0, (int)(20 * scale));
            var collapsedPosition = viewport.AutoScrollPosition;
            refresh.PerformClick();
            await UntilAsync(() => refresh.Enabled);
            Assert(!details.Visible, "Refreshing the same device error reopened collapsed diagnostics.");
            Assert(viewport.AutoScrollPosition == collapsedPosition, "Refreshing collapsed diagnostics moved the page.");
            Assert(transport.Writes.Count == writes, "Refresh position checks wrote to firmware.");
        }
        finally { transport.ReadError = null; transport.ModeError = previousError; }
        Console.WriteLine("PASS Refresh preserves the page, scroll position and collapsed diagnostics, including failures.");
    }

    private static void Navigate(Control form, bool settings)
    {
        var name = settings ? "SettingsNavigation" : "OverviewNavigation";
        var button = Find<Button>(form, name);
        if (!button.Visible) button = Find<Button>(form, settings ? "CompactSettingsNavigation" : "CompactOverviewNavigation");
        button.PerformClick();
    }

    private static async Task CheckLayoutsAsync(MainForm form, string screenshots)
    {
        var scale = form.DeviceDpi / 96f;
        var language = Find<ComboBox>(form, "LanguagePicker");
        var refresh = Find<Button>(form, "RefreshButton");
        var viewport = Find<Panel>(form, "PageViewport");
        foreach (var languageIndex in new[] { 0, 1 })
        {
            Navigate(form, true);
            language.SelectedIndex = languageIndex;
            await UntilAsync(() => language.Enabled && refresh.Enabled);
            foreach (var width in new[] { 640, 800, 819, 820, 900, 1020 })
            {
                form.ClientSize = new Size((int)(width * scale), (int)(620 * scale));
                foreach (var settingsPage in new[] { false, true })
                {
                    Navigate(form, settingsPage);
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

        Navigate(form, false);
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
        Navigate(form, true);
        Navigate(form, false);
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
                $"Sibling controls overlap: {children[first].Name} ({children[first].GetType().Name}) {children[first].Bounds} "
                + $"and {children[second].Name} ({children[second].GetType().Name}) {children[second].Bounds} "
                + $"in {parent.Name} ({parent.GetType().Name}), client size {parent.ClientSize}.");
    }

    private static async Task CheckSlowReadAsync(MainForm form, PreviewTransport transport, Button refresh)
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
            Navigate(form, true);
            Assert(Find<Panel>(form, "SettingsPage").Visible, "Navigation is blocked while reading a slow device.");
            await Task.Delay(150);
            Assert(ticks > 0 && !refresh.Enabled, "The UI message loop stopped while a device read was pending.");
            Navigate(form, false);
        }
        finally { transport.ReadStarted = null; transport.ReadRelease = null; release.Set(); }
        await UntilAsync(() => refresh.Enabled);
        Console.WriteLine("PASS UI stays responsive while device I/O is pending.");
    }

    private static T Find<T>(Control parent, string name) where T : Control =>
        parent.Controls.Find(name, searchAllChildren: true).OfType<T>().Single();

    private static void CheckRendering()
    {
        using var host = new Panel();
        host.SuspendLayout();
        foreach (var palette in new[] { FluentPalette.Light, FluentPalette.Dark })
        {
            host.BackColor = palette.Background;
            using var button = new FluentButton { Kind = FluentButtonKind.Subtle, Glyph = FluentGlyph.Refresh,
                Text = "A long original button label / 原来的按钮文字", AutoSize = false, Size = new Size(280, 48) };
            using var toggle = new FluentToggle();
            using var card = new ModeCard(ChargeMode.Normal) { Text = "Original mode / 原模式",
                Description = "Original description / 原来的说明", CurrentText = "Current / 当前使用",
                IsCurrent = true, AutoSize = false, Size = new Size(280, 260) };
            using var glyph = new GlyphView(FluentGlyph.Refresh, 32);
            using var meter = new BatteryMeter { Level = .7f };
            foreach (var control in new Control[] { button, toggle, card, glyph, meter })
            {
                host.Controls.Add(control);
                ((IFluentControl)control).Palette = palette;
                control.BackColor = control is FluentToggle or GlyphView or BatteryMeter ? palette.Surface : palette.Background;
                using var reused = NewCanvas();
                PaintFrame(control, reused, control.ClientRectangle);
                if (control == button)
                {
                    Raise(control, "OnMouseEnter", EventArgs.Empty);
                    PaintFrame(control, reused, control.ClientRectangle);
                    Raise(control, "OnKeyDown", new KeyEventArgs(Keys.Space));
                    PaintFrame(control, reused, control.ClientRectangle);
                    Raise(control, "OnKeyUp", new KeyEventArgs(Keys.Space));
                    control.Text = "New / 新文字";
                    Raise(control, "OnMouseLeave", EventArgs.Empty);
                }
                else if (control == toggle)
                {
                    toggle.Checked = true;
                    PaintFrame(control, reused, control.ClientRectangle);
                    toggle.Checked = false;
                }
                else if (control == card)
                {
                    card.Checked = true;
                    PaintFrame(control, reused, control.ClientRectangle);
                    card.Text = "New mode / 新模式";
                    card.Description = "New / 新说明";
                    card.Checked = card.IsCurrent = false;
                }
                else if (control == glyph)
                {
                    glyph.Glyph = FluentGlyph.Startup;
                    PaintFrame(control, reused, control.ClientRectangle);
                    glyph.Glyph = FluentGlyph.Tray;
                    PaintFrame(control, reused, control.ClientRectangle);
                    glyph.Glyph = FluentGlyph.Language;
                }
                else meter.Level = .3f;
                control.Enabled = false;
                PaintFrame(control, reused, control.ClientRectangle);
                control.Enabled = true;
                PaintFrame(control, reused, control.ClientRectangle);
                using var fresh = NewCanvas();
                PaintFrame(control, fresh, control.ClientRectangle);
                AssertEqual(reused, fresh, $"{control.GetType().Name} retains old text, hover or selection pixels.");

                // A translated target matches DrawToBitmap and buffered/partial paints.
                // Text must honor that translation and leave every outside pixel alone.
                var offset = new Point(19, 23);
                var bounds = new Rectangle(offset, control.Size);
                for (var y = 0; y < fresh.Height; y++)
                for (var x = 0; x < fresh.Width; x++)
                    if (!bounds.Contains(x, y))
                        Assert(fresh.GetPixel(x, y).ToArgb() == Color.Magenta.ToArgb(),
                            $"{control.GetType().Name} paints outside its bounds.");
                foreach (var corner in new[] { offset, new Point(bounds.Right - 1, bounds.Top),
                    new Point(bounds.Left, bounds.Bottom - 1), new Point(bounds.Right - 1, bounds.Bottom - 1) })
                    Assert(fresh.GetPixel(corner.X, corner.Y).ToArgb() == control.BackColor.ToArgb(),
                        $"{control.GetType().Name} leaves a black or unpainted corner.");

                // Neither the background fill nor text may overwrite a partial clip.
                using var partial = NewCanvas();
                var clip = new Rectangle(30, 8, Math.Min(120, control.Width - 30), Math.Min(24, control.Height - 8));
                PaintFrame(control, partial, clip);
                clip.Offset(offset);
                for (var y = 0; y < partial.Height; y++)
                for (var x = 0; x < partial.Width; x++)
                    Assert(partial.GetPixel(x, y).ToArgb() == (clip.Contains(x, y)
                        ? fresh.GetPixel(x, y).ToArgb() : Color.Magenta.ToArgb()),
                        $"{control.GetType().Name} ignores the partial repaint clip.");
                host.Controls.Remove(control);
            }
        }
        Console.WriteLine("PASS Repainting clears old text and states, preserves clips and leaves clean icon corners.");
    }

    private static Bitmap NewCanvas()
    {
        var bitmap = new Bitmap(340, 320);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Magenta);
        return bitmap;
    }

    private static void PaintFrame(Control control, Bitmap bitmap, Rectangle clip)
    {
        using var graphics = Graphics.FromImage(bitmap);
        graphics.TranslateTransform(19, 23);
        graphics.SetClip(clip);
        using var args = new PaintEventArgs(graphics, clip);
        // Mirror the WinForms paint pipeline: Opaque suppresses background painting.
        var opaque = (bool)typeof(Control).GetMethod("GetStyle", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(control, [ControlStyles.Opaque])!;
        if (!opaque) Raise(control, "OnPaintBackground", args);
        Raise(control, "OnPaint", args);
    }

    private static void Raise(Control control, string method, EventArgs args) =>
        typeof(Control).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, [args]);

    private static void AssertEqual(Bitmap actual, Bitmap expected, string message)
    {
        for (var y = 0; y < actual.Height; y++)
        for (var x = 0; x < actual.Width; x++)
        {
            var actualColor = actual.GetPixel(x, y).ToArgb();
            var expectedColor = expected.GetPixel(x, y).ToArgb();
            if (actualColor != expectedColor)
                throw new InvalidOperationException($"{message} First differing pixel ({x}, {y}): actual #{actualColor:X8}, expected #{expectedColor:X8}.");
        }
    }

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
    internal const string CurrentPath = @"D:\New\BatteryCharge.exe";
    internal bool Enabled { get; private set; }
    internal int Changes { get; private set; }
    internal string? RegisteredPath { get; private set; }
    internal bool UnsafeDisabled { get; set; }
    public StartupRegistration Read() => new(Enabled, RegisteredPath is null || RegisteredPath == CurrentPath,
        CurrentPath, RegisteredPath, UnsafeDisabled ? UiText.Get("UnsafeStartupDisabled") : null);
    internal void SimulateTask(bool enabled, string? path) { Enabled = enabled; RegisteredPath = path; UnsafeDisabled = false; }
    public void SetEnabled(bool enabled) { Enabled = enabled; RegisteredPath = enabled ? CurrentPath : null; UnsafeDisabled = false; Changes++; }
    public void RemoveForCleanup() => SetEnabled(false);
}

internal sealed class PreviewTransport : IEnergyTransport
{
    private uint _mode;
    private uint _night = 1;
    internal Exception? ModeError { get; set; }
    internal Exception? NightError { get; set; }
    internal Exception? ReadError { get; set; }
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
        if (ReadError is not null) throw ReadError;
        return controlCode switch
        {
        ChargeProtocol.ModeControlCode => ModeError is null ? _mode : throw ModeError,
        ChargeProtocol.NightControlCode => NightError is null ? _night : throw NightError,
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
