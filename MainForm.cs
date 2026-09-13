using System.Text;

namespace ImmichUploader;

/// <summary>
/// Bridge the main window uses to drive the app. Implemented by the tray
/// context so the window and the tray menu offer exactly the same actions.
/// </summary>
public interface IMainFormHost
{
    AppConfig Config { get; }
    bool IsRunning { get; }
    bool IsPaused { get; }
    string CurrentTrigger { get; }
    bool WatcherEnabled { get; }
    bool WatcherRunning { get; }
    int WatcherFolderCount { get; }

    event Action<string>? LogReceived;
    event Action<UploadOrchestrator.RunSummary>? RunFinished;
    event Action? StateChanged;

    void RequestRun(bool full, string trigger);
    void TogglePause();
    void CancelRun();
    void ApplySettings(AppConfig updated);
    void SetWatcherEnabled(bool enabled);
    string CommandPreview();
    void OpenLogFolder();
    void OpenStateFile();
}

/// <summary>
/// Primary application window. Surfaces the immich-go settings (via
/// <see cref="SettingsPanel"/>) and mirrors every tray menu action so the user
/// can do the same things from the GUI as from the system tray.
/// </summary>
public sealed class MainForm : Form
{
    private const int MaxLogLines = 2000;

    private readonly IMainFormHost _host;
    private readonly SettingsPanel _settings = new();
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _dashboardTab = new("Dashboard");
    private readonly TabPage _settingsTab = new("Settings");

    private Label _statusValue = null!;
    private Label _triggerValue = null!;
    private Label _watcherValue = null!;
    private Label _lastRunValue = null!;
    private ProgressBar _progress = null!;
    private Button _runNowBtn = null!;
    private Button _runFullBtn = null!;
    private Button _pauseBtn = null!;
    private Button _cancelBtn = null!;
    private CheckBox _watcherCheck = null!;
    private TextBox _previewBox = null!;
    private TextBox _logBox = null!;
    private ToolStripMenuItem _pauseMenuItem = null!;
    private ToolStripMenuItem _watcherMenuItem = null!;

    /// <summary>Set to true by the app when it is really exiting.</summary>
    public bool AllowClose { get; set; }

    public MainForm(IMainFormHost host)
    {
        _host = host;
        BuildUi();
        RefreshFromConfig();

        _host.LogReceived += OnLogReceived;
        _host.RunFinished += OnRunFinished;
        _host.StateChanged += OnStateChanged;
        FormClosing += OnFormClosing;
    }

    // ── Public surface ─────────────────────────────────────────

    /// <summary>Show the window with the Dashboard tab selected.</summary>
    public void ShowDashboard()
    {
        _tabs.SelectedTab = _dashboardTab;
        Reveal();
    }

    /// <summary>Show the window with the Settings tab selected.</summary>
    public void ShowSettings()
    {
        _tabs.SelectedTab = _settingsTab;
        Reveal();
    }

    /// <summary>Reload all settings controls from the live config.</summary>
    public void RefreshFromConfig()
    {
        _settings.LoadFrom(_host.Config);
        UpdateCommandPreview();
        UpdateStatus();
    }

    /// <summary>Append a line to the on-screen activity log.</summary>
    public void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendLog), message);
            return;
        }
        if (string.IsNullOrEmpty(message)) return;

        _logBox.AppendText(message + Environment.NewLine);
        if (_logBox.Lines.Length > MaxLogLines)
        {
            _logBox.Lines = _logBox.Lines.Skip(_logBox.Lines.Length - MaxLogLines).ToArray();
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }
    }

    // ── UI construction ────────────────────────────────────────

    private void BuildUi()
    {
        Text = "Immich Uploader";
        Width = 820;
        Height = 720;
        MinimumSize = new System.Drawing.Size(720, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new System.Drawing.Font("Segoe UI", 9f);

        MainMenuStrip = BuildMenu();

        _tabs.TabPages.Add(_dashboardTab);
        _tabs.TabPages.Add(_settingsTab);
        BuildDashboardTab(_dashboardTab);

        var settingsHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        settingsHost.Controls.Add(_settings);
        _settingsTab.Controls.Add(settingsHost);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8)
        };
        var hideBtn = new Button { Text = "Hide to tray", Width = 110, Height = 30 };
        hideBtn.Click += (_, _) => Hide();
        var applyBtn = new Button { Text = "Save settings", Width = 120, Height = 30 };
        applyBtn.Click += (_, _) => SaveSettings();
        footer.Controls.Add(hideBtn);
        footer.Controls.Add(applyBtn);

        Controls.Add(_tabs);
        Controls.Add(footer);
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(new ToolStripMenuItem("&Save settings", null, (_, _) => SaveSettings()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("Open &log folder", null, (_, _) => _host.OpenLogFolder()));
        file.DropDownItems.Add(new ToolStripMenuItem("Open &state file", null, (_, _) => _host.OpenStateFile()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("&Hide window", null, (_, _) => Hide()));
        file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close()));

        var run = new ToolStripMenuItem("&Run");
        run.DropDownItems.Add(new ToolStripMenuItem("Run &now", null, (_, _) => _host.RequestRun(false, "manual")));
        run.DropDownItems.Add(new ToolStripMenuItem("Run &full rescan now", null, (_, _) => _host.RequestRun(true, "manual-full")));
        run.DropDownItems.Add(new ToolStripSeparator());
        _pauseMenuItem = new ToolStripMenuItem("&Pause", null, (_, _) => _host.TogglePause());
        run.DropDownItems.Add(_pauseMenuItem);
        run.DropDownItems.Add(new ToolStripMenuItem("&Cancel current run", null, (_, _) => _host.CancelRun()));
        run.DropDownItems.Add(new ToolStripSeparator());
        _watcherMenuItem = new ToolStripMenuItem("&Watch folders in real time", null, (_, _) => ToggleWatcher())
        {
            CheckOnClick = false
        };
        run.DropDownItems.Add(_watcherMenuItem);

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(new ToolStripMenuItem("&About", null, (_, _) => ShowAbout()));

        menu.Items.Add(file);
        menu.Items.Add(run);
        menu.Items.Add(help);
        return menu;
    }

    private void BuildDashboardTab(TabPage tab)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));   // status
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));    // controls
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));   // command preview
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // activity log

        layout.Controls.Add(BuildStatusCard(), 0, 0);
        layout.Controls.Add(BuildControlRow(), 0, 1);
        layout.Controls.Add(BuildPreviewCard(), 0, 2);
        layout.Controls.Add(BuildActivityCard(), 0, 3);

        tab.Controls.Add(layout);
    }

    private Control BuildStatusCard()
    {
        var box = new GroupBox { Text = "Status", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

        _statusValue = ValueLabel();
        _triggerValue = ValueLabel();
        _watcherValue = ValueLabel();
        _lastRunValue = ValueLabel();
        _progress = new ProgressBar { Dock = DockStyle.Left, Width = 220, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 0 };

        grid.Controls.Add(Label("Status:"), 0, 0);
        grid.Controls.Add(_statusValue, 1, 0);
        grid.Controls.Add(Label("Trigger:"), 0, 1);
        grid.Controls.Add(_triggerValue, 1, 1);
        grid.Controls.Add(Label("Watcher:"), 0, 2);
        grid.Controls.Add(_watcherValue, 1, 2);
        grid.Controls.Add(Label("Last run:"), 0, 3);
        grid.Controls.Add(_lastRunValue, 1, 3);
        grid.Controls.Add(new Label(), 0, 4);
        grid.Controls.Add(_progress, 1, 4);

        box.Controls.Add(grid);
        return box;
    }

    private Control BuildControlRow()
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4, 8, 4, 4) };

        _runNowBtn = new Button { Text = "Run now", Width = 110, Height = 32 };
        _runNowBtn.Click += (_, _) => _host.RequestRun(false, "manual");

        _runFullBtn = new Button { Text = "Run full rescan", Width = 140, Height = 32 };
        _runFullBtn.Click += (_, _) => _host.RequestRun(true, "manual-full");

        _pauseBtn = new Button { Text = "Pause", Width = 90, Height = 32 };
        _pauseBtn.Click += (_, _) => _host.TogglePause();

        _cancelBtn = new Button { Text = "Cancel", Width = 90, Height = 32 };
        _cancelBtn.Click += (_, _) => _host.CancelRun();

        _watcherCheck = new CheckBox { Text = "Watch folders in real time", AutoSize = true, Margin = new Padding(16, 9, 0, 0) };
        _watcherCheck.CheckedChanged += (_, _) =>
        {
            if (_watcherCheck.Checked != _host.WatcherEnabled) ToggleWatcher();
        };

        var refreshBtn = new Button { Text = "Refresh preview", Width = 130, Height = 32, Margin = new Padding(16, 3, 0, 0) };
        refreshBtn.Click += (_, _) => UpdateCommandPreview();

        row.Controls.AddRange(new Control[] { _runNowBtn, _runFullBtn, _pauseBtn, _cancelBtn, _watcherCheck, refreshBtn });
        return row;
    }

    private Control BuildPreviewCard()
    {
        var box = new GroupBox { Text = "immich-go command preview (secrets omitted)", Dock = DockStyle.Fill, Padding = new Padding(10) };
        _previewBox = new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new System.Drawing.Font("Consolas", 9f),
            BackColor = System.Drawing.SystemColors.Window
        };
        box.Controls.Add(_previewBox);
        return box;
    }

    private Control BuildActivityCard()
    {
        var box = new GroupBox { Text = "Activity", Dock = DockStyle.Fill, Padding = new Padding(10) };
        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Font = new System.Drawing.Font("Consolas", 9f),
            BackColor = System.Drawing.SystemColors.Window
        };
        var clearBtn = new Button { Text = "Clear", Width = 80, Dock = DockStyle.Bottom };
        clearBtn.Click += (_, _) => _logBox.Clear();
        box.Controls.Add(_logBox);
        box.Controls.Add(clearBtn);
        return box;
    }

    // ── Host wiring ────────────────────────────────────────────

    private void OnStateChanged()
    {
        if (InvokeRequired) { BeginInvoke(new Action(OnStateChanged)); return; }
        UpdateStatus();
    }

    private void OnLogReceived(string message)
    {
        AppendLog(message);
    }

    private void OnRunFinished(UploadOrchestrator.RunSummary summary)
    {
        if (InvokeRequired) { BeginInvoke(new Action<UploadOrchestrator.RunSummary>(OnRunFinished), summary); return; }
        var kind = summary.WasTargeted ? "partial" : summary.WasFullRescan ? "full" : "incremental";
        AppendLog($"[ui] Run finished — {summary.Trigger} ({kind}), {summary.SuccessCount} ok / {summary.FailCount} failed.");
        UpdateStatus();
    }

    private void ToggleWatcher()
    {
        var enable = !_host.WatcherEnabled;
        _host.SetWatcherEnabled(enable);
        _watcherCheck.Checked = _host.WatcherEnabled;
        UpdateStatus();
    }

    private void SaveSettings()
    {
        var updated = _host.Config.Clone();
        _settings.ApplyTo(updated);

        if (!_settings.ValidateFolders())
        {
            var choice = MessageBox.Show(
                this,
                $"Some source folders are missing or duplicated.\n\nSave anyway?",
                "Immich Uploader",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (choice != DialogResult.Yes) return;
        }

        _host.ApplySettings(updated);
        RefreshFromConfig();
        AppendLog("[ui] Settings saved.");
    }

    // ── State rendering ────────────────────────────────────────

    private void UpdateStatus()
    {
        if (InvokeRequired) { BeginInvoke(new Action(UpdateStatus)); return; }

        var running = _host.IsRunning;
        var status = running ? (_host.IsPaused ? "Paused" : "Running") : "Idle";
        _statusValue.Text = status;
        _triggerValue.Text = running ? _host.CurrentTrigger : "-";
        _watcherValue.Text = _host.WatcherEnabled
            ? (_host.WatcherRunning ? $"Active ({_host.WatcherFolderCount} folder(s))" : "Enabled (no existing folders)")
            : "Disabled";

        _progress.MarqueeAnimationSpeed = running && !_host.IsPaused ? 30 : 0;

        _runNowBtn.Enabled = !running;
        _runFullBtn.Enabled = !running;
        _pauseBtn.Enabled = running;
        _pauseBtn.Text = _host.IsPaused ? "Resume" : "Pause";
        _cancelBtn.Enabled = running;
        _pauseMenuItem.Text = _host.IsPaused ? "&Resume" : "&Pause";
        _watcherMenuItem.Checked = _host.WatcherEnabled;
        _watcherCheck.Checked = _host.WatcherEnabled;
    }

    private void UpdateCommandPreview()
    {
        try
        {
            _previewBox.Text = _host.CommandPreview();
        }
        catch (Exception ex)
        {
            _previewBox.Text = "Could not build preview: " + ex.Message;
        }
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            this,
            "Immich Uploader\nAutomated immich-go uploads from the tray.\n\n" +
            "Run controls, immich-go options, scheduling, and watching are available here and from the tray menu.",
            "About Immich Uploader",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void Reveal()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // Closing the window hides it to the tray unless the app is exiting.
        if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private static Label Label(string text)
        => new() { Text = text, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = false, Dock = DockStyle.Fill };

    private static Label ValueLabel()
        => new() { Text = "-", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = false, Dock = DockStyle.Fill };
}
