using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Settlements.Domain;

/// <summary>
/// A gapless counter, one row per scope (docs/03-database-design.md §4.8).
/// </summary>
/// <remarks>
/// <para>
/// The third copy of this table in the platform — Ordering has one for invoice numbers, Returns for
/// credit notes, and this is the payout run's. It is duplicated for the reason every duplicated
/// primitive here is: a module may not reference another, and a counter living in
/// <c>KlaraHome.Infrastructure</c> would be infrastructure holding a row three modules write to and
/// nothing else ever will.
/// </para>
/// <para>
/// A table rather than a PostgreSQL sequence, because <c>nextval</c> is deliberately
/// non-transactional: a rolled-back transaction keeps the number it consumed. A hole in a payout
/// reference is not a statutory problem the way a hole in a credit-note series is, but it is a
/// reconciliation conversation nobody wants to have with a bank statement in front of them.
/// </para>
/// </remarks>
internal sealed class NumberSequence : Entity<Guid>, ITenantScoped
{
    private NumberSequence(Guid id, string kind, string scopeKey)
        : base(id)
    {
        Kind = kind;
        ScopeKey = scopeKey;
        NextValue = 1;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private NumberSequence()
    {
        Kind = string.Empty;
        ScopeKey = string.Empty;
    }

    /// <summary>Which series this counts: one of <see cref="NumberSequenceKinds"/>.</summary>
    public string Kind { get; private set; }

    /// <summary>What the series is scoped to — the calendar month, for a payout run.</summary>
    public string ScopeKey { get; private set; }

    /// <summary>The number the next allocation will take.</summary>
    public long NextValue { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Opens a counter at one.</summary>
    /// <param name="kind">Which series.</param>
    /// <param name="scopeKey">What it is scoped to.</param>
    public static NumberSequence Start(string kind, string scopeKey)
        => new(UuidV7.New(), Guard.NotNullOrWhiteSpace(kind), Guard.NotNullOrWhiteSpace(scopeKey));

    /// <summary>Takes the next number. Only ever called while the row is locked.</summary>
    public long Take()
    {
        var value = NextValue;
        NextValue = value + 1;
        return value;
    }
}

/// <summary>The series this module counts.</summary>
internal static class NumberSequenceKinds
{
    /// <summary>Payout batch references. Scoped to a calendar month.</summary>
    public const string Payout = "payout";

    /// <summary>Every kind this module allocates.</summary>
    public static readonly IReadOnlyList<string> All = [Payout];
}

/// <summary>
/// The Indian financial year, which runs April to March.
/// </summary>
/// <remarks>
/// A copy of the Ordering and Returns modules', for the same boundary reason the counter is. The
/// arithmetic must agree with theirs exactly: a TCS extract filed against a different financial year
/// from the invoices it covers is a mismatch a GST return will find.
/// </remarks>
internal static class FinancialYear
{
    /// <summary>The month a financial year starts in.</summary>
    public const int StartMonth = 4;

    /// <summary>
    /// India Standard Time, as a fixed offset.
    /// </summary>
    /// <remarks>
    /// A constant rather than a timezone lookup: India has one offset, has never observed daylight
    /// saving, and a <c>TimeZoneInfo</c> lookup here would take a dependency on the host's tz
    /// database for a value that cannot change.
    /// </remarks>
    public static readonly TimeSpan IndiaOffset = new(5, 30, 0);

    /// <summary>
    /// The financial year an instant falls in, as <c>2026-27</c>.
    /// </summary>
    /// <remarks>
    /// Computed in India Standard Time rather than UTC. A settlement closed at 03:00 IST on 1 April
    /// is stored as 21:30 UTC on 31 March, and filing it into the previous financial year would put
    /// its TDS in the wrong return.
    /// </remarks>
    /// <param name="instant">The instant.</param>
    public static string Of(DateTimeOffset instant)
    {
        var local = instant.ToOffset(IndiaOffset);
        var start = local.Month >= StartMonth ? local.Year : local.Year - 1;

        return $"{start}-{(start + 1) % 100:D2}";
    }

    /// <summary>The first instant of the financial year an instant falls in.</summary>
    /// <remarks>
    /// Needed by the TDS threshold, which is annual: how much has been deducted from a seller this
    /// year is a question about a window, and the window starts on 1 April in India Standard Time.
    /// </remarks>
    /// <param name="instant">The instant.</param>
    public static DateTimeOffset StartOf(DateTimeOffset instant)
    {
        var local = instant.ToOffset(IndiaOffset);
        var year = local.Month >= StartMonth ? local.Year : local.Year - 1;

        return new DateTimeOffset(year, StartMonth, 1, 0, 0, 0, IndiaOffset);
    }

    /// <summary>The period a payout reference is scoped to, as <c>2609</c> for September 2026.</summary>
    /// <param name="instant">The instant.</param>
    public static string PeriodOf(DateTimeOffset instant)
    {
        var local = instant.ToOffset(IndiaOffset);
        return $"{local.Year % 100:D2}{local.Month:D2}";
    }
}
