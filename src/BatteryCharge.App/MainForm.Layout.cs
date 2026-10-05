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
    private TableLayoutPanel _modeGrid = null!;
    private Panel _sidebar = null!;
    private Panel _viewport = null!;
    private TableLayoutPanel _overviewPage = null!;
    private TableLayoutPanel _settingsPage = null!;
    private FlowLayoutPanel _compactNavigation = null!;
    private FluentSurface _diagnostics = null!;
    private ChargeMode _selectedMode = ChargeMode.Normal;
    private bool _settingsVisible;
    private bool _stackedModes;
    private bool _updatingResponsiveLayout;
    private bool _startupHasError;
    private StatusTone _statusTone;
    private enum StatusTone { Info, Success, Error }

    private void BuildWindow()
    {
        _palette = WindowsAppearance.ReadPalette();
        _shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
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
        var trayHint = Secondary("TrayHint");
        trayHint.Dock = DockStyle.Bottom;
        _sidebar.Controls.Add(trayHint);
        _shell.Controls.Add(_sidebar, 0, 0);
        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(24, 26, 24, 16), Margin = Padding.Empty };
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
        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 22) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(VerticalPanel(0, _pageTitle, _pageSubtitle), 0, 0);
        _refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _refresh.Margin = new Padding(12, 5, 0, 0);
        header.Controls.Add(Localized(_refresh, "Refresh"), 1, 0);
        main.Controls.Add(header, 0, 1);
        _viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        _overviewPage = BuildOverview();
        _settingsPage = BuildSettings();
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
            _diagnostics.Visible = !_diagnostics.Visible;
            _detailsButton.Text = T(_diagnostics.Visible ? "HideDetails" : "ShowDetails");
        };
        _viewport.SizeChanged += (_, _) => UpdateResponsiveLayout();
        _powerTimer.Tick += (_, _) => RenderPower();
        _powerTimer.Start();
        _apply.Name = "ApplyModeButton"; _night.Name = "NightToggle"; _startup.Name = "StartupToggle";
        _languagePicker.Name = "LanguagePicker"; _details.Name = "DiagnosticDetails"; _refresh.Name = "RefreshButton";
        _settingsNav.Name = "SettingsNavigation"; _overviewNav.Name = "OverviewNavigation";
        _overviewPage.Name = "OverviewPage"; _settingsPage.Name = "SettingsPage";
        SetPage(false); SelectMode(ChargeMode.Normal); RenderPower(); ApplyPalette(_palette);
    }

    private TableLayoutPanel BuildOverview()
    {
        _batteryLevel.Font = OwnFont(34, FontStyle.Bold);
        _batteryLevel.Margin = new Padding(0, 0, 0, 2);
        _powerState.Margin = new Padding(0, 2, 0, 0);
        _modeState.Font = OwnFont(12, FontStyle.Bold);
        _modeState.Margin = new Padding(0, 4, 0, 8);
        _secondaryLabels.AddRange([_powerState, _lastRead, _nightState, _selectionInfo]);
        var hero = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        hero.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        hero.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        hero.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        hero.Controls.Add(_batteryMeter, 0, 0);
        hero.Controls.Add(VerticalPanel(0, Secondary("BatteryLevelLabel"), _batteryLevel, _powerState), 1, 0);
        var modeSummary = VerticalPanel(0, Secondary("CurrentModeLabel"), _modeState, _lastRead);
        modeSummary.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        hero.Controls.Add(modeSummary, 2, 0);
        var modeHeading = SectionTitle("ChargeMode");
        modeHeading.Margin = new Padding(0, 12, 0, 5);
        _modeGrid = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 12, 0, 12) };
        for (var index = 0; index < 3; index++) _modeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        _modeGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
            _modeCards.Add(mode, card); _modeGrid.Controls.Add(card, (int)mode, 0);
        }
        _modeCards[ChargeMode.RapidCharge].Margin = Padding.Empty;
        var applyRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 18) };
        applyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        applyRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _selectionInfo.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _selectionInfo.Dock = DockStyle.None;
        applyRow.Controls.Add(_selectionInfo, 0, 0);
        _apply.Margin = new Padding(12, 0, 0, 0);
        applyRow.Controls.Add(Localized(_apply, "ApplyMode"), 1, 0);
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

    private TableLayoutPanel BuildSettings()
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
            _sidebar.Visible = !compact;
            _shell.ColumnStyles[0].Width = compact ? 0 : 200 * scale;
            _compactNavigation.Visible = compact;
            var contentWidth = ClientSize.Width - (compact ? 0 : 200 * scale)
                - 48 * scale - SystemInformation.VerticalScrollBarWidth;
            var stack = contentWidth < 580 * scale;
            if (stack == _stackedModes) return;
            _stackedModes = stack;
            _modeGrid.SuspendLayout(); _modeGrid.ColumnStyles.Clear(); _modeGrid.RowStyles.Clear();
            _modeGrid.ColumnCount = stack ? 1 : 3; _modeGrid.RowCount = stack ? 3 : 1;
            for (var index = 0; index < _modeGrid.ColumnCount; index++) _modeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / _modeGrid.ColumnCount));
            for (var index = 0; index < _modeGrid.RowCount; index++) _modeGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            foreach (var pair in _modeCards)
            {
                _modeGrid.SetCellPosition(pair.Value, new TableLayoutPanelCellPosition(stack ? 0 : (int)pair.Key, stack ? (int)pair.Key : 0));
                pair.Value.Margin = stack ? new Padding(0, 0, 0, (int)(10 * scale)) : new Padding(0, 0, pair.Key == ChargeMode.RapidCharge ? 0 : (int)(10 * scale), 0);
            }
            _modeGrid.ResumeLayout(performLayout: true);
        }
        finally { _updatingResponsiveLayout = false; }
    }

    private void RenderPower()
    {
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
        if (control is IFluentControl fluent) fluent.Palette = palette;
        if (control is FluentSurface) inSurface = true;
        if (control is Label) { control.BackColor = Color.Transparent; control.ForeColor = palette.Text; }
        else if (control is ComboBox or TextBox) { control.BackColor = palette.Surface; control.ForeColor = palette.Text; }
        else if (control is not IFluentControl) control.BackColor = inSurface ? Color.Transparent : palette.Background;
        foreach (Control child in control.Controls) ApplyControlPalette(child, palette, inSurface);
    }

    protected override void OnHandleCreated(EventArgs e)
    { base.OnHandleCreated(e); WindowsAppearance.Apply(Handle, _palette); }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg is 0x001A or 0x031A && IsHandleCreated && !Disposing)
            BeginInvoke(new Action(() => { if (!IsDisposed) ApplyPalette(WindowsAppearance.ReadPalette()); }));
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

    private static TableLayoutPanel SettingsRow(FluentGlyph glyph, Control description, Control? action)
    {
        var row = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = action is null ? 2 : 3, RowCount = 1, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new GlyphView(glyph), 0, 0); row.Controls.Add(description, 1, 0);
        if (action is not null) { row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); row.Controls.Add(action, 2, 0); }
        return row;
    }

    private static Label TextLabel(string text) => new()
    { Text = text, AutoSize = true, Dock = DockStyle.Top, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 6) };

    private static TableLayoutPanel VerticalPanel(int padding, params Control[] controls)
    {
        var panel = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, RowCount = controls.Length, Padding = new Padding(padding), Margin = Padding.Empty };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < controls.Length; row++) { panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.Controls.Add(controls[row], 0, row); }
        return panel;
    }
}
