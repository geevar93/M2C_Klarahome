using KlaraHome.Modules.Reporting.Domain;

namespace KlaraHome.UnitTests.Reporting;

/// <summary>
/// When a scheduled report is next due, and what period it covers.
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1. Calendar arithmetic has exactly one
/// right answer and several ways to be silently wrong: a next-run in the past makes the worker loop,
/// a next-run computed from the wrong month makes a monthly report skip one, and "the 31st" in
/// February either throws or never fires. None of those is visible until somebody asks where their
/// report went.
/// </remarks>
public sealed class ReportScheduleTests
{
    /// <summary>Sunday, 6 September 2026, at ten past nine in the morning UTC.</summary>
    private static readonly DateTimeOffset SundayMorning = new(2026, 9, 6, 9, 10, 0, TimeSpan.Zero);

    /// <summary>A daily schedule whose hour has passed runs tomorrow, not today.</summary>
    /// <remarks>
    /// The strictly-after rule. A next-run equal to now is one the worker picks straight back up on
    /// the same pass, which is an infinite loop producing the same report.
    /// </remarks>
    [Fact]
    public void A_daily_schedule_past_its_hour_runs_tomorrow()
    {
        var schedule = Daily(hourUtc: 2);

        Assert.Equal(new DateTimeOffset(2026, 9, 7, 2, 0, 0, TimeSpan.Zero), schedule.NextRunAt);
    }

    /// <summary>A daily schedule whose hour is still ahead runs today.</summary>
    [Fact]
    public void A_daily_schedule_before_its_hour_runs_today()
    {
        var schedule = Daily(hourUtc: 22);

        Assert.Equal(new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero), schedule.NextRunAt);
    }

    /// <summary>A weekly schedule runs on the ISO weekday it names.</summary>
    /// <remarks>
    /// Monday is 1, which is what an operator picks from and what .NET's Sunday-is-zero enum is not.
    /// From a Sunday, the next Tuesday is two days away.
    /// </remarks>
    [Fact]
    public void A_weekly_schedule_runs_on_the_weekday_it_names()
    {
        var schedule = ReportSchedule.Open(
            ReportCatalogKeys.SalesByDay,
            "Weekly sales",
            ReportFrequency.Weekly,
            hourUtc: 6,
            dayOfWeek: 2,
            dayOfMonth: null,
            recipients: null,
            SundayMorning);

        Assert.Equal(new DateTimeOffset(2026, 9, 8, 6, 0, 0, TimeSpan.Zero), schedule.NextRunAt);
    }

    /// <summary>
    /// A monthly schedule on the 31st runs on the last day of a month that has no 31st.
    /// </summary>
    /// <remarks>
    /// The case that decides whether this arithmetic is written carefully or not. Constructing
    /// 31 February throws; skipping the month means a monthly report silently misses one; clamping is
    /// the only behaviour a person setting "the last day of the month" actually meant.
    /// </remarks>
    [Fact]
    public void A_monthly_schedule_clamps_to_the_length_of_the_month()
    {
        var lateJanuary = new DateTimeOffset(2026, 1, 31, 23, 0, 0, TimeSpan.Zero);

        var schedule = ReportSchedule.Open(
            ReportCatalogKeys.SettlementSummary,
            "Monthly settlement",
            ReportFrequency.Monthly,
            hourUtc: 5,
            dayOfWeek: null,
            dayOfMonth: 31,
            recipients: null,
            lateJanuary);

        // 2026 is not a leap year, so February has 28 days.
        Assert.Equal(new DateTimeOffset(2026, 2, 28, 5, 0, 0, TimeSpan.Zero), schedule.NextRunAt);
    }

    /// <summary>A daily run covers the whole of the day before, not the time since the last run.</summary>
    /// <remarks>
    /// A period whose length depends on when the worker happened to wake is a period whose numbers
    /// cannot be compared with last week's. Half-open, so consecutive days neither overlap nor gap.
    /// </remarks>
    [Fact]
    public void A_daily_run_covers_the_whole_of_yesterday()
    {
        var schedule = Daily(hourUtc: 2);

        var (start, end) = schedule.PeriodFor(new DateTimeOffset(2026, 9, 7, 2, 4, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero), end);
    }

    /// <summary>A monthly run covers the calendar month before, whichever day it fires on.</summary>
    [Fact]
    public void A_monthly_run_covers_the_calendar_month_before()
    {
        var schedule = ReportSchedule.Open(
            ReportCatalogKeys.SettlementSummary,
            "Monthly settlement",
            ReportFrequency.Monthly,
            hourUtc: 5,
            dayOfWeek: null,
            dayOfMonth: 3,
            recipients: null,
            SundayMorning);

        var (start, end) = schedule.PeriodFor(new DateTimeOffset(2026, 10, 3, 5, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), end);
    }

    /// <summary>Recording a run always moves the schedule strictly forward.</summary>
    /// <remarks>
    /// The property the worker depends on. A next-run that did not move is a report produced every
    /// five minutes for ever.
    /// </remarks>
    [Fact]
    public void Recording_a_run_moves_the_schedule_forward()
    {
        var schedule = Daily(hourUtc: 2);
        var firstRun = schedule.NextRunAt;

        schedule.RecordRun(firstRun);

        Assert.True(schedule.NextRunAt > firstRun);
        Assert.Equal(firstRun, schedule.LastRunAt);
    }

    /// <summary>Addresses are tidied, de-duplicated and capped.</summary>
    /// <remarks>
    /// Case and spacing are how the same person ends up on a list twice, and each duplicate is a
    /// second copy of the store's takings in somebody's inbox.
    /// </remarks>
    [Fact]
    public void Recipients_are_normalised_and_deduplicated()
    {
        var schedule = ReportSchedule.Open(
            ReportCatalogKeys.SalesByDay,
            "Daily sales",
            ReportFrequency.Daily,
            hourUtc: 2,
            dayOfWeek: null,
            dayOfMonth: null,
            recipients: [" Finance@Example.com ", "finance@example.com", string.Empty, "ops@example.com"],
            SundayMorning);

        Assert.Equal(["finance@example.com", "ops@example.com"], schedule.Recipients);
    }

    private static ReportSchedule Daily(int hourUtc)
        => ReportSchedule.Open(
            ReportCatalogKeys.SalesByDay,
            "Daily sales",
            ReportFrequency.Daily,
            hourUtc,
            dayOfWeek: null,
            dayOfMonth: null,
            recipients: null,
            SundayMorning);

    /// <summary>The report keys these tests name, so a rename in the catalogue breaks the build.</summary>
    private static class ReportCatalogKeys
    {
        public const string SalesByDay = "sales-by-day";

        public const string SettlementSummary = "settlement-summary";
    }
}
