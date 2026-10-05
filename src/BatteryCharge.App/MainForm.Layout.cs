using BatteryCharge.Core;

namespace BatteryCharge.App;

internal sealed partial class MainForm
{
    private readonly List<Font> _ownedFonts = [];
    private readonly Dictionary<ChargeMode, ModeCard> _modeCards = [];
    private readonly List<Label> _secondaryLabels = [];
    private readonly Label _batteryLevel = TextLabel("—");
    private readonly Label _selectionInfo = TextLabel("");
    private readonly Label _pageTitle = TextLabel("");
    private readonly Label _pageSubtitle = TextLabel("");
    private readonly BatteryMeter _batteryMeter = new();
    private readonly FluentButton _overviewNav = new() { Kind = FluentButtonKind.Navigation, Glyph = FluentGlyph.Overview, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 6) };
    private readonly FluentButton _settingsNav = new() { Kind = FluentButtonKind.Navigation, Glyph = FluentGlyph.Settings, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 6) };
    private readonly FluentButton _compactOverview = new() { Kind = FluentButtonKind.Navigation, Glyph = FluentGlyph.Overview };
    private readonly FluentButton _compactSettings = new() { Kind = FluentButtonKind.Navigation, Glyph = FluentGlyph.Settings };
    private readonly FluentButton _detailsButton = new() { Kind = FluentButtonKind.Subtle, Glyph = FluentGlyph.Info };
    private readonly System.Windows.Forms.Timer _powerTimer = new() { Interval = 30_000 };
    private FluentPalette _palette = FluentPalette.Light;
    private TableLayoutPanel _shell = null!;
    private FluentModePanel _modeGrid = null!;
    private Panel _sidebar = null!;
    private Panel _viewport = null!;
    private FluentStackPanel _overviewPage = null!;
    private FluentStackPanel _settingsPage = null!;
    private FlowLayoutPanel _compactNavigation = null!;
    private FluentSurface _diagnostics = null!;
    private ChargeMode _selectedMode = ChargeMode.Normal;
    private bool _settingsVisible;
    private bool _updatingResponsiveLayout;
    private bool _startupHasError;
    private bool _paletteApplied;
    private bool _appearanceUpdateQueued;
    private StatusTone _statusTone;
    private enum StatusTone { Info, Success, Error }

    private void BuildWindow()
    {
        _palette = WindowsAppearance.ReadPalette();
        _shell = new FluentTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        _shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        _shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _sidebar = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 26, 16, 18), Margin = Padding.Empty };
        var brandTitle = Localized(TextLabel(""), "AppName");
        brandTitle.Font = OwnFont(12, FontStyle.Bold);
        brandTitle.Margin = new Padding(0, 12, 0, 4);
        var brand = VerticalPanel(0, new GlyphView(FluentGlyph.Battery, 32), brandTitle, Secondary("BrandSubtitle"));
        brand.Margin = new Padding(0, 0, 0, 30);
        _sidebar.Controls.Add(VerticalPanel(0, brand, Localized(_overviewNav, "OverviewNav"), Localized(_settingsNav, "SettingsNav")));
        var trayHint = VerticalPanel(0, Secondary("TrayHint"));
        trayHint.Dock = DockStyle.Bottom;
        _sidebar.Controls.Add(trayHint);
        _shell.Controls.Add(_sidebar, 0, 0);
        var main = new FluentTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(24, 26, 24, 16), Margin = Padding.Empty };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _compactNavigation = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 12), Visible = false };
        _compactNavigation.Controls.Add(Localized(_compactOverview, "OverviewNav"));
        _compactNavigation.Controls.Add(Localized(_compactSettings, "SettingsNav"));
        main.Controls.Add(_compactNavigation, 0, 0);
        _pageTitle.Font = OwnFont(25, FontStyle.Bold);
        _pageTitle.Margin = new Padding(0, 0, 0, 6);
        _pageSubtitle.Margin = Padding.Empty;
        _secondaryLabels.Add(_pageSubtitle);
        var header = new FluentRowPanel(null, VerticalPanel(0, _pageTitle, _pageSubtitle), Localized(_refresh, "Refresh"))
        { Margin = new Padding(0, 0, 0, 22) };
        _refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _refresh.Margin = new Padding(12, 5, 0, 0);
        main.Controls.Add(header, 0, 1);
        _viewport = new FluentViewport { Dock = DockStyle.Fill, Margin = Padding.Empty, Name = "PageViewport" };
        _overviewPage = BuildOverview();
        _settingsPage = BuildSettings();
        _overviewPage.Dock = _settingsPage.Dock = DockStyle.None;
        _overviewPage.AutoSize = _settingsPage.AutoSize = false;
        _settingsPage.Visible = false;
        _viewport.Controls.Add(_settingsPage);
        _viewport.Controls.Add(_overviewPage);
        main.Controls.Add(_viewport, 0, 2);
        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, WrapContents = true, Margin = new Padding(0, 14, 0, 0) };
        var quit = Localized(new FluentButton { Kind = FluentButtonKind.Subtle, Glyph = FluentGlyph.Exit, Name = "ExitButton" }, "Quit");
        quit.Click += (_, _) => Quit();
        var hide = Localized(new FluentButton { Kind = FluentButtonKind.Subtle, Glyph = FluentGlyph.Tray, Name = "HideButton" }, "Hide");
        hide.Click += (_, _) => Hide();
        footer.Controls.Add(quit); footer.Controls.Add(hide);
        main.Controls.Add(footer, 0, 3);
        _shell.Controls.Add(main, 1, 0);
        Controls.Add(_shell);
        _overviewNav.Click += (_, _) => SetPage(false);
        _settingsNav.Click += (_, _) => SetPage(true);
        _compactOverview.Click += (_, _) => SetPage(false);
        _compactSettings.Click += (_, _) => SetPage(true);
        _detailsButton.Click += (_, _) =>
        {
            using var layout = new FluentLayoutBatch(_overviewPage);
            _diagnostics.Visible = !_diagnostics.Visible;
            _detailsButton.Text = T(_diagnostics.Visible ? "HideDetails" : "ShowDetails");
        };
        _viewport.SizeChanged += (_, _) => UpdateResponsiveLayout();
        _powerTimer.Tick += (_, _) => { if (Visible) RenderPower(); };
        _powerTimer.Start();
        _apply.Name = "ApplyModeButton"; _night.Name = "NightToggle"; _startup.Name = "StartupToggle";
        _languagePicker.Name = "LanguagePicker"; _details.Name = "DiagnosticDetails"; _refresh.Name = "RefreshButton";
        _settingsNav.Name = "SettingsNavigation"; _overviewNav.Name = "OverviewNavigation";
        _overviewPage.Name = "OverviewPage"; _settingsPage.Name = "SettingsPage";
        SetPage(false); SelectMode(ChargeMode.Normal); RenderPower(); ApplyPalette(_palette);
    }

    private FluentStackPanel BuildOverview()
    {
        _batteryLevel.Font = OwnFont(34, FontStyle.Bold);
        _batteryLevel.Margin = new Padding(0, 0, 0, 2);
        _powerState.Margin = new Padding(0, 2, 0, 0);
        _modeState.Font = OwnFont(12, FontStyle.Bold);
        _modeState.Margin = new Padding(0, 4, 0, 8);
        _secondaryLabels.AddRange([_powerState, _lastRead, _nightState, _selectionInfo]);
        var modeSummary = VerticalPanel(0, Secondary("CurrentModeLabel"), _modeState, _lastRead);
        var hero = new FluentRowPanel(_batteryMeter,
            VerticalPanel(0, Secondary("BatteryLevelLabel"), _batteryLevel, _powerState), modeSummary, flexibleAction: true);
        var modeHeading = SectionTitle("ChargeMode");
        modeHeading.Margin = new Padding(0, 12, 0, 5);
        _modeGrid = new FluentModePanel { Name = "ModeGrid" };
        foreach (var mode in Enum.GetValues<ChargeMode>())
        {
            var card = new ModeCard(mode) { Name = $"ModeCard{mode}", Text = ModeName(mode), TabIndex = (int)mode };
            card.Click += (_, _) => SelectMode(mode);
            card.KeyDown += (_, e) =>
            {
                if (e.KeyCode is not (Keys.Left or Keys.Right or Keys.Up or Keys.Down))
                    return;
                var direction = e.KeyCode is Keys.Left or Keys.Up ? -1 : 1;
                var next = (ChargeMode)(((int)mode + direction + 3) % 3);
                SelectMode(next);
                _modeCards[next].Focus();
                e.Handled = e.SuppressKeyPress = true;
            };
            _modeCards.Add(mode, card); _modeGrid.Controls.Add(card);
        }
        _selectionInfo.Anchor = AnchorStyles.Left;
        _selectionInfo.Dock = DockStyle.None;
        _apply.Margin = new Padding(12, 0, 0, 0);
        var applyRow = new FluentRowPanel(null, _selectionInfo, Localized(_apply, "ApplyMode"))
        { Margin = new Padding(0, 0, 0, 18) };
        _night.Text = _night.AccessibleName = T("NightToggle");
        var night = SettingsRow(FluentGlyph.Moon, VerticalPanel(0, SectionTitle("NightCharge"), Hint("NightHint"), _nightState), _night);
        _detailsButton.Text = T("ShowDetails");
        var status = Surface(SettingsRow(FluentGlyph.Info, VerticalPanel(0, _status, _detailsButton), null));
        status.Padding = new Padding(16, 12, 16, 10);
        _details.Height = 130; _details.Dock = DockStyle.Top; _details.BorderStyle = BorderStyle.None; _details.Font = OwnFont(9);
        _diagnostics = Surface(_details); _diagnostics.Visible = false; _diagnostics.Padding = new Padding(16);
        return VerticalPanel(0, Surface(hero), modeHeading, Secondary("ModeIntro"), _modeGrid, applyRow,
            Surface(night), Hint("ConservationHint"), status, _diagnostics);
    }

    private FluentStackPanel BuildSettings()
    {
        _languagePicker.Items.Add(new LanguageChoice("zh-CN", "简体中文"));
        _languagePicker.Items.Add(new LanguageChoice("en-US", "English"));
        _languagePicker.Width = 160; _languagePicker.FlatStyle = FlatStyle.Flat;
        _languagePicker.Anchor = AnchorStyles.Right; _languagePicker.Margin = new Padding(16, 0, 0, 0);
        var language = SettingsRow(FluentGlyph.Language, VerticalPanel(0, SectionTitle("LanguageMenu"), Secondary("LanguageHint")), _languagePicker);
        _startup.Text = _startup.AccessibleName = T("StartupToggle");
        var startup = SettingsRow(FluentGlyph.Tray, VerticalPanel(0, SectionTitle("StartupToggle"), _startupInfo), _startup);
        var cleanup = SettingsRow(FluentGlyph.Settings, VerticalPanel(0, SectionTitle("CleanupTitle"), Secondary("CleanupHint")), Localized(_cleanup, "Cleanup"));
        _cleanup.Margin = new Padding(16, 0, 0, 0); _cleanup.Anchor = AnchorStyles.Right;
        return VerticalPanel(0, Surface(language), Surface(startup),
            Surface(VerticalPanel(0, SectionTitle("TrayBehaviorTitle"), Secondary("TrayBehaviorHint"))), Surface(cleanup));
    }

    private void SelectMode(ChargeMode mode)
    {
        _selectedMode = mode;
        foreach (var pair in _modeCards) pair.Value.Checked = pair.Key == mode;
        UpdateEnabledState();
    }

    private void SetPage(bool settings)
    {
        using var layout = new FluentLayoutBatch(_shell);
        _settingsVisible = settings;
        _overviewPage.Visible = !settings; _settingsPage.Visible = settings;
        _overviewNav.Selected = _compactOverview.Selected = !settings;
        _settingsNav.Selected = _compactSettings.Selected = settings;
        _pageTitle.Text = T(settings ? "SettingsTitle" : "OverviewTitle");
        _pageSubtitle.Text = T(settings ? "SettingsSubtitle" : "OverviewSubtitle");
        _viewport.AutoScrollPosition = Point.Empty;
    }

    private void UpdateResponsiveLayout()
    {
        if (_updatingResponsiveLayout || _modeGrid is null) return;
        _updatingResponsiveLayout = true;
        try
        {
            var scale = DeviceDpi / 96f;
            var compact = ClientSize.Width < 820 * scale;
            var sidebarWidth = compact ? 0 : 200 * scale;
            if (_sidebar.Visible == !compact && _shell.ColumnStyles[0].Width == sidebarWidth
                && _compactNavigation.Visible == compact) return;
            using var layout = new FluentLayoutBatch(_shell);
            _sidebar.Visible = !compact;
            _shell.ColumnStyles[0].Width = sidebarWidth;
            _compactNavigation.Visible = compact;
        }
        finally { _updatingResponsiveLayout = false; }
    }

    private void RenderPower()
    {
        using var layout = new FluentLayoutBatch(_overviewPage);
        var power = SystemInformation.PowerStatus;
        var percent = power.BatteryLifePercent;
        var known = percent is >= 0 and <= 1 && !power.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery);
        _batteryLevel.Text = known ? T("BatteryLevelValue", percent) : "—";
        _batteryMeter.Level = known ? percent : null;
        _batteryMeter.AccessibleName = known ? T("BatteryPercent", percent) : T("BatteryUnknown");
        _powerState.Text = !known ? T("BatteryUnknown") : T(power.PowerLineStatus switch
        { PowerLineStatus.Online => "PowerOnline", PowerLineStatus.Offline => "PowerOffline", _ => "PowerUnknown" });
    }

    private void SetStatus(string text, StatusTone tone = StatusTone.Info)
    {
        _statusTone = tone; _status.Text = text;
        _status.ForeColor = tone switch { StatusTone.Success => _palette.Success, StatusTone.Error => _palette.Error, _ => _palette.Secondary };
        if (tone == StatusTone.Error) { SetPage(false); _diagnostics.Visible = true; _detailsButton.Text = T("HideDetails"); }
    }

    private void SetStartupInfo(string text, bool error = false)
    {
        _startupHasError = error; _startupInfo.Text = text; _startupInfo.ForeColor = error ? _palette.Error : _palette.Secondary;
    }

    internal void ApplyPalette(FluentPalette palette)
    {
        if (_paletteApplied && _palette == palette) return;
        _paletteApplied = true;
        using var layout = new FluentLayoutBatch(this);
        _palette = palette; BackColor = palette.Background; ForeColor = palette.Text;
        ApplyControlPalette(this, palette, false);
        foreach (var label in _secondaryLabels) label.ForeColor = palette.Secondary;
        _startupInfo.ForeColor = _startupHasError ? palette.Error : palette.Secondary;
        _status.ForeColor = _statusTone switch { StatusTone.Success => palette.Success, StatusTone.Error => palette.Error, _ => palette.Secondary };
        _trayMenu.BackColor = palette.Surface; _trayMenu.ForeColor = palette.Text;
        _trayMenu.Renderer = new FluentMenuRenderer(palette);
        if (IsHandleCreated) WindowsAppearance.Apply(Handle, palette);
        Invalidate(invalidateChildren: true);
    }

    private static void ApplyControlPalette(Control control, FluentPalette palette, bool inSurface)
    {
        var background = inSurface ? palette.Surface : palette.Background;
        if (control is IFluentControl fluent) fluent.Palette = palette;
        if (control is FluentSurface) inSurface = true;
        // Opaque child backgrounds avoid recursively repainting every transparent
        // ancestor when a label, glyph or toggle changes. Surface corners retain
        // their parent's background, while their children use the surface color.
        if (control is Label) { control.BackColor = background; control.ForeColor = palette.Text; }
        else if (control is ComboBox or TextBox) { control.BackColor = palette.Surface; control.ForeColor = palette.Text; }
        else control.BackColor = background;
        foreach (Control child in control.Controls) ApplyControlPalette(child, palette, inSurface);
    }

    protected override void OnHandleCreated(EventArgs e)
    { base.OnHandleCreated(e); WindowsAppearance.Apply(Handle, _palette); }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg is 0x001A or 0x031A && IsHandleCreated && !Disposing && !_appearanceUpdateQueued)
        {
            _appearanceUpdateQueued = true;
            BeginInvoke(new Action(() =>
            {
                _appearanceUpdateQueued = false;
                if (!IsDisposed && !Disposing) ApplyPalette(WindowsAppearance.ReadPalette());
            }));
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5)
        {
            if (_refresh.Enabled) _refresh.PerformClick();
            return true;
        }
        if (keyData is (Keys.Alt | Keys.D1) or (Keys.Alt | Keys.D2))
        {
            SetPage(keyData == (Keys.Alt | Keys.D2));
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private Font OwnFont(float size, FontStyle style = FontStyle.Regular)
    { var font = new Font(Font.FontFamily, size, style); _ownedFonts.Add(font); return font; }

    private Label Secondary(string key)
    { var label = Localized(TextLabel(""), key); _secondaryLabels.Add(label); return label; }

    private Label SectionTitle(string key)
    {
        var title = Localized(TextLabel(""), key); title.Font = OwnFont(11, FontStyle.Bold);
        title.Margin = new Padding(0, 0, 0, 5); return title;
    }
    private Label Hint(string key) => Secondary(key);
    private static FluentSurface Surface(Control content)
    { var surface = new FluentSurface(); surface.Controls.Add(content); return surface; }

    private static FluentRowPanel SettingsRow(FluentGlyph glyph, Control description, Control? action) =>
        new(new GlyphView(glyph), description, action);

    private static Label TextLabel(string text) => new FluentLabel
    { Text = text, Dock = DockStyle.Top, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 6) };

    private static FluentStackPanel VerticalPanel(int padding, params Control[] controls)
    {
        var panel = new FluentStackPanel { Padding = new Padding(padding) };
        panel.SuspendLayout();
        panel.Controls.AddRange(controls);
        panel.ResumeLayout(performLayout: false);
        return panel;
    }
}
