using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

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

public sealed class TrayAppContext : ApplicationContext
{
    private readonly SynchronizationContext _uiContext;
    private readonly NotifyIcon _tray;
    private readonly AppConfig _cfg;
    private readonly UploadOrchestrator _orchestrator;
    private readonly Scheduler _scheduler;
    private readonly ToolStripMenuItem _runNowItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly Icon _iconIdle;
    private readonly Icon _iconRunning;
    private readonly Icon _iconPaused;
    private readonly Icon _iconError;
    private readonly System.Windows.Forms.Timer _iconTimer;
    private bool _blinkOn;
    private string _lastStatusText = "Idle";

    public TrayAppContext(bool startMinimized)
    {
        _uiContext = WindowsFormsSynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _cfg = ConfigStore.Load();
        if (string.IsNullOrWhiteSpace(_cfg.ApiKey))
        {
            _cfg.ApiKey = Environment.GetEnvironmentVariable("IMMICH_API_KEY") ?? "";
        }
        ConfigStore.Save(_cfg);

        _iconIdle = TrayIcons.CreateIdle();
        _iconRunning = TrayIcons.CreateRunning();
        _iconPaused = TrayIcons.CreatePaused();
        _iconError = TrayIcons.CreateError();

        _orchestrator = new UploadOrchestrator();
        _orchestrator.Log += OnLog;
        _orchestrator.RunCompleted += OnRunCompleted;
        _orchestrator.FolderCompleted += OnFolderCompleted;

        _scheduler = new Scheduler(_cfg, _orchestrator);
        _scheduler.Start();

        var menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem("Status: Idle") { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());

        _runNowItem = new ToolStripMenuItem("Run now", null, (_, _) =>
        {
            if (_orchestrator.IsRunning) return;
            _orchestrator.Start(_cfg, new UploadOrchestrator.RunOptions { ForceFullRescan = false, Trigger = "manual" });
        });
        menu.Items.Add(_runNowItem);

        var runFullItem = new ToolStripMenuItem("Run full rescan now", null, (_, _) =>
        {
            if (_orchestrator.IsRunning) return;
            _orchestrator.Start(_cfg, new UploadOrchestrator.RunOptions { ForceFullRescan = true, Trigger = "manual-full" });
        });
        menu.Items.Add(runFullItem);

        _pauseItem = new ToolStripMenuItem("Pause", null, (_, _) =>
        {
            if (!_orchestrator.IsRunning) return;
            _orchestrator.TogglePause();
        });
        menu.Items.Add(_pauseItem);

        var cancelItem = new ToolStripMenuItem("Cancel current run", null, (_, _) => _orchestrator.Cancel());
        menu.Items.Add(cancelItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Settings...", null, (_, _) => OpenSettings()));
        menu.Items.Add(new ToolStripMenuItem("Open log folder", null, (_, _) => OpenLogFolder()));
        menu.Items.Add(new ToolStripMenuItem("Open state file", null, (_, _) => OpenInDefaultApp(StateStore.ResolvePath())));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => ExitThread()));

        _tray = new NotifyIcon
        {
            Icon = _iconIdle,
            Text = "Immich Uploader",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => OpenSettings();

        _iconTimer = new System.Windows.Forms.Timer { Interval = 700 };
        _iconTimer.Tick += (_, _) =>
        {
            _blinkOn = !_blinkOn;
            UpdateTrayIcon();
        };

        if (!startMinimized)
        {
            _tray.ShowBalloonTip(4000, "Immich Uploader", "Running in the system tray.", ToolTipIcon.Info);
        }
    }

    private void OnLog(string msg)
    {
        _uiContext.Post(_ =>
        {
            UpdateStatusFromLog(msg);
            UpdateTrayIcon();
        }, null);
    }

    private void UpdateStatusFromLog(string msg)
    {
        if (msg.StartsWith("[start]")) _lastStatusText = "Running";
        else if (msg.StartsWith("[pause]")) _lastStatusText = msg.Contains("Paused") ? "Paused" : "Running";
        else if (msg.StartsWith("[done]")) _lastStatusText = "Idle";
        else if (msg.StartsWith("[cancel]")) _lastStatusText = "Cancelling";
        else if (msg.StartsWith("[error]")) _lastStatusText = "Error";
        _statusItem.Text = $"Status: {_lastStatusText}";
        _tray.Text = $"Immich Uploader - {_lastStatusText}";
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
            var title = summary.FailCount == 0 ? "Run succeeded" : "Run finished with errors";
            var body = $"{summary.Trigger}: {summary.SuccessCount} ok / {summary.FailCount} failed. Log: {summary.LogFile}";
            _tray.ShowBalloonTip(6000, title, body, icon);
        }, null);
    }

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
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_cfg);
        if (form.ShowDialog() == DialogResult.OK)
        {
            var updated = form.UpdatedConfig;
            _cfg.Server = updated.Server;
            _cfg.ApiKey = updated.ApiKey;
            _cfg.Folders = updated.Folders;
            _cfg.Concurrency = updated.Concurrency;
            _cfg.DaysBack = updated.DaysBack;
            _cfg.LogDir = updated.LogDir;
            _cfg.ImmichGoPath = updated.ImmichGoPath;
            _cfg.LaunchOnWindowsStartup = updated.LaunchOnWindowsStartup;
            _cfg.StartMinimizedToTray = updated.StartMinimizedToTray;
            _cfg.WeeklyDay = updated.WeeklyDay;
            _cfg.WeeklyHour = updated.WeeklyHour;
            _cfg.WeeklyMinute = updated.WeeklyMinute;
            _cfg.MonthlyRescanDay = updated.MonthlyRescanDay;
            _cfg.MonthlyRescanHour = updated.MonthlyRescanHour;
            _cfg.MonthlyRescanMinute = updated.MonthlyRescanMinute;
            ConfigStore.Save(_cfg);
            _scheduler.OnConfigChanged();
            _tray.ShowBalloonTip(3000, "Immich Uploader", "Settings saved.", ToolTipIcon.Info);
        }
    }

    private void OpenLogFolder()
    {
        var dir = _cfg.DefaultLogDir;
        Directory.CreateDirectory(dir);
        OpenInDefaultApp(dir);
    }

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

    protected override void ExitThreadCore()
    {
        try { _scheduler.Stop(); } catch { }
        try { _orchestrator.Dispose(); } catch { }
        try { _tray.Visible = false; } catch { }
        try { _iconIdle.Dispose(); } catch { }
        try { _iconRunning.Dispose(); } catch { }
        try { _iconPaused.Dispose(); } catch { }
        try { _iconError.Dispose(); } catch { }
        base.ExitThreadCore();
    }
}
