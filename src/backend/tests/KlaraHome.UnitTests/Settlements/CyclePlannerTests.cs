using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Settlements.Infrastructure.Accounting;

namespace KlaraHome.UnitTests.Settlements;

/// <summary>
/// Where the lines between settlement periods fall.
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1. Period arithmetic has exactly one right
/// answer and two failure modes that are both money in the wrong place: overlapping periods settle a
/// sale twice, and a gap between them settles it never. Neither throws, and both are invisible until
/// a seller adds up their statements.
/// </remarks>
public sealed class CyclePlannerTests
{
    /// <summary>Half past six on a Wednesday evening in India, expressed in UTC.</summary>
    private static readonly DateTimeOffset WednesdayEvening =
        new(2026, 9, 9, 13, 0, 0, TimeSpan.Zero);

    /// <summary>A weekly period runs Monday to Monday, in India Standard Time.</summary>
    /// <remarks>
    /// Half-open: the Monday it starts on is included and the Monday it ends on is not, which is the
    /// only arrangement in which consecutive weeks neither overlap nor leave a gap.
    /// </remarks>
    [Fact]
    public void A_weekly_period_runs_monday_to_monday()
    {
        var policy = new SettlementSettings();

        var period = CyclePlanner.PeriodOf(WednesdayEvening, policy);

        Assert.Equal(new DateTimeOffset(2026, 9, 7, 0, 0, 0, FiveThirty), period.Start);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 0, 0, 0, FiveThirty), period.End);

        Assert.True(period.Contains(WednesdayEvening));
        Assert.True(period.Contains(period.Start));
        Assert.False(period.Contains(period.End));
    }

    /// <summary>
    /// Boundaries are drawn in India Standard Time, not UTC.
    /// </summary>
    /// <remarks>
    /// A sale at 03:00 IST on Monday is stored as 21:30 UTC on Sunday. A planner working in UTC would
    /// put it in the previous week — and the seller would be paid for it seven days late, on a
    /// statement covering days that do not match the ones they see in their own dashboard.
    /// </remarks>
    [Fact]
    public void The_boundary_is_indian_midnight_and_not_utc_midnight()
    {
        var policy = new SettlementSettings();

        // 21:30 UTC on Sunday 6 September is 03:00 IST on Monday 7 September.
        var earlyMonday = new DateTimeOffset(2026, 9, 6, 21, 30, 0, TimeSpan.Zero);

        var period = CyclePlanner.PeriodOf(earlyMonday, policy);

        Assert.Equal(new DateTimeOffset(2026, 9, 7, 0, 0, 0, FiveThirty), period.Start);
    }

    /// <summary>Consecutive periods tile: each one starts exactly where the last ended.</summary>
    /// <remarks>
    /// The invariant the whole ledger rests on. It is asserted for all three frequencies because each
    /// anchors differently — a week on a configured day, a fortnight on the 1st and the 16th, a month
    /// on the calendar — and only one of the three has a fixed length.
    /// </remarks>
    [Theory]
    [InlineData(SettlementFrequencies.Weekly)]
    [InlineData(SettlementFrequencies.Fortnightly)]
    [InlineData(SettlementFrequencies.Monthly)]
    public void Consecutive_periods_neither_overlap_nor_leave_a_gap(string frequency)
    {
        var policy = new SettlementSettings { Frequency = frequency };
        var period = CyclePlanner.PeriodOf(new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero), policy);

        // A year and a bit of them, which walks through every month length including February.
        for (var index = 0; index < 30; index++)
        {
            var next = CyclePlanner.NextPeriod(period, policy);

            Assert.Equal(period.End, next.Start);
            Assert.True(next.End > next.Start);

            period = next;
        }
    }

    /// <summary>
    /// The period before is what the scheduler settles, and it ends where the current one starts.
    /// </summary>
    /// <remarks>
    /// The current period is still accruing by definition. Settling it would pay a seller for a week
    /// that is not over.
    /// </remarks>
    [Fact]
    public void The_previous_period_ends_where_the_current_one_begins()
    {
        var policy = new SettlementSettings();

        var current = CyclePlanner.PeriodOf(WednesdayEvening, policy);
        var previous = CyclePlanner.PreviousPeriod(WednesdayEvening, policy);

        Assert.Equal(current.Start, previous.End);
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 0, 0, 0, FiveThirty), previous.Start);
    }

    /// <summary>A period becomes closable its hold in days after it ends.</summary>
    /// <remarks>
    /// The hold is what gives a shopper time to send something back before the seller is paid for it.
    /// A sale delivered on the last day of the period gets exactly the store's return window and not a
    /// day less, which is the whole reason the hold is measured from the period end rather than from
    /// the sale.
    /// </remarks>
    [Fact]
    public void A_period_is_closable_only_after_its_hold_expires()
    {
        var policy = new SettlementSettings { HoldDays = 7 };
        var period = CyclePlanner.PeriodOf(WednesdayEvening, policy);

        Assert.Equal(period.End.AddDays(7), CyclePlanner.ClosableFrom(period, policy));

        var immediate = new SettlementSettings { HoldDays = 0 };
        Assert.Equal(period.End, CyclePlanner.ClosableFrom(period, immediate));
    }

    /// <summary>A fortnight is the 1st to the 15th, then the 16th to month end.</summary>
    /// <remarks>
    /// Not fourteen days. Two halves of a calendar month is what an Indian finance team means by a
    /// fortnightly settlement, and a rolling fourteen-day period would drift away from the month
    /// boundaries every statutory filing is aligned to.
    /// </remarks>
    [Fact]
    public void A_fortnight_is_two_halves_of_a_calendar_month()
    {
        var policy = new SettlementSettings { Frequency = SettlementFrequencies.Fortnightly };

        var first = CyclePlanner.PeriodOf(new DateTimeOffset(2026, 2, 10, 0, 0, 0, FiveThirty), policy);
        var second = CyclePlanner.PeriodOf(new DateTimeOffset(2026, 2, 20, 0, 0, 0, FiveThirty), policy);

        Assert.Equal(new DateTimeOffset(2026, 2, 1, 0, 0, 0, FiveThirty), first.Start);
        Assert.Equal(new DateTimeOffset(2026, 2, 16, 0, 0, 0, FiveThirty), first.End);

        Assert.Equal(new DateTimeOffset(2026, 2, 16, 0, 0, 0, FiveThirty), second.Start);

        // March, because February is short and the second half runs to the end of the month whatever
        // that turns out to be.
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 0, 0, 0, FiveThirty), second.End);
    }

    /// <summary>India Standard Time, as a fixed offset.</summary>
    private static TimeSpan FiveThirty => new(5, 30, 0);
}
