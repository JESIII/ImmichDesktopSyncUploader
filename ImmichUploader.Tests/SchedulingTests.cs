using ImmichUploader;
using Xunit;

namespace ImmichUploader.Tests;

public class SchedulingTests
{
    [Fact]
    public void NextWeeklyAfter_ReturnsUpcomingOccurrence()
    {
        var from = new DateTime(2026, 1, 7, 12, 0, 0); // Wednesday

        var next = Scheduler.NextWeeklyAfter(from, DayOfWeek.Sunday, 3, 0);

        Assert.Equal(new DateTime(2026, 1, 11, 3, 0, 0), next);
    }

    [Fact]
    public void NextWeeklyAfter_SameDayLaterTimeUsesToday()
    {
        var from = new DateTime(2026, 1, 7, 1, 0, 0); // Wednesday

        var next = Scheduler.NextWeeklyAfter(from, DayOfWeek.Wednesday, 3, 0);

        Assert.Equal(new DateTime(2026, 1, 7, 3, 0, 0), next);
    }

    [Fact]
    public void PreviousWeekly_ReturnsMostRecentOccurrence()
    {
        var from = new DateTime(2026, 1, 7, 12, 0, 0); // Wednesday

        var previous = Scheduler.PreviousWeekly(from, DayOfWeek.Sunday, 3, 0);

        Assert.Equal(new DateTime(2026, 1, 4, 3, 0, 0), previous);
    }

    [Fact]
    public void NextMonthlyAfter_RollsToNextMonth()
    {
        var from = new DateTime(2026, 1, 20, 0, 0, 0);

        var next = Scheduler.NextMonthlyAfter(from, 1, 4, 0);

        Assert.Equal(new DateTime(2026, 2, 1, 4, 0, 0), next);
    }

    [Fact]
    public void PreviousMonthly_ReturnsCurrentMonthOccurrence()
    {
        var from = new DateTime(2026, 1, 20, 0, 0, 0);

        var previous = Scheduler.PreviousMonthly(from, 1, 4, 0);

        Assert.Equal(new DateTime(2026, 1, 1, 4, 0, 0), previous);
    }

    [Fact]
    public void IsDue_ComparesAgainstHandledMarker()
    {
        var occurrence = new DateTime(2026, 1, 4, 3, 0, 0);

        Assert.True(Scheduler.IsDue(occurrence, null));
        Assert.True(Scheduler.IsDue(occurrence, occurrence.AddDays(-7)));
        Assert.False(Scheduler.IsDue(occurrence, occurrence));
        Assert.False(Scheduler.IsDue(occurrence, occurrence.AddDays(1)));
    }

    [Fact]
    public void WeeklyDue_RespectsDisabledToggle()
    {
        var cfg = new AppConfig { WeeklyEnabled = false, WeeklyDay = DayOfWeek.Wednesday, WeeklyHour = 3 };
        var now = new DateTime(2026, 1, 7, 12, 0, 0);

        Assert.False(Scheduler.WeeklyDue(cfg, now, null));

        cfg.WeeklyEnabled = true;
        Assert.True(Scheduler.WeeklyDue(cfg, now, null));
    }

    [Fact]
    public void MonthlyDue_RespectsDisabledToggle()
    {
        var cfg = new AppConfig { MonthlyEnabled = false, MonthlyRescanDay = 1, MonthlyRescanHour = 4 };
        var now = new DateTime(2026, 1, 20, 0, 0, 0);

        Assert.False(Scheduler.MonthlyDue(cfg, now, null));

        cfg.MonthlyEnabled = true;
        Assert.True(Scheduler.MonthlyDue(cfg, now, null));
    }

    [Fact]
    public void HandledOccurrenceDoesNotRefire()
    {
        var cfg = new AppConfig { WeeklyEnabled = true, WeeklyDay = DayOfWeek.Wednesday, WeeklyHour = 3 };
        var now = new DateTime(2026, 1, 7, 12, 0, 0);
        var handled = Scheduler.PreviousWeekly(now, cfg.WeeklyDay, cfg.WeeklyHour, cfg.WeeklyMinute);

        Assert.False(Scheduler.WeeklyDue(cfg, now, handled));
    }

    [Fact]
    public void EnabledByDefaultForBothSchedules()
    {
        var cfg = new AppConfig();
        Assert.True(cfg.WeeklyEnabled);
        Assert.True(cfg.MonthlyEnabled);
    }
}
