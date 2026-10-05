using BatteryCharge.Core;

namespace BatteryCharge.App;

internal sealed class MainForm : Form
{
    private readonly ChargeController _controller;
    private readonly List<(Control Control, string Key)> _localizedControls = [];
    private readonly List<(ToolStripItem Item, string Key)> _localizedMenus = [];
    private readonly ComboBox _languagePicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 115 };
    private readonly ToolStripMenuItem _languageMenu = new();
    private readonly Dictionary<string, ToolStripMenuItem> _languageItems = [];
    private readonly ComboBox _modePicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 235 };
    private readonly Button _apply = new() { AutoSize = true };
    private readonly CheckBox _night = new() { AutoSize = true, AutoCheck = false };
    private readonly CheckBox _startup = new() { AutoSize = true, AutoCheck = false };
    private readonly Label _startupInfo = TextLabel(T("ReadingStartup"));
    private readonly StartupManager _startupManager = new();
    private readonly ChargeIcons _icons = new();
    private readonly Button _refresh = new() { AutoSize = true };
    private readonly Label _modeState = TextLabel(T("Detecting"));
    private readonly Label _nightState = TextLabel(T("Detecting"));
    private readonly Label _powerState = TextLabel("");
    private readonly Label _lastRead = TextLabel("");
    private readonly Label _status = TextLabel(T("ReadingDevice"));
    private readonly TextBox _details = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = SystemColors.Window
    };
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly Dictionary<ChargeMode, ToolStripMenuItem> _modeItems = [];
    private readonly ToolStripMenuItem _nightItem = new();
    private readonly ToolStripMenuItem _startupItem = new();
    private readonly ToolStripMenuItem _refreshItem = new();
    private readonly ToolStripMenuItem _quitItem = new();
    private readonly NotifyIcon _tray;
    private ChargeSnapshot? _snapshot;
    private bool? _startupEnabled;
    private bool _startupBusy;
    private bool _languageBusy;
    private bool _updatingLanguage;
    private bool _startInTray;
    private bool _initialized;
    private bool _busy;
    private bool _quitting;

    public MainForm(ChargeController controller, bool startInTray = false)
    {
        _controller = controller;
        _startInTray = startInTray;
        Text = T("AppName");
        Icon = _icons.WindowFor(null);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(590, 780);
        MinimumSize = new Size(570, 780);
        BackColor = Color.FromArgb(247, 249, 252);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildWindow();
        BuildTrayMenu();
        _tray = new NotifyIcon
        {
            Icon = _icons.TrayFor(null),
            Text = T("TrayMode", T("AppName"), T("Detecting")),
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowWindow();
        ApplyLanguage();
        _languagePicker.SelectedIndexChanged += async (_, _) =>
        {
            if (!_updatingLanguage && _languagePicker.SelectedItem is LanguageChoice choice)
                await SwitchLanguageAsync(choice.Language);
        };

        _apply.Click += async (_, _) =>
        {
            if (_modePicker.SelectedItem is ModeChoice choice)
                await ApplyModeAsync(choice.Mode);
        };
        _night.Click += async (_, _) => await ToggleNightAsync();
        _startup.Click += async (_, _) => await ToggleStartupAsync();
        _refresh.Click += async (_, _) => await RefreshAsync();
        Shown += async (_, _) => await InitializeAsync();
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
                Hide();
        };
        UpdateEnabledState();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (value && _startInTray)
        {
            _startInTray = false;
            if (!IsHandleCreated)
                CreateHandle();
            // Initialize after the message loop starts, without showing a startup window.
            BeginInvoke(new Action(async () => await InitializeAsync()));
            value = false;
        }
        base.SetVisibleCore(value);
    }

    private async Task InitializeAsync()
    {
        if (_initialized)
            return;
        _initialized = true;
        await RefreshAsync();
    }

    private void BuildWindow()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 9
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 8; row++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = Localized(TextLabel(""), "AppName");
        title.Font = new Font(Font.FontFamily, 19, FontStyle.Bold);
        title.ForeColor = Color.FromArgb(27, 43, 65);
        title.Margin = new Padding(0, 0, 0, 7);
        var header = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 3, RowCount = 1 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(Localized(TextLabel(""), "Language"), 1, 0);
        _languagePicker.Items.Add(new LanguageChoice("zh-CN", "简体中文"));
        _languagePicker.Items.Add(new LanguageChoice("en-US", "English"));
        header.Controls.Add(_languagePicker, 2, 0);
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(Hint("Intro"), 0, 1);

        var modePanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12)
        };
        foreach (var mode in Enum.GetValues<ChargeMode>())
            _modePicker.Items.Add(new ModeChoice(mode));
        _modePicker.SelectedIndex = 0;
        modePanel.Controls.Add(_modeState);
        var selectRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        selectRow.Controls.Add(_modePicker);
        selectRow.Controls.Add(Localized(_apply, "ApplyMode"));
        modePanel.Controls.Add(selectRow);
        modePanel.Controls.Add(Hint("ConservationHint"));
        layout.Controls.Add(Localized(Card("", modePanel), "ChargeMode"), 0, 2);

        var nightPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12)
        };
        nightPanel.Controls.Add(_nightState);
        nightPanel.Controls.Add(Localized(_night, "NightToggle"));
        nightPanel.Controls.Add(Hint("NightHint"));
        layout.Controls.Add(Localized(Card("", nightPanel), "NightCharge"), 0, 3);

        var startupPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12)
        };
        _startupInfo.MaximumSize = new Size(470, 0);
        startupPanel.Controls.Add(Localized(_startup, "StartupToggle"));
        startupPanel.Controls.Add(_startupInfo);
        layout.Controls.Add(Localized(Card("", startupPanel), "StartupSettings"), 0, 4);

        var summary = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        summary.Controls.Add(_powerState);
        summary.Controls.Add(_lastRead);
        layout.Controls.Add(summary, 0, 5);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
        actions.Controls.Add(Localized(_refresh, "Refresh"));
        var hide = Localized(new Button { AutoSize = true }, "Hide");
        hide.Click += (_, _) => Hide();
        actions.Controls.Add(hide);
        var quit = Localized(new Button { AutoSize = true }, "Quit");
        quit.Click += (_, _) => Quit();
        actions.Controls.Add(quit);
        layout.Controls.Add(actions, 0, 6);

        _status.MaximumSize = new Size(500, 0);
        _status.Margin = new Padding(0, 8, 0, 8);
        layout.Controls.Add(_status, 0, 7);
        layout.Controls.Add(_details, 0, 8);
        Controls.Add(layout);
    }

    private void BuildTrayMenu()
    {
        var open = LocalizedMenu(new ToolStripMenuItem(), "Open");
        open.Click += (_, _) => ShowWindow();
        _trayMenu.Items.Add(open);
        _trayMenu.Items.Add(new ToolStripSeparator());
        foreach (var mode in Enum.GetValues<ChargeMode>())
        {
            var item = new ToolStripMenuItem(ModeName(mode));
            item.Click += async (_, _) => await ApplyModeAsync(mode);
            _modeItems.Add(mode, item);
            _trayMenu.Items.Add(item);
        }

        _nightItem.Click += async (_, _) => await ToggleNightAsync();
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(LocalizedMenu(_nightItem, "NightToggle"));
        _startupItem.Click += async (_, _) => await ToggleStartupAsync();
        _trayMenu.Items.Add(LocalizedMenu(_startupItem, "StartupToggle"));
        foreach (var choice in _languagePicker.Items.Cast<LanguageChoice>())
        {
            var item = new ToolStripMenuItem(choice.DisplayName);
            item.Click += async (_, _) => await SwitchLanguageAsync(choice.Language);
            _languageItems.Add(choice.Language, item);
            _languageMenu.DropDownItems.Add(item);
        }
        _trayMenu.Items.Add(LocalizedMenu(_languageMenu, "LanguageMenu"));
        _refreshItem.Click += async (_, _) => await RefreshAsync();
        _trayMenu.Items.Add(LocalizedMenu(_refreshItem, "Refresh"));
        _quitItem.Click += (_, _) => Quit();
        _trayMenu.Items.Add(LocalizedMenu(_quitItem, "Quit"));
    }

    private async Task SwitchLanguageAsync(string language)
    {
        if (_busy || _startupBusy || _languageBusy || language == UiText.Language)
        {
            SelectCurrentLanguage();
            return;
        }
        _languageBusy = true;
        UpdateEnabledState();
        var selectedMode = (_modePicker.SelectedItem as ModeChoice)?.Mode;
        var changed = false;
        try
        {
            await Task.Run(() => LanguagePreferences.Save(LanguagePreferences.SettingsPath, language));
            UiText.SetLanguage(language);
            ApplyLanguage();
            changed = true;
        }
        catch (Exception error)
        {
            SelectCurrentLanguage();
            MessageBox.Show(this, T("LanguageSaveFailed", error.Message), T("AppName"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _languageBusy = false;
            UpdateEnabledState();
        }
        // Re-read localized driver errors; this only queries the device and startup task.
        if (changed)
        {
            await RefreshAsync();
            if (selectedMode.HasValue)
                _modePicker.SelectedItem = _modePicker.Items.Cast<ModeChoice>()
                    .Single(choice => choice.Mode == selectedMode.Value);
        }
    }

    private void SelectCurrentLanguage()
    {
        _updatingLanguage = true;
        try
        {
            _languagePicker.SelectedItem = _languagePicker.Items.Cast<LanguageChoice>()
                .Single(choice => choice.Language == UiText.Language);
            foreach (var pair in _languageItems)
                pair.Value.Checked = pair.Key == UiText.Language;
        }
        finally
        {
            _updatingLanguage = false;
        }
    }

    private void ApplyLanguage()
    {
        SuspendLayout();
        try
        {
            Text = T("AppName");
            foreach (var (control, key) in _localizedControls)
                control.Text = T(key);
            foreach (var (item, key) in _localizedMenus)
                item.Text = T(key);
            var selected = (_modePicker.SelectedItem as ModeChoice)?.Mode;
            _modePicker.Items.Clear();
            foreach (var mode in Enum.GetValues<ChargeMode>())
            {
                var choice = new ModeChoice(mode);
                _modePicker.Items.Add(choice);
                _modeItems[mode].Text = ModeName(mode);
                if (mode == selected)
                    _modePicker.SelectedItem = choice;
            }
            if (_modePicker.SelectedIndex < 0)
                _modePicker.SelectedIndex = 0;
            SelectCurrentLanguage();
            _status.Text = T("ReadingDevice");
            _startupInfo.Text = T("ReadingStartup");
            if (_snapshot is not null)
                RenderSnapshot(updateSelection: false);
            else
            {
                _modeState.Text = T("Detecting");
                _nightState.Text = T("Detecting");
                _tray.Text = T("TrayMode", T("AppName"), T("Detecting"));
            }
        }
        finally
        {
            ResumeLayout(performLayout: true);
        }
    }

    private TControl Localized<TControl>(TControl control, string key) where TControl : Control
    {
        _localizedControls.Add((control, key));
        control.Text = T(key);
        return control;
    }

    private ToolStripMenuItem LocalizedMenu(ToolStripMenuItem item, string key)
    {
        _localizedMenus.Add((item, key));
        item.Text = T(key);
        return item;
    }

    private Label Hint(string key)
    {
        var label = Localized(TextLabel(""), key);
        label.MaximumSize = new Size(440, 0);
        return label;
    }

    private Task RefreshAsync() => PerformAsync(async () =>
    {
        await RefreshStartupAsync();
        await LoadSnapshotAsync();
        return _snapshot is { Mode.IsAvailable: true, NightCharge.IsAvailable: true }
            ? T("StatusUpdated")
            : T("StatusPartial");
    }, refreshAfter: false);

    private Task ApplyModeAsync(ChargeMode mode) => PerformAsync(async () =>
    {
        var actual = await _controller.SetModeAsync(mode);
        return T("ModeApplied", ModeName(actual));
    });

    private Task ToggleNightAsync()
    {
        if (_busy || _snapshot?.NightCharge.Value is not bool current)
            return Task.CompletedTask;

        return PerformAsync(async () =>
        {
            var actual = await _controller.SetNightChargeAsync(!current);
            return T("NightApplied", T(actual ? "Enabled" : "Disabled"));
        });
    }

    private async Task RefreshStartupAsync()
    {
        try
        {
            SetStartupState(await Task.Run(_startupManager.Read));
        }
        catch (Exception error)
        {
            _startupEnabled = null;
            _startup.Checked = false;
            _startupItem.Checked = false;
            _startupItem.Text = T("StartupUnknown");
            _startupInfo.Text = T("StartupReadFailed", error.Message);
            _startupInfo.ForeColor = Color.FromArgb(174, 49, 49);
        }
    }

    private void SetStartupState(StartupRegistration registration)
    {
        var enabled = registration.Enabled;
        _startupEnabled = enabled;
        _startup.Checked = enabled;
        _startupItem.Checked = enabled;
        _startupItem.Text = T("StartupToggle");
        _startupInfo.Text = enabled && !registration.UsesCurrentPath
            ? T("StartupMoved")
            : enabled
                ? T("StartupOn")
                : T("StartupOff");
        _startupInfo.ForeColor = Color.FromArgb(69, 85, 105);
    }

    private async Task ToggleStartupAsync()
    {
        if (_busy || _startupBusy || _languageBusy)
            return;
        var enabled = _startupEnabled != true;
        _startupBusy = true;
        UpdateEnabledState();
        _startupInfo.Text = T("StartupUpdating");
        try
        {
            await Task.Run(() => _startupManager.SetEnabled(enabled));
            var actual = await Task.Run(_startupManager.Read);
            if (actual.Enabled != enabled || (enabled && !actual.UsesCurrentPath))
                throw new IOException(T("StartupNotApplied"));
            SetStartupState(actual);
        }
        catch (Exception error)
        {
            await RefreshStartupAsync();
            _startupInfo.Text = T("StartupWriteFailed", error.Message);
            _startupInfo.ForeColor = Color.FromArgb(174, 49, 49);
            if (!Visible)
                ShowWindow();
        }
        finally
        {
            _startupBusy = false;
            UpdateEnabledState();
        }
    }

    private async Task PerformAsync(Func<Task<string>> operation, bool refreshAfter = true)
    {
        if (_busy || _startupBusy || _languageBusy)
            return;

        _busy = true;
        UpdateEnabledState();
        _status.Text = T("Working");
        _status.ForeColor = Color.FromArgb(69, 85, 105);
        try
        {
            var message = await operation();
            if (refreshAfter)
                await LoadSnapshotAsync();
            _status.Text = message;
            _status.ForeColor = Color.FromArgb(25, 110, 70);
        }
        catch (Exception error)
        {
            // Never leave an optimistic check mark after an unsuccessful write.
            _snapshot = null;
            try
            {
                await LoadSnapshotAsync();
            }
            catch
            {
                _modeState.Text = T("ModeReadUnknown");
                _nightState.Text = T("NightReadUnknown");
                _night.Checked = false;
                foreach (var item in _modeItems.Values)
                    item.Checked = false;
                _nightItem.Checked = false;
                UpdateModeIcon(null);
            }

            _status.Text = T("OperationFailed");
            _status.ForeColor = Color.FromArgb(174, 49, 49);
            _details.Text = error.Message + Environment.NewLine + _details.Text;
            if (!Visible)
            {
                _tray.ShowBalloonTip(5000, T("AppName"), T("FailureBalloon"), ToolTipIcon.Warning);
                ShowWindow();
            }
        }
        finally
        {
            _busy = false;
            UpdateEnabledState();
        }
    }

    private async Task LoadSnapshotAsync()
    {
        _snapshot = await _controller.ReadAsync();
        RenderSnapshot();
    }

    private void RenderSnapshot(bool updateSelection = true)
    {
        if (_snapshot is null)
            return;
        var mode = _snapshot.Mode.Value;
        _modeState.Text = mode.HasValue ? T("CurrentMode", ModeName(mode.Value)) : T("ModeUnavailable");
        if (mode.HasValue && updateSelection)
            _modePicker.SelectedItem = _modePicker.Items.Cast<ModeChoice>().Single(choice => choice.Mode == mode.Value);

        var night = _snapshot.NightCharge.Value;
        _nightState.Text = night.HasValue ? T("CurrentState", T(night.Value ? "On" : "Off")) : T("NightUnavailable");
        _night.Checked = night == true;
        _nightItem.Checked = night == true;
        foreach (var pair in _modeItems)
            pair.Value.Checked = mode == pair.Key;

        _lastRead.Text = T("LastRead", _snapshot.ReadAt);
        var power = SystemInformation.PowerStatus;
        var percent = power.BatteryLifePercent;
        var battery = percent is >= 0 and <= 1 ? T("BatteryPercent", percent) : T("BatteryUnknown");
        var source = power.PowerLineStatus switch
        {
            PowerLineStatus.Online => T("PowerOnline"),
            PowerLineStatus.Offline => T("PowerOffline"),
            _ => T("PowerUnknown")
        };
        _powerState.Text = $"{battery} · {source}";
        UpdateModeIcon(mode);

        var problems = new List<string>();
        if (_snapshot.Mode.Error is string modeError)
            problems.Add(T("FeatureProblem", T("ChargeMode"), modeError));
        if (_snapshot.NightCharge.Error is string nightError)
            problems.Add(T("FeatureProblem", T("NightCharge"), nightError));
        _details.Text = problems.Count == 0
            ? T("HealthyDetails")
            : string.Join(Environment.NewLine + Environment.NewLine, problems);
    }

    private void UpdateModeIcon(ChargeMode? mode)
    {
        Icon = _icons.WindowFor(mode);
        _tray.Icon = _icons.TrayFor(mode);
        _tray.Text = T("TrayMode", T("AppName"), mode.HasValue ? ModeName(mode.Value) : T("TrayUnknown"));
    }

    private void UpdateEnabledState()
    {
        var idle = !_busy && !_startupBusy && !_languageBusy;
        var modeAvailable = idle && _snapshot?.Mode.IsAvailable == true;
        _modePicker.Enabled = modeAvailable;
        _apply.Enabled = modeAvailable;
        foreach (var item in _modeItems.Values)
            item.Enabled = modeAvailable;
        _night.Enabled = idle && _snapshot?.NightCharge.IsAvailable == true;
        _nightItem.Enabled = _night.Enabled;
        _startup.Enabled = idle;
        _startupItem.Enabled = idle;
        _refresh.Enabled = idle;
        _refreshItem.Enabled = idle;
        _quitItem.Enabled = idle;
        _languagePicker.Enabled = idle;
        _languageMenu.Enabled = idle;
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void Quit()
    {
        if (_busy || _startupBusy || _languageBusy)
        {
            _status.Text = T("WaitBeforeQuit");
            ShowWindow();
            return;
        }

        _quitting = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Allow Windows shutdown/task-manager closure. The device handle is safe-handled.
        if (!_quitting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _trayMenu.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
            _icons.Dispose();
    }

    private static Label TextLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Color.FromArgb(69, 85, 105),
        Margin = new Padding(0, 3, 0, 7)
    };

    private static GroupBox Card(string title, Control content)
    {
        var card = new GroupBox
        {
            Text = title,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(8),
            Margin = new Padding(0, 12, 0, 0)
        };
        card.Controls.Add(content);
        return card;
    }

    private static string T(string key, params object?[] arguments) => UiText.Get(key, arguments);

    private static string ModeName(ChargeMode mode) => UiText.ModeName(mode);

    private sealed record LanguageChoice(string Language, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record ModeChoice(ChargeMode Mode)
    {
        public override string ToString() => ModeName(Mode);
    }
}
