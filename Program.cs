using System.Diagnostics;
using System.Drawing;
using System.Text;

namespace ImmichUploader;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var startMinimized = args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.Run(new TrayAppContext(startMinimized));
        return 0;
    }
}

/// <summary>
/// Owns the tray icon and the main window. Implements <see cref="IMainFormHost"/>
/// so the window and the tray menu expose exactly the same actions.
/// </summary>
public sealed class TrayAppContext : ApplicationContext, IMainFormHost
{
    private readonly SynchronizationContext _uiContext;
    private readonly NotifyIcon _tray;
    private readonly AppConfig _cfg;
    private readonly UploadOrchestrator _orchestrator;
    private readonly FolderWatcher _watcher;
    private readonly Scheduler _scheduler;
    private readonly MainForm _mainForm;

    private readonly ToolStripMenuItem _runNowItem;
    private readonly ToolStripMenuItem _runFullItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _cancelItem;
    private readonly ToolStripMenuItem _watcherItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _watcherStatusItem;

    private readonly Icon _iconIdle;
    private readonly Icon _iconRunning;
    private readonly Icon _iconPaused;
    private readonly Icon _iconError;
    private readonly System.Windows.Forms.Timer _iconTimer;
    private bool _blinkOn;
    private string _lastStatusText = "Idle";

    public event Action<string>? LogReceived;
    public event Action<UploadOrchestrator.RunSummary>? RunFinished;
    public event Action? StateChanged;

    public TrayAppContext(bool startMinimized)
    {
        _uiContext = WindowsFormsSynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _cfg = ConfigStore.Load();
        if (string.IsNullOrWhiteSpace(_cfg.ApiKey))
            _cfg.ApiKey = Environment.GetEnvironmentVariable("IMMICH_API_KEY") ?? "";
        if (string.IsNullOrWhiteSpace(_cfg.AdminApiKey))
            _cfg.AdminApiKey = Environment.GetEnvironmentVariable("IMMICH_ADMIN_API_KEY") ?? "";
        _cfg.Normalize();
        ConfigStore.Save(_cfg);

        _iconIdle = TrayIcons.CreateIdle();
        _iconRunning = TrayIcons.CreateRunning();
        _iconPaused = TrayIcons.CreatePaused();
        _iconError = TrayIcons.CreateError();

        _orchestrator = new UploadOrchestrator();
        _orchestrator.Log += OnLog;
        _orchestrator.RunCompleted += OnRunCompleted;
        _orchestrator.FolderCompleted += OnFolderCompleted;

        _watcher = new FolderWatcher(_cfg);
        _watcher.Log += OnLog;
        _watcher.BatchReady += OnWatcherBatch;

        _scheduler = new Scheduler(_cfg, _orchestrator);
        _scheduler.Start();

        // The main window provides the full GUI (settings + run controls).
        _mainForm = new MainForm(this);

        var menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem("Status: Idle") { Enabled = false };
        _watcherStatusItem = new ToolStripMenuItem("Watcher: starting...") { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(_watcherStatusItem);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem("Open dashboard", null, (_, _) => _mainForm.ShowDashboard()));
        menu.Items.Add(new ToolStripMenuItem("Settings...", null, (_, _) => _mainForm.ShowSettings()));
        menu.Items.Add(new ToolStripSeparator());

        _runNowItem = new ToolStripMenuItem("Run now", null, (_, _) => RequestRun(full: false, trigger: "manual"));
        menu.Items.Add(_runNowItem);

        _runFullItem = new ToolStripMenuItem("Run full rescan now", null, (_, _) => RequestRun(full: true, trigger: "manual-full"));
        menu.Items.Add(_runFullItem);

        _pauseItem = new ToolStripMenuItem("Pause", null, (_, _) =>
        {
            if (!_orchestrator.IsRunning) return;
            _orchestrator.TogglePause();
        });
        menu.Items.Add(_pauseItem);

        _cancelItem = new ToolStripMenuItem("Cancel current run", null, (_, _) => _orchestrator.Cancel());
        menu.Items.Add(_cancelItem);

        _watcherItem = new ToolStripMenuItem("Watch folders in real time", null, (_, _) => SetWatcherEnabled(!_cfg.WatcherEnabled))
        {
            CheckOnClick = false,
            Checked = _cfg.WatcherEnabled
        };
        menu.Items.Add(_watcherItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open log folder", null, (_, _) => OpenLogFolder()));
        menu.Items.Add(new ToolStripMenuItem("Open state file", null, (_, _) => OpenStateFile()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => ExitThread()));

        _tray = new NotifyIcon
        {
            Icon = _iconIdle,
            Text = "Immich Uploader",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => _mainForm.ShowDashboard();

        _iconTimer = new System.Windows.Forms.Timer { Interval = 700 };
        _iconTimer.Tick += (_, _) =>
        {
            _blinkOn = !_blinkOn;
            UpdateTrayIcon();
        };

        // Start the watcher against the saved config (no-op when disabled).
        _watcher.Refresh(_cfg);
        UpdateWatcherStatus();
        _mainForm.RefreshFromConfig();

        if (startMinimized)
        {
            _tray.ShowBalloonTip(4000, "Immich Uploader", "Running in the system tray. Double-click to open the dashboard.", ToolTipIcon.Info);
        }
        else
        {
            _mainForm.ShowDashboard();
        }
    }

    // ── IMainFormHost ──────────────────────────────────────────

    AppConfig IMainFormHost.Config => _cfg;
    bool IMainFormHost.IsRunning => _orchestrator.IsRunning;
    bool IMainFormHost.IsPaused => _orchestrator.IsPaused;
    string IMainFormHost.CurrentTrigger => _orchestrator.CurrentTrigger;
    bool IMainFormHost.WatcherEnabled => _cfg.WatcherEnabled;
    bool IMainFormHost.WatcherRunning => _watcher.IsRunning;
    int IMainFormHost.WatcherFolderCount => _watcher.WatchedFolderCount;

    void IMainFormHost.RequestRun(bool full, string trigger) => RequestRun(full, trigger);

    void IMainFormHost.TogglePause() => _orchestrator.TogglePause();

    void IMainFormHost.CancelRun() => _orchestrator.Cancel();

    void IMainFormHost.SetWatcherEnabled(bool enabled) => SetWatcherEnabled(enabled);

    void IMainFormHost.OpenLogFolder() => OpenLogFolder();

    void IMainFormHost.OpenStateFile() => OpenStateFile();

    void IMainFormHost.ApplySettings(AppConfig updated)
    {
        ApplyConfig(updated);
        ConfigStore.Save(_cfg);
        _scheduler.OnConfigChanged();
        _watcher.Refresh(_cfg);
        _watcherItem.Checked = _cfg.WatcherEnabled;
        UpdateWatcherStatus();
        RaiseStateChanged();
    }

    string IMainFormHost.CommandPreview() => BuildCommandPreview();

    // ── Run helpers ────────────────────────────────────────────

    private void RequestRun(bool full, string trigger)
    {
        // Coalescing lives in the orchestrator; a request while busy is merged.
        _orchestrator.RequestRun(_cfg, new UploadOrchestrator.RunOptions
        {
            FullRescan = full,
            Trigger = trigger
        });
    }

    private void SetWatcherEnabled(bool enabled)
    {
        _cfg.WatcherEnabled = enabled;
        ConfigStore.Save(_cfg);
        _watcherItem.Checked = enabled;
        _watcher.Refresh(_cfg);
        UpdateWatcherStatus();
        RaiseStateChanged();
        _tray.ShowBalloonTip(3000, "Immich Uploader",
            enabled ? "Real-time watching enabled." : "Real-time watching disabled.",
            ToolTipIcon.Info);
    }

    private string BuildCommandPreview()
    {
        var folder = _cfg.Folders.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(folder)) folder = @"<source folder>";

        var since = DateTime.UtcNow.AddDays(-Math.Max(1, _cfg.DaysBack));
        var invocation = ImmichGoRunner.BuildInvocation(_cfg, folder, since);

        var sb = new StringBuilder();
        sb.AppendLine(invocation.DisplayCommand);
        foreach (var warning in invocation.Warnings)
        {
            sb.AppendLine();
            sb.AppendLine("WARNING: " + warning);
        }
        sb.AppendLine();
        sb.AppendLine("Secrets are injected via environment variables (never argv):");
        sb.AppendLine(
            string.IsNullOrWhiteSpace(_cfg.ApiKey)
                ? "  IMMICH_GO_UPLOAD_API_KEY = (not set)"
                : "  IMMICH_GO_UPLOAD_API_KEY = <hidden>");
        sb.AppendLine(
            string.IsNullOrWhiteSpace(_cfg.AdminApiKey)
                ? "  IMMICH_GO_UPLOAD_ADMIN_API_KEY = (not set)"
                : "  IMMICH_GO_UPLOAD_ADMIN_API_KEY = <hidden>");
        return sb.ToString();
    }

    private void OnWatcherBatch(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return;

        var folders = files
            .Select(f => PathFilters.FindOwningFolder(_cfg.Folders, f))
            .Where(f => f is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (folders.Count == 0) return;

        // Include the changed files' write times so immich-go's date range sees them.
        var earliest = files
            .Select(f =>
            {
                try { return File.GetLastWriteTimeUtc(f); }
                catch { return DateTime.UtcNow; }
            })
            .DefaultIfEmpty(DateTime.UtcNow)
            .Min();

        OnLog($"[watch] {files.Count} changed file(s) in {folders.Count} folder(s)");
        _orchestrator.RequestRun(_cfg, new UploadOrchestrator.RunOptions
        {
            FullRescan = false,
            Trigger = "watcher",
            TargetFolders = folders,
            SinceOverrideUtc = earliest.AddSeconds(-1)
        });
    }

    // ── Status plumbing ────────────────────────────────────────

    private void OnLog(string msg)
    {
        _uiContext.Post(_ =>
        {
            UpdateStatusFromLog(msg);
            UpdateTrayIcon();
            LogReceived?.Invoke(msg);
        }, null);
    }

    private void UpdateStatusFromLog(string msg)
    {
        if (msg.StartsWith("[start]")) _lastStatusText = "Running";
        else if (msg.StartsWith("[pause]")) _lastStatusText = msg.Contains("Paused") ? "Paused" : "Running";
        else if (msg.StartsWith("[queue]")) _lastStatusText = "Running (queued)";
        else if (msg.StartsWith("[done]")) _lastStatusText = "Idle";
        else if (msg.StartsWith("[cancel]")) _lastStatusText = "Cancelling";
        else if (msg.StartsWith("[error]")) _lastStatusText = "Error";
        _statusItem.Text = $"Status: {_lastStatusText}";
        _tray.Text = Truncate($"Immich Uploader - {_lastStatusText}", 63);
        RaiseStateChanged();
    }

    private void UpdateWatcherStatus()
    {
        _watcherStatusItem.Text = _cfg.WatcherEnabled
            ? (_watcher.IsRunning ? $"Watcher: active ({_watcher.WatchedFolderCount} folder(s))" : "Watcher: inactive (no existing folders)")
            : "Watcher: disabled";
    }

    private void OnFolderCompleted(UploadResult result)
    {
        _uiContext.Post(_ =>
        {
            if (!result.Success)
            {
                _tray.ShowBalloonTip(5000, "Immich Uploader - Folder failed", result.Message, ToolTipIcon.Warning);
            }
        }, null);
    }

    private void OnRunCompleted(UploadOrchestrator.RunSummary summary)
    {
        _uiContext.Post(_ =>
        {
            _iconTimer.Stop();
            UpdateTrayIcon();
            var icon = summary.FailCount == 0 ? ToolTipIcon.Info : ToolTipIcon.Warning;
            var title = summary.Cancelled
                ? "Run cancelled"
                : summary.FailCount == 0 ? "Run succeeded" : "Run finished with errors";
            var kind = summary.WasTargeted ? "partial" : summary.WasFullRescan ? "full" : "incremental";
            var body = $"{summary.Trigger} ({kind}): {summary.SuccessCount} ok / {summary.FailCount} failed. Log: {summary.LogFile}";
            _tray.ShowBalloonTip(6000, title, body, icon);
            RunFinished?.Invoke(summary);
            RaiseStateChanged();
        }, null);
    }

    private void RaiseStateChanged() => StateChanged?.Invoke();

    private void UpdateTrayIcon()
    {
        if (_orchestrator.IsRunning)
        {
            if (_orchestrator.IsPaused)
            {
                _tray.Icon = _iconPaused;
            }
            else
            {
                _tray.Icon = _blinkOn ? _iconRunning : _iconIdle;
                if (!_iconTimer.Enabled) _iconTimer.Start();
            }
        }
        else
        {
            _iconTimer.Stop();
            _tray.Icon = _iconIdle;
        }
        _pauseItem.Text = _orchestrator.IsPaused ? "Resume" : "Pause";
    }

    // ── Settings ───────────────────────────────────────────────

    private void ApplyConfig(AppConfig updated)
    {
        _cfg.Server = updated.Server;
        _cfg.ApiKey = updated.ApiKey;
        _cfg.AdminApiKey = updated.AdminApiKey;
        _cfg.Folders = updated.Folders;
        _cfg.Concurrency = updated.Concurrency;
        _cfg.MaxParallelProcesses = updated.MaxParallelProcesses;
        _cfg.DaysBack = updated.DaysBack;

        _cfg.FolderAsAlbum = updated.FolderAsAlbum;
        _cfg.IntoAlbum = updated.IntoAlbum;
        _cfg.ManageBurst = updated.ManageBurst;
        _cfg.ManageRawJpeg = updated.ManageRawJpeg;
        _cfg.ManageHeicJpeg = updated.ManageHeicJpeg;

        _cfg.Recursive = updated.Recursive;
        _cfg.DateFromName = updated.DateFromName;
        _cfg.IgnoreSidecarFiles = updated.IgnoreSidecarFiles;
        _cfg.ManageEpsonFastFoto = updated.ManageEpsonFastFoto;
        _cfg.FolderAsTags = updated.FolderAsTags;
        _cfg.SessionTag = updated.SessionTag;
        _cfg.ApiTrace = updated.ApiTrace;
        _cfg.SkipSslVerify = updated.SkipSslVerify;
        _cfg.Overwrite = updated.Overwrite;
        _cfg.DryRun = updated.DryRun;
        _cfg.PauseImmichJobs = updated.PauseImmichJobs;
        _cfg.DeviceUuid = updated.DeviceUuid;
        _cfg.TimeZone = updated.TimeZone;
        _cfg.AlbumPathJoiner = updated.AlbumPathJoiner;
        _cfg.OnErrors = updated.OnErrors;
        _cfg.ClientTimeoutMinutes = updated.ClientTimeoutMinutes;
        _cfg.IncludeExtensions = updated.IncludeExtensions;
        _cfg.ExcludeExtensions = updated.ExcludeExtensions;
        _cfg.BanFiles = updated.BanFiles;
        _cfg.Tags = updated.Tags;
        _cfg.IncludeType = updated.IncludeType;
        _cfg.LogLevel = updated.LogLevel;

        _cfg.LogDir = updated.LogDir;
        _cfg.ImmichGoPath = updated.ImmichGoPath;
        _cfg.LaunchOnWindowsStartup = updated.LaunchOnWindowsStartup;
        _cfg.StartMinimizedToTray = updated.StartMinimizedToTray;
        _cfg.WeeklyEnabled = updated.WeeklyEnabled;
        _cfg.WeeklyDay = updated.WeeklyDay;
        _cfg.WeeklyHour = updated.WeeklyHour;
        _cfg.WeeklyMinute = updated.WeeklyMinute;
        _cfg.MonthlyEnabled = updated.MonthlyEnabled;
        _cfg.MonthlyRescanDay = updated.MonthlyRescanDay;
        _cfg.MonthlyRescanHour = updated.MonthlyRescanHour;
        _cfg.MonthlyRescanMinute = updated.MonthlyRescanMinute;
        _cfg.WatcherEnabled = updated.WatcherEnabled;
        _cfg.WatcherDebounceSeconds = updated.WatcherDebounceSeconds;
        _cfg.Normalize();
    }

    private void OpenLogFolder()
    {
        var dir = _cfg.DefaultLogDir;
        Directory.CreateDirectory(dir);
        OpenInDefaultApp(dir);
    }

    private void OpenStateFile() => OpenInDefaultApp(StateStore.ResolvePath());

    private static void OpenInDefaultApp(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open {path}: {ex.Message}", "Immich Uploader", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value.Substring(0, max);

    protected override void ExitThreadCore()
    {
        try { _scheduler.Stop(); } catch { }
        try { _watcher.Dispose(); } catch { }
        try { _orchestrator.Dispose(); } catch { }
        try
        {
            _mainForm.AllowClose = true;
            _mainForm.Close();
            _mainForm.Dispose();
        }
        catch { }
        try { _tray.Visible = false; } catch { }
        try { _iconIdle.Dispose(); } catch { }
        try { _iconRunning.Dispose(); } catch { }
        try { _iconPaused.Dispose(); } catch { }
        try { _iconError.Dispose(); } catch { }
        base.ExitThreadCore();
    }
}
