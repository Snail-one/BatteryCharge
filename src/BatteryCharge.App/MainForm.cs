using BatteryCharge.Core;

namespace BatteryCharge.App;

internal sealed partial class MainForm : Form
{
    private readonly ChargeController _controller;
    private readonly List<(Control Control, string Key)> _localizedControls = [];
    private readonly List<(ToolStripItem Item, string Key)> _localizedMenus = [];
    private readonly ComboBox _languagePicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 115 };
    private readonly ToolStripMenuItem _languageMenu = new();
    private readonly Dictionary<string, ToolStripMenuItem> _languageItems = [];
    private readonly FluentButton _apply = new() { Kind = FluentButtonKind.Accent };
    private readonly FluentToggle _night = new();
    private readonly FluentToggle _startup = new();
    private readonly FluentButton _cleanup = new() { Kind = FluentButtonKind.Danger };
    private readonly Label _startupInfo = TextLabel(T("ReadingStartup"));
    private readonly IStartupManager _startupManager;
    private readonly string _preferencesPath;
    private readonly ChargeIcons _icons = new();
    private readonly FluentButton _refresh = new() { Kind = FluentButtonKind.Standard, Glyph = FluentGlyph.Refresh };
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
    private readonly ToolStripMenuItem _cleanupItem = new();
    private readonly NotifyIcon _tray;
    private ChargeSnapshot? _snapshot;
    private bool? _startupEnabled;
    private bool _startupBusy;
    private bool _languageBusy;
    private bool _cleanupBusy;
    private bool _updatingLanguage;
    private bool _startInTray;
    private bool _initialized;
    private bool _busy;
    private bool _quitting;

    public MainForm(ChargeController controller, bool startInTray = false,
        IStartupManager? startupManager = null, string? preferencesPath = null)
    {
        SuspendLayout();
        _controller = controller;
        _startupManager = startupManager ?? new StartupManager();
        _preferencesPath = preferencesPath ?? LanguagePreferences.SettingsPath;
        _startInTray = startInTray;
        Text = T("AppName");
        Icon = _icons.WindowFor(null);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        ClientSize = new Size(1020, 760);
        BackColor = FluentPalette.Light.Background;

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

        _apply.Click += async (_, _) => await ApplyModeAsync(_selectedMode);
        _night.Click += async (_, _) => await ToggleNightAsync();
        _startup.Click += async (_, _) => await ToggleStartupAsync();
        _cleanup.Click += async (_, _) => await CleanupAndExitAsync();
        _refresh.Click += async (_, _) => await RefreshAsync();
        Shown += async (_, _) => await InitializeAsync();
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
                Hide();
        };
        UpdateEnabledState();
        ResumeLayout(performLayout: true);
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
        if (!IsDisposed)
            FitWindowToScreen();
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
        _trayMenu.Items.Add(new ToolStripSeparator());
        _cleanupItem.Click += async (_, _) => await CleanupAndExitAsync();
        _trayMenu.Items.Add(LocalizedMenu(_cleanupItem, "Cleanup"));
        _quitItem.Click += (_, _) => Quit();
        _trayMenu.Items.Add(LocalizedMenu(_quitItem, "Quit"));
    }

    private async Task SwitchLanguageAsync(string language)
    {
        if (_busy || _startupBusy || _languageBusy || _cleanupBusy || language == UiText.Language)
        {
            SelectCurrentLanguage();
            return;
        }
        _languageBusy = true;
        UpdateEnabledState();
        var selectedMode = _selectedMode;
        var changed = false;
        try
        {
            await Task.Run(() => LanguagePreferences.Save(_preferencesPath, language));
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
            SelectMode(selectedMode);
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
        using (new FluentLayoutBatch(this))
        {
            Text = T("AppName");
            foreach (var (control, key) in _localizedControls)
                control.Text = T(key);
            foreach (var (item, key) in _localizedMenus)
                item.Text = T(key);
            foreach (var pair in _modeCards)
            {
                pair.Value.Text = ModeName(pair.Key);
                pair.Value.Description = T(pair.Key switch
                {
                    ChargeMode.Conservation => "ModeConservationDescription",
                    ChargeMode.RapidCharge => "ModeRapidDescription",
                    _ => "ModeNormalDescription"
                });
                pair.Value.CurrentText = T("CurrentBadge");
                pair.Value.AccessibleDescription = pair.Value.Description
                    + (pair.Value.IsCurrent ? " " + pair.Value.CurrentText : "");
                pair.Value.Invalidate();
                _modeItems[pair.Key].Text = ModeName(pair.Key);
            }
            _night.Text = _night.AccessibleName = T("NightToggle");
            _startup.Text = _startup.AccessibleName = T("StartupToggle");
            _languagePicker.AccessibleName = T("LanguageMenu");
            _details.AccessibleName = T("ShowDetails");
            _detailsButton.Text = T(_diagnostics.Visible ? "HideDetails" : "ShowDetails");
            SetPage(_settingsVisible);
            RenderPower();
            SelectCurrentLanguage();
            SetStatus(T("ReadingDevice"));
            SetStartupInfo(T("ReadingStartup"));
            if (_snapshot is not null)
                RenderSnapshot(updateSelection: false);
            else
            {
                _modeState.Text = T("Detecting");
                _nightState.Text = T("Detecting");
                _tray.Text = T("TrayMode", T("AppName"), T("Detecting"));
            }
        }
        if (IsHandleCreated)
            FitWindowToScreen();
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

    private Task RefreshAsync() => PerformAsync(async () =>
    {
        await Task.WhenAll(RefreshStartupAsync(), LoadSnapshotAsync());
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
            SetStartupInfo(T("StartupReadFailed", error.Message), error: true);
        }
    }

    private void SetStartupState(StartupRegistration registration)
    {
        var enabled = registration.Enabled;
        _startupEnabled = enabled;
        _startup.Checked = enabled;
        _startupItem.Checked = enabled;
        _startupItem.Text = T("StartupToggle");
        SetStartupInfo(enabled && !registration.UsesCurrentPath
            ? T("StartupMoved") : T(enabled ? "StartupOn" : "StartupOff"));
    }

    private async Task ToggleStartupAsync()
    {
        if (_busy || _startupBusy || _languageBusy || _cleanupBusy)
            return;
        var enabled = _startupEnabled != true;
        _startupBusy = true;
        UpdateEnabledState();
        SetStartupInfo(T("StartupUpdating"));
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
            SetStartupInfo(T("StartupWriteFailed", error.Message), error: true);
            if (!Visible)
                ShowWindow();
        }
        finally
        {
            _startupBusy = false;
            UpdateEnabledState();
        }
    }

    private async Task CleanupAndExitAsync()
    {
        if (_busy || _startupBusy || _languageBusy || _cleanupBusy)
            return;
        _cleanupBusy = true;
        UpdateEnabledState();
        try
        {
            ShowWindow();
            if (MessageBox.Show(this, T("CleanupConfirm"), T("AppName"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2)
                != DialogResult.Yes)
                return;

            SetStatus(T("CleanupWorking"));
            await Task.Run(() => CleanupService.Run(_startupManager.RemoveForCleanup,
                _preferencesPath, LanguagePreferences.LegacySettingsPath));
            MessageBox.Show(this, T("CleanupComplete"), T("AppName"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            _quitting = true;
            Close();
        }
        catch (Exception error)
        {
            // Reflect task removal even when a later configuration-file deletion failed.
            await RefreshStartupAsync();
            SetStatus(T("CleanupIncomplete"), StatusTone.Error);
            _details.Text = error.Message;
            ShowWindow();
            MessageBox.Show(this, T("CleanupFailed", error.Message), T("AppName"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cleanupBusy = false;
            if (!IsDisposed)
                UpdateEnabledState();
        }
    }

    private async Task PerformAsync(Func<Task<string>> operation, bool refreshAfter = true)
    {
        if (_busy || _startupBusy || _languageBusy || _cleanupBusy)
            return;

        _busy = true;
        UpdateEnabledState();
        SetStatus(T("Working"));
        try
        {
            var message = await operation();
            if (refreshAfter)
                await LoadSnapshotAsync();
            SetStatus(message, StatusTone.Success);
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
                foreach (var card in _modeCards.Values)
                    card.IsCurrent = false;
            }

            SetStatus(T("OperationFailed"), StatusTone.Error);
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
        using var layout = new FluentLayoutBatch(_overviewPage);
        var mode = _snapshot.Mode.Value;
        _modeState.Text = mode.HasValue ? ModeName(mode.Value) : T("ModeUnavailable");
        if (mode.HasValue && updateSelection)
            SelectMode(mode.Value);
        foreach (var pair in _modeCards)
            pair.Value.IsCurrent = pair.Key == mode;

        var night = _snapshot.NightCharge.Value;
        _nightState.Text = night.HasValue ? T("CurrentState", T(night.Value ? "On" : "Off")) : T("NightUnavailable");
        _night.Checked = night == true;
        _nightItem.Checked = night == true;
        foreach (var pair in _modeItems)
            pair.Value.Checked = mode == pair.Key;

        _lastRead.Text = T("LastRead", _snapshot.ReadAt);
        RenderPower();
        UpdateModeIcon(mode);

        var problems = new List<string>();
        if (_snapshot.Mode.Error is string modeError)
            problems.Add(T("FeatureProblem", T("ChargeMode"), modeError));
        if (_snapshot.NightCharge.Error is string nightError)
            problems.Add(T("FeatureProblem", T("NightCharge"), nightError));
        _details.Text = problems.Count == 0
            ? T("HealthyDetails")
            : string.Join(Environment.NewLine + Environment.NewLine, problems);
        if (problems.Count > 0)
        {
            _diagnostics.Visible = true;
            _detailsButton.Text = T("HideDetails");
        }
    }

    private void UpdateModeIcon(ChargeMode? mode)
    {
        Icon = _icons.WindowFor(mode);
        _tray.Icon = _icons.TrayFor(mode);
        _tray.Text = T("TrayMode", T("AppName"), mode.HasValue ? ModeName(mode.Value) : T("TrayUnknown"));
    }

    private void UpdateEnabledState()
    {
        using var layout = new FluentLayoutBatch(_overviewPage);
        var idle = !_busy && !_startupBusy && !_languageBusy && !_cleanupBusy;
        var modeAvailable = idle && _snapshot?.Mode.IsAvailable == true;
        foreach (var card in _modeCards.Values)
            card.Enabled = modeAvailable;
        _apply.Enabled = modeAvailable && _selectedMode != _snapshot?.Mode.Value;
        _selectionInfo.Text = _snapshot?.Mode.IsAvailable != true ? T("ModeUnavailable")
            : _selectedMode == _snapshot.Mode.Value ? T("ModeInUse") : T("ModeSelectionPending", ModeName(_selectedMode));
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
        _cleanup.Enabled = idle;
        _cleanupItem.Enabled = idle;
    }

    internal void RequestShowWindow()
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
            return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed && !_quitting)
                    ShowWindow();
            }));
        }
        catch (InvalidOperationException)
        {
            // The window can close between the handle check and dispatch.
        }
    }

    private void ShowWindow()
    {
        _startInTray = false;
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        FitWindowToScreen();
        RenderPower();
        Activate();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitWindowToScreen();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        // Windows applies the suggested bounds after this notification returns.
        BeginInvoke(new Action(() =>
        {
            if (!IsDisposed)
                FitWindowToScreen();
        }));
    }

    private void FitWindowToScreen()
    {
        if (WindowState != FormWindowState.Normal)
            return;

        var workingArea = Screen.FromControl(this).WorkingArea;
        var scale = DeviceDpi / 96f;
        MinimumSize = new Size(Math.Min((int)Math.Ceiling(640 * scale), workingArea.Width),
            Math.Min((int)Math.Ceiling(480 * scale), workingArea.Height));
        Bounds = WindowBounds.Fit(Bounds, workingArea);
        UpdateResponsiveLayout();
    }

    private void Quit()
    {
        if (_busy || _startupBusy || _languageBusy || _cleanupBusy)
        {
            SetStatus(T("WaitBeforeQuit"));
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
            _powerTimer.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _trayMenu.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            _icons.Dispose();
            foreach (var font in _ownedFonts)
                font.Dispose();
        }
    }

    private static string T(string key, params object?[] arguments) => UiText.Get(key, arguments);

    private static string ModeName(ChargeMode mode) => UiText.ModeName(mode);

    private sealed record LanguageChoice(string Language, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

}
