using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Settlements.Domain;

namespace KlaraHome.Modules.Settlements.Infrastructure.Accounting;

/// <summary>A settlement period, as a half-open interval.</summary>
/// <param name="Start">The first instant it covers.</param>
/// <param name="End">The first instant it does not.</param>
internal readonly record struct SettlementPeriod(DateTimeOffset Start, DateTimeOffset End)
{
    /// <summary>Whether an instant falls inside it.</summary>
    /// <param name="instant">The instant.</param>
    public bool Contains(DateTimeOffset instant) => instant >= Start && instant < End;
}

/// <summary>
/// Where the lines between settlement periods fall.
/// </summary>
/// <remarks>
/// <para>
/// Every boundary is computed in India Standard Time and then expressed as an instant. A weekly
/// period that started at midnight UTC would start at half past five in the morning for the people
/// it concerns, and a seller's "week" would cover parts of two of theirs.
/// </para>
/// <para>
/// Periods are half-open — start inclusive, end exclusive — which is the only arrangement in which
/// consecutive periods neither overlap nor leave a gap. Both of those are money in the wrong week,
/// and the second is money in no week at all.
/// </para>
/// <para>
/// Pure arithmetic on purpose: it takes an instant and a policy and returns dates. Whether a period
/// has a cycle, and whether that cycle may be closed, are questions for the service that reads the
/// database.
/// </para>
/// </remarks>
internal static class CyclePlanner
{
    /// <summary>
    /// The period an instant falls in.
    /// </summary>
    /// <param name="instant">The instant.</param>
    /// <param name="policy">The store's settlement policy, which names the frequency.</param>
    public static SettlementPeriod PeriodOf(DateTimeOffset instant, SettlementSettings policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var local = instant.ToOffset(FinancialYear.IndiaOffset);

        return policy.Frequency switch
        {
            SettlementFrequencies.Monthly => Monthly(local),
            SettlementFrequencies.Fortnightly => Fortnightly(local),
            _ => Weekly(local, policy.WeekStartDay),
        };
    }

    /// <summary>The period immediately before the one an instant falls in.</summary>
    /// <remarks>
    /// What the scheduler actually settles. The current period is still accruing by definition, so
    /// the one that can be closed is always the one before it — and the hold is measured from that
    /// period's end.
    /// </remarks>
    /// <param name="instant">The instant.</param>
    /// <param name="policy">The store's settlement policy.</param>
    public static SettlementPeriod PreviousPeriod(DateTimeOffset instant, SettlementSettings policy)
    {
        var current = PeriodOf(instant, policy);

        // One tick before the current period starts is, by construction, inside the previous one.
        return PeriodOf(current.Start.AddTicks(-1), policy);
    }

    /// <summary>The next period after one.</summary>
    /// <param name="period">The period.</param>
    /// <param name="policy">The store's settlement policy.</param>
    public static SettlementPeriod NextPeriod(SettlementPeriod period, SettlementSettings policy)
        => PeriodOf(period.End, policy);

    /// <summary>
    /// When a period becomes closable: its end, plus the store's hold.
    /// </summary>
    /// <remarks>
    /// The hold is what makes a settlement safe to pay. A sale delivered on the last day of the
    /// period still has this many days in which a shopper can send it back, and a return raised
    /// inside it reverses in the same cycle rather than clawing money back from the next one.
    /// </remarks>
    /// <param name="period">The period.</param>
    /// <param name="policy">The store's settlement policy.</param>
    public static DateTimeOffset ClosableFrom(SettlementPeriod period, SettlementSettings policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return period.End.AddDays(Math.Max(0, policy.HoldDays));
    }

    /// <summary>The week an instant falls in, anchored on the configured start day.</summary>
    private static SettlementPeriod Weekly(DateTimeOffset local, int weekStartDay)
    {
        // ISO numbering, 1 = Monday through 7 = Sunday, which is what the setting is documented as.
        var anchor = Math.Clamp(weekStartDay, 1, 7);
        var today = local.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)local.DayOfWeek;
        var back = (today - anchor + 7) % 7;

        var start = Midnight(local).AddDays(-back);

        return new SettlementPeriod(start, start.AddDays(7));
    }

    /// <summary>The half of the month an instant falls in: the 1st to the 15th, or the 16th to the end.</summary>
    private static SettlementPeriod Fortnightly(DateTimeOffset local)
    {
        var first = new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, FinancialYear.IndiaOffset);
        var middle = first.AddDays(15);

        return local.Day <= 15
            ? new SettlementPeriod(first, middle)
            : new SettlementPeriod(middle, first.AddMonths(1));
    }

    /// <summary>The calendar month an instant falls in.</summary>
    private static SettlementPeriod Monthly(DateTimeOffset local)
    {
        var first = new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, FinancialYear.IndiaOffset);
        return new SettlementPeriod(first, first.AddMonths(1));
    }

    /// <summary>The start of an instant's day, in India Standard Time.</summary>
    private static DateTimeOffset Midnight(DateTimeOffset local)
        => new(local.Year, local.Month, local.Day, 0, 0, 0, FinancialYear.IndiaOffset);
}
