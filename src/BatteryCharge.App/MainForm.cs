using BatteryCharge.Core;

namespace BatteryCharge.App;

internal sealed class MainForm : Form
{
    private readonly ChargeController _controller;
    private readonly ComboBox _modePicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 235 };
    private readonly Button _apply = new() { Text = "应用充电模式", AutoSize = true };
    private readonly CheckBox _night = new() { Text = "开启夜间充电", AutoSize = true, AutoCheck = false };
    private readonly CheckBox _startup = new() { Text = "开机自动启动", AutoSize = true, AutoCheck = false };
    private readonly Label _startupInfo = TextLabel("正在读取自动启动设置…");
    private readonly StartupManager _startupManager = new();
    private readonly ChargeIcons _icons = new();
    private readonly Button _refresh = new() { Text = "刷新状态", AutoSize = true };
    private readonly Label _modeState = TextLabel("正在检测…");
    private readonly Label _nightState = TextLabel("正在检测…");
    private readonly Label _powerState = TextLabel("");
    private readonly Label _lastRead = TextLabel("");
    private readonly Label _status = TextLabel("正在读取设备状态…");
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
    private readonly ToolStripMenuItem _nightItem = new("开启夜间充电");
    private readonly ToolStripMenuItem _startupItem = new("开机自动启动");
    private readonly ToolStripMenuItem _refreshItem = new("刷新状态");
    private readonly ToolStripMenuItem _quitItem = new("退出");
    private readonly NotifyIcon _tray;
    private ChargeSnapshot? _snapshot;
    private bool? _startupEnabled;
    private bool _startupBusy;
    private bool _startInTray;
    private bool _initialized;
    private bool _busy;
    private bool _quitting;

    public MainForm(ChargeController controller, bool startInTray = false)
    {
        _controller = controller;
        _startInTray = startInTray;
        Text = "电池充电助手";
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
            Text = "电池充电助手 · 正在检测",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowWindow();

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
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 9
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 8; row++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = TextLabel("电池充电助手");
        title.Font = new Font(Font.FontFamily, 19, FontStyle.Bold);
        title.ForeColor = Color.FromArgb(27, 43, 65);
        title.Margin = new Padding(0, 0, 0, 7);
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(TextLabel("选择充电策略，查看设备的实际状态。"), 0, 1);

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
        var selectRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        selectRow.Controls.Add(_modePicker);
        selectRow.Controls.Add(_apply);
        modePanel.Controls.Add(selectRow);
        modePanel.Controls.Add(TextLabel("养护模式的充电上限由设备决定，不能自定义百分比。"));
        layout.Controls.Add(Card("充电模式", modePanel), 0, 2);

        var nightPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12)
        };
        nightPanel.Controls.Add(_nightState);
        nightPanel.Controls.Add(_night);
        nightPanel.Controls.Add(TextLabel("夜间先充到 80%，早晨再补满；具体安排由设备决定。"));
        layout.Controls.Add(Card("夜间充电", nightPanel), 0, 3);

        var startupPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12)
        };
        _startupInfo.MaximumSize = new Size(470, 0);
        startupPanel.Controls.Add(_startup);
        startupPanel.Controls.Add(_startupInfo);
        layout.Controls.Add(Card("启动设置", startupPanel), 0, 4);

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

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false };
        actions.Controls.Add(_refresh);
        var hide = new Button { Text = "收起到托盘", AutoSize = true };
        hide.Click += (_, _) => Hide();
        actions.Controls.Add(hide);
        var quit = new Button { Text = "退出", AutoSize = true };
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
        var open = new ToolStripMenuItem("打开电池充电助手");
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
        _trayMenu.Items.Add(_nightItem);
        _startupItem.Click += async (_, _) => await ToggleStartupAsync();
        _trayMenu.Items.Add(_startupItem);
        _refreshItem.Click += async (_, _) => await RefreshAsync();
        _trayMenu.Items.Add(_refreshItem);
        _quitItem.Click += (_, _) => Quit();
        _trayMenu.Items.Add(_quitItem);
    }

    private Task RefreshAsync() => PerformAsync(async () =>
    {
        await RefreshStartupAsync();
        await LoadSnapshotAsync();
        return _snapshot is { Mode.IsAvailable: true, NightCharge.IsAvailable: true }
            ? "状态已更新。"
            : "状态已更新；部分功能不可用，请查看下方说明。";
    }, refreshAfter: false);

    private Task ApplyModeAsync(ChargeMode mode) => PerformAsync(async () =>
    {
        var actual = await _controller.SetModeAsync(mode);
        return $"已切换到{ModeName(actual)}，并确认设备状态。";
    });

    private Task ToggleNightAsync()
    {
        if (_busy || _snapshot?.NightCharge.Value is not bool current)
            return Task.CompletedTask;

        return PerformAsync(async () =>
        {
            var actual = await _controller.SetNightChargeAsync(!current);
            return $"夜间充电已{(actual ? "开启" : "关闭")}，并确认设备状态。";
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
            _startupItem.Text = "开启自动启动（状态未知）";
            _startupInfo.Text = $"自动启动状态读取失败：{error.Message}";
            _startupInfo.ForeColor = Color.FromArgb(174, 49, 49);
        }
    }

    private void SetStartupState(StartupRegistration registration)
    {
        var enabled = registration.Enabled;
        _startupEnabled = enabled;
        _startup.Checked = enabled;
        _startupItem.Checked = enabled;
        _startupItem.Text = "开机自动启动";
        _startupInfo.Text = enabled && !registration.UsesCurrentPath
            ? "已开启，但程序位置已变化；请关闭后重新开启以更新路径。"
            : enabled
                ? "已开启：登录后自动运行并收起到托盘。移动程序后请重新开启。"
                : "已关闭：登录后不会自动启动。";
        _startupInfo.ForeColor = Color.FromArgb(69, 85, 105);
    }

    private async Task ToggleStartupAsync()
    {
        if (_busy || _startupBusy)
            return;
        var enabled = _startupEnabled != true;
        _startupBusy = true;
        UpdateEnabledState();
        _startupInfo.Text = "正在更新自动启动设置…";
        try
        {
            await Task.Run(() => _startupManager.SetEnabled(enabled));
            var actual = await Task.Run(_startupManager.Read);
            if (actual.Enabled != enabled || (enabled && !actual.UsesCurrentPath))
                throw new IOException("自动启动设置未生效，请刷新后重试。");
            SetStartupState(actual);
        }
        catch (Exception error)
        {
            await RefreshStartupAsync();
            _startupInfo.Text = $"自动启动设置未完成：{error.Message}";
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
        if (_busy || _startupBusy)
            return;

        _busy = true;
        UpdateEnabledState();
        _status.Text = "正在读取或应用设置，请稍候…";
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
                _modeState.Text = "当前模式：未知，请刷新状态";
                _nightState.Text = "夜间充电：未知，请刷新状态";
                _night.Checked = false;
                foreach (var item in _modeItems.Values)
                    item.Checked = false;
                _nightItem.Checked = false;
                UpdateModeIcon(null);
            }

            _status.Text = "操作未完成，请查看下方原因。";
            _status.ForeColor = Color.FromArgb(174, 49, 49);
            _details.Text = error.Message + Environment.NewLine + _details.Text;
            if (!Visible)
            {
                _tray.ShowBalloonTip(5000, "电池充电助手", "操作未完成，打开窗口查看原因。", ToolTipIcon.Warning);
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
        var mode = _snapshot.Mode.Value;
        _modeState.Text = mode.HasValue ? $"当前模式：{ModeName(mode.Value)}" : "充电模式不可用";
        if (mode.HasValue)
            _modePicker.SelectedItem = _modePicker.Items.Cast<ModeChoice>().Single(choice => choice.Mode == mode.Value);

        var night = _snapshot.NightCharge.Value;
        _nightState.Text = night.HasValue ? $"当前状态：{(night.Value ? "已开启" : "已关闭")}" : "夜间充电不可用";
        _night.Checked = night == true;
        _nightItem.Checked = night == true;
        foreach (var pair in _modeItems)
            pair.Value.Checked = mode == pair.Key;

        _lastRead.Text = $"最后读取：{_snapshot.ReadAt:HH:mm:ss}";
        var power = SystemInformation.PowerStatus;
        var percent = power.BatteryLifePercent;
        var battery = percent is >= 0 and <= 1 ? $"电量 {percent:P0}" : "电量未知";
        var source = power.PowerLineStatus switch
        {
            PowerLineStatus.Online => "已接通电源",
            PowerLineStatus.Offline => "使用电池",
            _ => "供电状态未知"
        };
        _powerState.Text = $"{battery} · {source}";
        UpdateModeIcon(mode);

        var problems = new List<string>();
        if (_snapshot.Mode.Error is string modeError)
            problems.Add($"充电模式：{modeError}");
        if (_snapshot.NightCharge.Error is string nightError)
            problems.Add($"夜间充电：{nightError}");
        _details.Text = problems.Count == 0
            ? "设备状态读取正常。关闭窗口会收起到托盘，使用“退出”结束程序。"
            : string.Join(Environment.NewLine + Environment.NewLine, problems);
    }

    private void UpdateModeIcon(ChargeMode? mode)
    {
        Icon = _icons.WindowFor(mode);
        _tray.Icon = _icons.TrayFor(mode);
        _tray.Text = mode.HasValue ? $"电池充电助手 · {ModeName(mode.Value)}" : "电池充电助手 · 模式未知";
    }

    private void UpdateEnabledState()
    {
        var idle = !_busy && !_startupBusy;
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
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void Quit()
    {
        if (_busy || _startupBusy)
        {
            _status.Text = "请等待当前操作完成后再退出。";
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

    private static string ModeName(ChargeMode mode) => mode switch
    {
        ChargeMode.Normal => "普通充电",
        ChargeMode.Conservation => "电池养护",
        ChargeMode.RapidCharge => "快速充电",
        _ => "未知模式"
    };

    private sealed record ModeChoice(ChargeMode Mode)
    {
        public override string ToString() => ModeName(Mode);
    }
}
