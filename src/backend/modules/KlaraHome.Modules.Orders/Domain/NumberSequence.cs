using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Orders.Domain;

/// <summary>
/// A gapless counter, one row per scope per financial year (docs/03-database-design.md §4.8).
/// </summary>
/// <remarks>
/// <para>
/// A table rather than a PostgreSQL sequence, and the difference is the entire point. <c>nextval</c>
/// is fast because it is <em>not</em> transactional: a transaction that rolls back keeps the number
/// it consumed, and the series gets a hole in it. A hole in a purchase-order series is untidy; a
/// hole in a GST invoice series is a finding at an audit, because rule 46 of the CGST Rules requires
/// a consecutive serial number.
/// </para>
/// <para>
/// So the counter is a row, taken with <c>SELECT ... FOR UPDATE</c>. The lock serialises allocation
/// within a scope — which is the cost — and a rollback gives the number back, which is the point.
/// The scope is per seller per year, so the contention is per seller and not platform-wide.
/// </para>
/// <para>
/// Order numbers use the same mechanism for a weaker reason: they need not be gapless, but they are
/// quoted to support and printed on labels, and a series that visibly skips numbers invites the
/// question "where did order 184 go".
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

    /// <summary>
    /// What the series is scoped to: the period for an order number, the seller for an invoice.
    /// </summary>
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
    /// <summary>Order numbers. Scoped to a period, so the number carries the month it was placed in.</summary>
    public const string Order = "order";

    /// <summary>Tax invoice numbers. Scoped to a seller, and gapless within their financial year.</summary>
    public const string Invoice = "invoice";

    /// <summary>Every kind this module allocates.</summary>
    public static readonly IReadOnlyList<string> All = [Order, Invoice];
}

/// <summary>
/// The Indian financial year, which runs April to March.
/// </summary>
/// <remarks>
/// Its own type rather than an inline calculation because three things depend on getting it right —
/// the invoice series, the sequence scope and what a reprint says — and an off-by-one in April would
/// silently restart a statutory series three months early.
/// </remarks>
internal static class FinancialYear
{
    /// <summary>The month a financial year starts in.</summary>
    public const int StartMonth = 4;

    /// <summary>
    /// The financial year an instant falls in, as <c>2026-27</c>.
    /// </summary>
    /// <remarks>
    /// Computed in India Standard Time rather than UTC. An order placed at 03:00 IST on 1 April is
    /// stored as 21:30 UTC on 31 March, and invoicing it into the previous financial year would put
    /// it in the wrong return.
    /// </remarks>
    /// <param name="instant">The instant.</param>
    public static string Of(DateTimeOffset instant)
    {
        var local = instant.ToOffset(IndiaOffset);
        var start = local.Month >= StartMonth ? local.Year : local.Year - 1;

        return $"{start}-{(start + 1) % 100:D2}";
    }

    /// <summary>The period an order number is scoped to, as <c>2609</c> for September 2026.</summary>
    /// <param name="instant">The instant.</param>
    public static string PeriodOf(DateTimeOffset instant)
    {
        var local = instant.ToOffset(IndiaOffset);
        return $"{local.Year % 100:D2}{local.Month:D2}";
    }

    /// <summary>
    /// India Standard Time, as a fixed offset.
    /// </summary>
    /// <remarks>
    /// A constant rather than a timezone lookup: India has one offset, has never observed daylight
    /// saving, and a <c>TimeZoneInfo</c> lookup here would take a dependency on the host's tz
    /// database for a value that cannot change.
    /// </remarks>
    private static readonly TimeSpan IndiaOffset = new(5, 30, 0);
}
