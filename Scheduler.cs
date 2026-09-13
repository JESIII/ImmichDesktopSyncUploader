namespace ImmichUploader;

/// <summary>
/// Fires the optional weekly (partial) and monthly (full) scans.
///
/// Uses the "most recent past occurrence + persisted handled marker" pattern so
/// each schedule occurrence fires exactly once, even across restarts. Markers
/// are baselined on startup, so a fresh install (or a restart) does not fire an
/// immediate catch-up run.
/// </summary>
public sealed class Scheduler
{
    private readonly AppConfig _cfg;
    private readonly UploadOrchestrator _orchestrator;
    private readonly Func<UploadState> _loadState;
    private readonly Action<UploadState> _saveState;
    private System.Windows.Forms.Timer? _timer;
    private DateTime? _weeklyHandled;
    private DateTime? _monthlyHandled;

    public Scheduler(
        AppConfig cfg,
        UploadOrchestrator orchestrator,
        Func<UploadState>? loadState = null,
        Action<UploadState>? saveState = null)
    {
        _cfg = cfg;
        _orchestrator = orchestrator;
        _loadState = loadState ?? StateStore.Load;
        _saveState = saveState ?? StateStore.Save;
    }

    public void Start()
    {
        var state = _loadState();
        var now = DateTime.Now;

        // Baseline handled markers so we never fire a run merely because the app
        // started after a scheduled time had already passed.
        _weeklyHandled = state.LastWeeklyHandled ?? PreviousWeekly(now, _cfg.WeeklyDay, _cfg.WeeklyHour, _cfg.WeeklyMinute);
        _monthlyHandled = state.LastMonthlyHandled ?? PreviousMonthly(now, _cfg.MonthlyRescanDay, _cfg.MonthlyRescanHour, _cfg.MonthlyRescanMinute);

        state.LastWeeklyHandled = _weeklyHandled;
        state.LastMonthlyHandled = _monthlyHandled;
        _saveState(state);

        _timer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Called after settings change; the scheduler reads the live config next tick.</summary>
    public void OnConfigChanged()
    {
        // Values are read live from _cfg on each tick; nothing to recompute.
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_orchestrator.IsRunning) return;
        var now = DateTime.Now;

        if (WeeklyDue(_cfg, now, _weeklyHandled))
        {
            _weeklyHandled = PreviousWeekly(now, _cfg.WeeklyDay, _cfg.WeeklyHour, _cfg.WeeklyMinute);
            PersistMarkers();
            _orchestrator.RequestRun(_cfg, new UploadOrchestrator.RunOptions
            {
                FullRescan = false,
                Trigger = "weekly"
            });
            return;
        }

        if (MonthlyDue(_cfg, now, _monthlyHandled))
        {
            _monthlyHandled = PreviousMonthly(now, _cfg.MonthlyRescanDay, _cfg.MonthlyRescanHour, _cfg.MonthlyRescanMinute);
            PersistMarkers();
            _orchestrator.RequestRun(_cfg, new UploadOrchestrator.RunOptions
            {
                FullRescan = true,
                Trigger = "monthly-rescan"
            });
        }
    }

    /// <summary>True when the weekly scan is enabled and its latest occurrence is unhandled.</summary>
    public static bool WeeklyDue(AppConfig cfg, DateTime now, DateTime? handled)
    {
        return cfg.WeeklyEnabled
            && IsDue(PreviousWeekly(now, cfg.WeeklyDay, cfg.WeeklyHour, cfg.WeeklyMinute), handled);
    }

    /// <summary>True when the monthly full rescan is enabled and its latest occurrence is unhandled.</summary>
    public static bool MonthlyDue(AppConfig cfg, DateTime now, DateTime? handled)
    {
        return cfg.MonthlyEnabled
            && IsDue(PreviousMonthly(now, cfg.MonthlyRescanDay, cfg.MonthlyRescanHour, cfg.MonthlyRescanMinute), handled);
    }

    private void PersistMarkers()
    {
        var state = _loadState();
        state.LastWeeklyHandled = _weeklyHandled;
        state.LastMonthlyHandled = _monthlyHandled;
        _saveState(state);
    }

    /// <summary>True when an occurrence newer than the handled marker should fire.</summary>
    public static bool IsDue(DateTime occurrence, DateTime? handled)
    {
        return handled is null || occurrence > handled.Value;
    }

    public static DateTime NextWeeklyAfter(DateTime from, DayOfWeek day, int hour, int minute)
    {
        int diff = ((int)day - (int)from.DayOfWeek + 7) % 7;
        var candidate = new DateTime(from.Year, from.Month, from.Day, hour, minute, 0);
        candidate = candidate.AddDays(diff);
        if (candidate <= from) candidate = candidate.AddDays(7);
        return candidate;
    }

    public static DateTime NextMonthlyAfter(DateTime from, int day, int hour, int minute)
    {
        var candidate = new DateTime(from.Year, from.Month, Math.Min(day, DateTime.DaysInMonth(from.Year, from.Month)), hour, minute, 0);
        if (candidate <= from)
        {
            var nextMonth = from.AddMonths(1);
            candidate = new DateTime(nextMonth.Year, nextMonth.Month, Math.Min(day, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month)), hour, minute, 0);
        }
        return candidate;
    }

    public static DateTime PreviousWeekly(DateTime from, DayOfWeek day, int hour, int minute)
    {
        int diff = ((int)from.DayOfWeek - (int)day + 7) % 7;
        var candidate = new DateTime(from.Year, from.Month, from.Day, hour, minute, 0).AddDays(-diff);
        if (candidate > from) candidate = candidate.AddDays(-7);
        return candidate;
    }

    public static DateTime PreviousMonthly(DateTime from, int day, int hour, int minute)
    {
        day = Math.Clamp(day, 1, 28);
        var candidate = new DateTime(
            from.Year, from.Month,
            Math.Min(day, DateTime.DaysInMonth(from.Year, from.Month)),
            hour, minute, 0);
        if (candidate > from)
        {
            var prev = from.AddMonths(-1);
            candidate = new DateTime(
                prev.Year, prev.Month,
                Math.Min(day, DateTime.DaysInMonth(prev.Year, prev.Month)),
                hour, minute, 0);
        }
        return candidate;
    }
}
