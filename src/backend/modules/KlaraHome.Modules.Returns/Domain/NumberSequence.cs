using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Returns.Domain;

/// <summary>
/// A gapless counter, one row per scope per financial year (docs/03-database-design.md §4.8).
/// </summary>
/// <remarks>
/// <para>
/// The same mechanism the Ordering module uses for invoice numbers, and it is here rather than
/// shared for the reason every duplicated primitive in this platform is: a module may not reference
/// another, and a counter that lived in <c>KlaraHome.Infrastructure</c> would be infrastructure
/// holding a row that only two modules will ever write to.
/// </para>
/// <para>
/// A table rather than a PostgreSQL sequence, because <c>nextval</c> is deliberately
/// non-transactional: a rolled-back transaction keeps the number it consumed. A hole in a return
/// number is untidy; a hole in a credit-note series is a finding at an audit, because rule 53 of the
/// CGST Rules asks for a consecutive serial number.
/// </para>
/// </remarks>
internal sealed class NumberSequence : Entity<Guid>, ITenantScoped
{
    private NumberSequence(Guid id, string kind, string scopeKey, string financialYear)
        : base(id)
    {
        Kind = kind;
        ScopeKey = scopeKey;
        FinancialYear = financialYear;
        NextValue = 1;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private NumberSequence()
    {
        Kind = string.Empty;
        ScopeKey = string.Empty;
        FinancialYear = string.Empty;
    }

    /// <summary>Which series this counts: one of <see cref="NumberSequenceKinds"/>.</summary>
    public string Kind { get; private set; }

    /// <summary>What the series is scoped to: the period for a return, the seller for a credit note.</summary>
    public string ScopeKey { get; private set; }

    /// <summary>The Indian financial year, April to March, as <c>2026-27</c>.</summary>
    public string FinancialYear { get; private set; }

    /// <summary>The number the next allocation will take.</summary>
    public long NextValue { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Opens a counter at one.</summary>
    /// <param name="kind">Which series.</param>
    /// <param name="scopeKey">What it is scoped to.</param>
    /// <param name="financialYear">The financial year.</param>
    public static NumberSequence Start(string kind, string scopeKey, string financialYear)
        => new(
            UuidV7.New(),
            Guard.NotNullOrWhiteSpace(kind),
            Guard.NotNullOrWhiteSpace(scopeKey),
            Guard.NotNullOrWhiteSpace(financialYear));

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
    /// <summary>RMA numbers. Scoped to a period, so the number carries the month it was raised in.</summary>
    public const string Return = "return";

    /// <summary>Credit-note numbers. Scoped to a seller, and gapless within their financial year.</summary>
    public const string CreditNote = "credit-note";

    /// <summary>Every kind this module allocates.</summary>
    public static readonly IReadOnlyList<string> All = [Return, CreditNote];
}

/// <summary>
/// The Indian financial year, which runs April to March.
/// </summary>
/// <remarks>
/// A copy of the Ordering module's, for the same boundary reason the counter is. The arithmetic must
/// agree with that module's exactly: a credit note raised into a different financial year from the
/// invoice it credits is a mismatch a GST return will find.
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
    private static readonly TimeSpan IndiaOffset = new(5, 30, 0);

    /// <summary>
    /// The financial year an instant falls in, as <c>2026-27</c>.
    /// </summary>
    /// <remarks>
    /// Computed in India Standard Time rather than UTC. A credit note raised at 03:00 IST on 1 April
    /// is stored as 21:30 UTC on 31 March, and filing it into the previous financial year would put
    /// it in the wrong return.
    /// </remarks>
    /// <param name="instant">The instant.</param>
    public static string Of(DateTimeOffset instant)
    {
        var local = instant.ToOffset(IndiaOffset);
        var start = local.Month >= StartMonth ? local.Year : local.Year - 1;

        return $"{start}-{(start + 1) % 100:D2}";
    }

    /// <summary>The period a return number is scoped to, as <c>2609</c> for September 2026.</summary>
    /// <param name="instant">The instant.</param>
    public static string PeriodOf(DateTimeOffset instant)
    {
        var local = instant.ToOffset(IndiaOffset);
        return $"{local.Year % 100:D2}{local.Month:D2}";
    }
}
