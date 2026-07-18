namespace ImmichUploader;

public sealed class Scheduler
{
    private readonly AppConfig _cfg;
    private readonly UploadOrchestrator _orchestrator;
    private DateTime _nextWeekly;
    private DateTime _nextMonthly;
    private System.Windows.Forms.Timer? _timer;

    public Scheduler(AppConfig cfg, UploadOrchestrator orchestrator)
    {
        _cfg = cfg;
        _orchestrator = orchestrator;
    }

    public void Start()
    {
        RecomputeNextRuns(DateTime.Now);
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

    public void OnConfigChanged()
    {
        RecomputeNextRuns(DateTime.Now);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        if (!_orchestrator.IsRunning)
        {
            if (now >= _nextWeekly)
            {
                _orchestrator.Start(_cfg, new UploadOrchestrator.RunOptions { ForceFullRescan = false, Trigger = "weekly" });
                RecomputeNextRuns(now.AddMinutes(1));
                return;
            }
            if (now >= _nextMonthly)
            {
                _orchestrator.Start(_cfg, new UploadOrchestrator.RunOptions { ForceFullRescan = true, Trigger = "monthly-rescan" });
                RecomputeNextRuns(now.AddMinutes(1));
            }
        }
    }

    private void RecomputeNextRuns(DateTime fromLocal)
    {
        _nextWeekly = NextWeeklyAfter(fromLocal, (DayOfWeek)_cfg.WeeklyDay, _cfg.WeeklyHour, _cfg.WeeklyMinute);
        _nextMonthly = NextMonthlyAfter(fromLocal, Math.Clamp(_cfg.MonthlyRescanDay, 1, 28), _cfg.MonthlyRescanHour, _cfg.MonthlyRescanMinute);
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
}
