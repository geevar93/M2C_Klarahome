using System.Globalization;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Settlements.Infrastructure.Numbering;

/// <summary>
/// Allocates commission-invoice numbers, gaplessly and per financial year.
/// </summary>
/// <remarks>
/// <para>
/// The same counter mechanism as the payout reference beside it, with one difference that matters:
/// the series is scoped to the <em>financial year</em> rather than the month. A tax invoice series
/// is what a GST return is filed against, and a series that restarted every month would file twelve
/// series a year where the law expects one.
/// </para>
/// <para>
/// <c>SELECT … FOR UPDATE</c>, read, increment, and no commit here — the caller's transaction is
/// what makes the number real and what gives it back on a rollback. A tax-invoice number consumed by
/// an invoice nobody kept is a gap an auditor will ask about.
/// </para>
/// </remarks>
/// <param name="context">The Settlements data context; the lock lives in its transaction.</param>
/// <param name="options">Supplies the prefix and the padding.</param>
internal sealed class CommissionInvoiceNumbering(
    SettlementsDbContext context,
    IOptions<SettlementsOptions> options)
{
    /// <summary>Takes the next invoice number, as <c>KHC/2026-27/000042</c>.</summary>
    /// <param name="raisedAt">When the invoice is being raised.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string> NextNumberAsync(DateTimeOffset raisedAt, CancellationToken cancellationToken)
    {
        var year = FinancialYear.Of(raisedAt);
        var next = await TakeAsync(NumberSequenceKinds.CommissionInvoice, year, cancellationToken)
            .ConfigureAwait(false);

        var digits = options.Value.CommissionInvoiceDigits;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{options.Value.CommissionInvoicePrefix}/{year}/{next.ToString($"D{digits}", CultureInfo.InvariantCulture)}");
    }

    /// <summary>Finds or opens the counter, locks it, and takes the next value.</summary>
    private async Task<long> TakeAsync(string kind, string scopeKey, CancellationToken cancellationToken)
    {
        var sequence = await LockAsync(kind, scopeKey, cancellationToken).ConfigureAwait(false);

        if (sequence is null)
        {
            var opened = NumberSequence.Start(kind, scopeKey);
            context.NumberSequences.Add(opened);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Re-locked rather than used directly. Two callers can both have found nothing and both
            // have inserted; one of them lost the unique index and has to read the winner's row.
            sequence = await LockAsync(kind, scopeKey, cancellationToken).ConfigureAwait(false)
                       ?? throw new InvalidOperationException(
                           $"The '{kind}' counter for '{scopeKey}' could not be opened.");
        }

        return sequence.Take();
    }

    /// <remarks>
    /// The tenant is written into the predicate rather than left to the global query filter, and
    /// <c>xmin</c> is named explicitly because it is a system column that <c>SELECT *</c> omits and
    /// the model maps as this entity's concurrency token. Both are the payout counter's reasoning,
    /// and both are the kind of thing that is wrong once and then wrong for years.
    /// </remarks>
    private async Task<NumberSequence?> LockAsync(
        string kind,
        string scopeKey,
        CancellationToken cancellationToken)
    {
        var tenantId = context.TenantId;

        return await context.NumberSequences
            .FromSql(
                $"""
                 SELECT *, xmin FROM settlements.number_sequences
                 WHERE tenant_id = {tenantId}
                   AND kind = {kind}
                   AND scope_key = {scopeKey}
                 FOR UPDATE
                 """)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
