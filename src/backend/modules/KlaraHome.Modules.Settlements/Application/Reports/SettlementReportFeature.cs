using KlaraHome.Contracts.Vendors;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using KlaraHome.Modules.Settlements.Infrastructure.Reporting;
using KlaraHome.SharedKernel.Primitives;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Settlements.Application.Reports;

/// <summary>The TCS and TDS taken across a period, one line per seller.</summary>
/// <param name="From">The first instant covered.</param>
/// <param name="To">The first instant not covered.</param>
/// <param name="VendorId">Filter to one seller.</param>
internal sealed record GetStatutoryExtractQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? VendorId) : IQuery<StatutoryExtractResponse>;

/// <summary>The same extract as a spreadsheet.</summary>
/// <param name="From">The first instant covered.</param>
/// <param name="To">The first instant not covered.</param>
/// <param name="VendorId">Filter to one seller.</param>
internal sealed record ExportStatutoryExtractQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? VendorId) : IQuery<byte[]>;

/// <summary>What the platform itself earned over a window.</summary>
/// <param name="From">The first instant covered.</param>
/// <param name="To">The first instant not covered.</param>
internal sealed record GetPlatformRevenueQuery(
    DateTimeOffset? From,
    DateTimeOffset? To) : IQuery<PlatformRevenueResponse>;

/// <summary>One seller's statement as a spreadsheet.</summary>
/// <param name="VendorId">Whose. Ignored for a seller caller, who has only their own.</param>
/// <param name="From">The first instant covered.</param>
/// <param name="To">The first instant not covered.</param>
internal sealed record ExportVendorStatementQuery(
    Guid VendorId,
    DateTimeOffset? From,
    DateTimeOffset? To) : IQuery<byte[]>;

/// <summary>
/// Builds the TCS/TDS extract from the closed cycles in a window.
/// </summary>
/// <remarks>
/// <para>
/// Read from the cycles rather than re-summed from the ledger, and that is the point of a cycle
/// storing its own totals. What was <em>declared</em> for a period is what was on the statement the
/// seller was sent and what will be on the return; re-deriving it from a ledger that has since
/// acquired an adjustment would produce a figure nobody has ever seen.
/// </para>
/// <para>
/// Only closed and paid cycles appear. An open period has no declared figure yet — its TCS is not
/// computed until it closes — and including it would put a provisional number in a statutory filing.
/// </para>
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="vendors">Resolves each seller's GSTIN and PAN, which the filing needs.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class GetStatutoryExtractQueryHandler(
    SettlementsDbContext context,
    SettlementsScope scope,
    IVendorPayouts vendors,
    IClock clock)
    : IQueryHandler<GetStatutoryExtractQuery, StatutoryExtractResponse>
{
    public async Task<Result<StatutoryExtractResponse>> HandleAsync(
        GetStatutoryExtractQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var extract = await BuildAsync(
                context,
                scope,
                vendors,
                clock,
                query.From,
                query.To,
                query.VendorId,
                cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(extract);
    }

    /// <summary>
    /// The extract itself, shared by the JSON and the spreadsheet endpoints.
    /// </summary>
    /// <remarks>
    /// One builder rather than two, because the two must agree to the paisa: a finance team that
    /// exported a file and then queried the same period would otherwise have two documents to
    /// reconcile before they could file either.
    /// </remarks>
    internal static async Task<StatutoryExtractResponse> BuildAsync(
        SettlementsDbContext context,
        SettlementsScope scope,
        IVendorPayouts vendors,
        IClock clock,
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? vendorId,
        CancellationToken cancellationToken)
    {
        // A month back by default, which is the period a monthly GSTR-8 covers and the commonest
        // thing a finance team asks for.
        var end = to ?? clock.UtcNow;
        var start = from ?? end.AddMonths(-1);

        var rows = context.Cycles
            .AsNoTracking()
            .Where(cycle => cycle.Status != SettlementCycleStatus.Open
                            && cycle.PeriodEnd > start
                            && cycle.PeriodEnd <= end);

        if (scope.VendorFilter(vendorId) is { } wanted)
        {
            rows = rows.Where(cycle => cycle.VendorId == wanted);
        }

        var cycles = await rows
            .OrderBy(cycle => cycle.VendorId)
            .ThenBy(cycle => cycle.PeriodStart)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var profiles = await vendors
            .FindManyAsync([.. cycles.Select(cycle => cycle.VendorId ?? Guid.Empty).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        var lines = cycles
            .Select(cycle =>
            {
                var profile = profiles.GetValueOrDefault(cycle.VendorId ?? Guid.Empty);

                return new StatutoryExtractRow(
                    cycle.VendorId ?? Guid.Empty,
                    profile?.Code,
                    profile?.LegalName,
                    profile?.Gstin,
                    profile?.Pan,
                    cycle.PeriodStart,
                    cycle.PeriodEnd,
                    cycle.GrossSales,
                    cycle.TotalRefunds,
                    Math.Max(0m, cycle.TaxableSales - cycle.TaxableRefunds),
                    cycle.Tcs,
                    Math.Max(0m, cycle.GrossSales - cycle.TotalRefunds),
                    cycle.Tds,
                    cycle.CurrencyCode);
            })
            .ToArray();

        return new StatutoryExtractResponse(
            start,
            end,
            lines.Sum(row => row.Tcs),
            lines.Sum(row => row.Tds),
            lines.Length > 0 ? lines[0].CurrencyCode : Money.Inr,
            lines);
    }
}

/// <summary>Writes the TCS/TDS extract as a spreadsheet.</summary>
/// <param name="context">The Settlements data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="vendors">Resolves each seller's GSTIN and PAN.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ExportStatutoryExtractQueryHandler(
    SettlementsDbContext context,
    SettlementsScope scope,
    IVendorPayouts vendors,
    IClock clock)
    : IQueryHandler<ExportStatutoryExtractQuery, byte[]>
{
    /// <summary>The column headings, in the order a GSTR-8 and a 27EQ working paper read them.</summary>
    private static readonly string[] Header =
    [
        "Vendor code", "Vendor name", "GSTIN", "PAN", "Period start", "Period end",
        "Gross sales", "Refunds", "Net taxable supplies", "TCS", "Net gross sales", "TDS",
        "Currency",
    ];

    public async Task<Result<byte[]>> HandleAsync(
        ExportStatutoryExtractQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var extract = await GetStatutoryExtractQueryHandler
            .BuildAsync(context, scope, vendors, clock, query.From, query.To, query.VendorId, cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<IReadOnlyList<string?>>(extract.Rows.Count + 1) { Header };

        rows.AddRange(extract.Rows.Select(row => new[]
        {
            row.VendorCode,
            row.VendorName,
            row.Gstin,
            row.Pan,
            Csv.Date(row.PeriodStart),
            Csv.Date(row.PeriodEnd),
            Csv.Money(row.GrossSales),
            Csv.Money(row.Refunds),
            Csv.Money(row.NetTaxableSupplies),
            Csv.Money(row.Tcs),
            Csv.Money(row.NetGrossSales),
            Csv.Money(row.Tds),
            row.CurrencyCode,
        }));

        return Result.Success(Csv.Write(rows));
    }
}

/// <summary>
/// Sums what the platform earned over a window.
/// </summary>
/// <remarks>
/// <para>
/// Every figure here is a debit on some seller's ledger, and that is what makes the report
/// trustworthy: the platform's revenue is exactly what the sellers were charged, and there is no
/// second place either number could come from. A revenue report built from orders rather than from
/// the ledger would drift the first time a return was processed.
/// </para>
/// <para>
/// TCS and TDS are reported and are explicitly not revenue. They are collected on the government's
/// behalf and remitted, and a report that added them to the platform's margin would overstate it by
/// the exact amount the platform owes.
/// </para>
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class GetPlatformRevenueQueryHandler(
    SettlementsDbContext context,
    SettlementsScope scope,
    IClock clock)
    : IQueryHandler<GetPlatformRevenueQuery, PlatformRevenueResponse>
{
    public async Task<Result<PlatformRevenueResponse>> HandleAsync(
        GetPlatformRevenueQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (scope.IsVendor)
        {
            return Result.Failure<PlatformRevenueResponse>(SettlementsErrors.VendorForbidden);
        }

        var to = query.To ?? clock.UtcNow;
        var from = query.From ?? to.AddMonths(-1);

        var totals = await context.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.OccurredAt >= from && entry.OccurredAt < to)
            .GroupBy(entry => entry.EntryType)
            .Select(group => new { EntryType = group.Key, Amount = group.Sum(entry => entry.Amount) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var by = totals.ToDictionary(total => total.EntryType, total => total.Amount, StringComparer.Ordinal);

        decimal Of(string entryType) => by.GetValueOrDefault(entryType, 0m);

        var commission = Of(LedgerEntryTypes.Commission);
        var platformFee = Of(LedgerEntryTypes.PlatformFee);
        var paymentFee = Of(LedgerEntryTypes.PaymentFee);
        var shippingFee = Of(LedgerEntryTypes.ShippingFee);
        var reversed = Of(LedgerEntryTypes.RefundCommissionReversal);

        return Result.Success(new PlatformRevenueResponse(
            from,
            to,
            Of(LedgerEntryTypes.Sale),
            commission,
            platformFee,
            paymentFee,
            shippingFee,
            Of(LedgerEntryTypes.PlatformTax),
            Of(LedgerEntryTypes.Refund),
            reversed,

            // Net of what was given back on returns, and excluding the platform's own output tax:
            // the GST on a commission is collected from the seller and remitted, not kept.
            commission + platformFee + paymentFee + shippingFee - reversed,
            Of(LedgerEntryTypes.Tcs),
            Of(LedgerEntryTypes.Tds),
            Of(LedgerEntryTypes.Payout),
            Money.Inr));
    }
}

/// <summary>
/// Writes one seller's statement as a spreadsheet.
/// </summary>
/// <remarks>
/// The same window and the same rows the JSON statement returns, in the format a seller's accountant
/// actually works in. It goes through the statement query rather than reading the ledger again, so
/// the file and the screen cannot disagree.
/// </remarks>
/// <param name="dispatcher">Runs the statement query this export formats.</param>
/// <param name="options">Supplies the export ceiling, which the statement query enforces.</param>
internal sealed class ExportVendorStatementQueryHandler(
    IDispatcher dispatcher,
    IOptions<SettlementsOptions> options)
    : IQueryHandler<ExportVendorStatementQuery, byte[]>
{
    /// <summary>The column headings, in the order a bank statement reads them.</summary>
    private static readonly string[] Header =
        ["Date", "Type", "Description", "Reference", "Credit", "Debit", "Balance", "Currency"];

    public async Task<Result<byte[]>> HandleAsync(
        ExportVendorStatementQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var statement = await dispatcher
            .QueryAsync(
                new Ledger.GetVendorStatementQuery(query.VendorId, query.From, query.To),
                cancellationToken)
            .ConfigureAwait(false);

        if (statement.IsFailure)
        {
            return Result.Failure<byte[]>(statement.Error);
        }

        var value = statement.Value;

        if (value.Entries.Count > options.Value.MaxExportRows)
        {
            return Result.Failure<byte[]>(SettlementsErrors.ExportTooLarge(options.Value.MaxExportRows));
        }

        var running = value.OpeningBalance;

        var rows = new List<IReadOnlyList<string?>>(value.Entries.Count + 2)
        {
            Header,

            // The opening balance as its own row, because a statement that started at the first
            // movement would be a statement whose closing figure could not be checked.
            new[]
            {
                Csv.Date(value.From),
                "opening_balance",
                "Balance brought forward",
                null,
                null,
                null,
                Csv.Money(running),
                value.CurrencyCode,
            },
        };

        foreach (var entry in value.Entries)
        {
            running += entry.SignedAmount;

            rows.Add(new[]
            {
                Csv.Date(entry.OccurredAt),
                entry.EntryType,
                entry.Note,
                entry.SubOrderId?.ToString() ?? entry.ReferenceId?.ToString(),
                entry.Direction == nameof(LedgerDirection.Credit) ? Csv.Money(entry.Amount) : null,
                entry.Direction == nameof(LedgerDirection.Debit) ? Csv.Money(entry.Amount) : null,
                Csv.Money(running),
                entry.CurrencyCode,
            });
        }

        rows.Add(new[]
        {
            Csv.Date(value.To),
            "closing_balance",
            "Balance carried forward",
            null,
            null,
            null,
            Csv.Money(value.ClosingBalance),
            value.CurrencyCode,
        });

        return Result.Success(Csv.Write(rows));
    }
}
