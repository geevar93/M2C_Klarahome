using KlaraHome.Contracts.Media;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure.Numbering;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Settlements.Infrastructure.Invoicing;

/// <summary>
/// Raises the platform's own tax invoice when a settlement cycle closes
/// (docs/02-domain-model.md §7.2).
/// </summary>
/// <remarks>
/// <para>
/// The ledger has recorded a <c>platform_tax</c> entry against every seller since Step 18 — GST the
/// platform charged them on commission and fees — and produced no document for it. A marketplace is
/// required to raise that invoice, and without it the seller cannot claim input credit against tax
/// they have already borne. This is that document (Step 28B, deliverable 23).
/// </para>
/// <para>
/// The figures are <em>read off the closed cycle's ledger entries</em> rather than recomputed. The
/// settlement calculator is the only thing on this platform that decides what a seller was charged;
/// an invoice that worked it out again would be a second answer, and the discrepancy would surface
/// in a GST return rather than in a code review.
/// </para>
/// <para>
/// One invoice per cycle, enforced by a unique index rather than by this code: two closings racing
/// both reach the insert and one is rejected by the database. A cycle that already has an invoice is
/// left alone.
/// </para>
/// <para>
/// A failure to render the PDF does not fail the invoice, and a failure to raise the invoice does
/// not fail the closing. The invoice is the numbered record and the document is a rendering of it;
/// refusing to close a settlement period because a storage bucket was unreachable would stop the
/// money rather than fix the problem.
/// </para>
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="numbering">Allocates the gapless invoice number.</param>
/// <param name="vendors">Names the seller the invoice is raised on.</param>
/// <param name="settings">Supplies the platform's legal identity and the GST rate.</param>
/// <param name="documents">Renders and stores the PDF.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports a rendering failure that did not stop the invoice.</param>
internal sealed partial class CommissionInvoiceService(
    SettlementsDbContext context,
    CommissionInvoiceNumbering numbering,
    IVendorPayouts vendors,
    IStoreSettings settings,
    IDocumentStore documents,
    IClock clock,
    ILogger<CommissionInvoiceService> logger)
{
    /// <summary>
    /// Raises the invoice for a just-closed cycle, or returns the one it already has.
    /// </summary>
    /// <remarks>
    /// It does not save. The caller's transaction is what makes the number gapless — a rollback has
    /// to give the number back — so committing here would defeat the counter the allocation exists
    /// to protect.
    /// </remarks>
    /// <param name="cycle">The closed cycle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CommissionInvoice?> RaiseAsync(SettlementCycle cycle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        if (cycle.VendorId is not { } vendorId)
        {
            // A platform-level cycle has no counterparty to invoice.
            return null;
        }

        var existing = await context.CommissionInvoices
            .FirstOrDefaultAsync(invoice => invoice.SettlementCycleId == cycle.Id, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var charges = await ChargesOfAsync(cycle.Id, cancellationToken).ConfigureAwait(false);

        // Nothing was charged, so there is nothing to invoice. A nil tax invoice is not a document
        // anybody needs and it would consume a number in a statutory series.
        if (charges.Taxable <= 0m && charges.Tax <= 0m)
        {
            return null;
        }

        var policy = await settings.GetAsync<SettlementSettings>(cancellationToken).ConfigureAwait(false);
        var legal = await settings.GetAsync<LegalSettings>(cancellationToken).ConfigureAwait(false);
        var vendor = await vendors.FindAsync(vendorId, cancellationToken).ConfigureAwait(false);

        var now = clock.UtcNow;
        var number = await numbering.NextNumberAsync(now, cancellationToken).ConfigureAwait(false);

        var invoice = CommissionInvoice.Raise(
            cycle.Id,
            vendorId,
            number,
            now,
            cycle.PeriodStart,
            cycle.PeriodEnd,
            cycle.CurrencyCode);

        var supplierState = StateCodeOf(legal.Gstin);
        var recipientState = StateCodeOf(vendor?.Gstin);
        var split = Split(charges.Tax, supplierState, recipientState);

        invoice.Charge(
            charges.Commission,
            charges.PlatformFee,
            charges.PaymentFee,
            policy.PlatformServiceGstRate,
            split.Cgst,
            split.Sgst,
            split.Igst);

        invoice.Identify(
            NullIfBlank(legal.Gstin),
            NullIfBlank(vendor?.Gstin),

            // The place of supply is the recipient's state where it is known. Where it is not — an
            // unregistered seller whose GSTIN we do not hold — it falls back to the supplier's own,
            // which is what section 12(2)(b) of the IGST Act provides for a recipient whose address
            // is not on record.
            recipientState ?? supplierState);

        context.CommissionInvoices.Add(invoice);

        await RenderAsync(invoice, vendor, legal, cancellationToken).ConfigureAwait(false);

        return invoice;
    }

    /// <summary>What the platform charged this seller in this cycle, by head.</summary>
    /// <remarks>
    /// Read off the ledger rather than off the cycle's totals, because the cycle sums commission and
    /// the tax on it into one figure — right for a statement, wrong for an invoice, which has to
    /// state the taxable value and the tax separately. The reversal on a refund is netted off the
    /// commission for the same reason it is on a statement: it reduces what was charged.
    /// </remarks>
    private async Task<CycleCharges> ChargesOfAsync(Guid cycleId, CancellationToken cancellationToken)
    {
        var rows = await context.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.SettlementCycleId == cycleId)
            .GroupBy(entry => entry.EntryType)
            .Select(group => new { EntryType = group.Key, Amount = group.Sum(entry => entry.Amount) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        decimal Of(string entryType)
            => rows.Find(row => row.EntryType == entryType)?.Amount ?? 0m;

        var commission = Of(LedgerEntryTypes.Commission) - Of(LedgerEntryTypes.RefundCommissionReversal);

        return new CycleCharges(
            Math.Max(0m, commission),
            Of(LedgerEntryTypes.PlatformFee),
            Of(LedgerEntryTypes.PaymentFee),
            Of(LedgerEntryTypes.PlatformTax));
    }

    /// <summary>
    /// Splits the tax by head: CGST and SGST within a state, IGST across one.
    /// </summary>
    /// <remarks>
    /// The halves are computed so they add back to the total exactly. Halving a figure ending in an
    /// odd paisa twice and rounding both would lose or invent a paisa, and an invoice whose parts do
    /// not sum to its total is one a GST return will reject.
    /// </remarks>
    private static (decimal Cgst, decimal Sgst, decimal Igst) Split(
        decimal tax,
        string? supplierState,
        string? recipientState)
    {
        if (recipientState is not null
            && supplierState is not null
            && !string.Equals(recipientState, supplierState, StringComparison.Ordinal))
        {
            return (0m, 0m, tax);
        }

        var half = Math.Round(tax / 2m, 2, MidpointRounding.AwayFromZero);
        return (half, tax - half, 0m);
    }

    /// <summary>The two-digit GST state code a GSTIN begins with, or null when there is no GSTIN.</summary>
    private static string? StateCodeOf(string? gstin)
        => gstin is { Length: >= 2 } ? gstin[..2] : null;

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private async Task RenderAsync(
        CommissionInvoice invoice,
        VendorPayoutProfile? vendor,
        LegalSettings legal,
        CancellationToken cancellationToken)
    {
        try
        {
            var support = await settings.GetAsync<SupportSettings>(cancellationToken).ConfigureAwait(false);
            var document = CommissionInvoiceDocumentBuilder.Build(invoice, vendor, legal, support);

            var file = await documents
                .RenderAsync(
                    document,
                    $"commission-invoice-{invoice.InvoiceNumber.Replace('/', '-')}.pdf",
                    "CommissionInvoice",
                    invoice.Id,
                    cancellationToken)
                .ConfigureAwait(false);

            invoice.Attach(file.Id);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RenderFailed(logger, invoice.InvoiceNumber, exception);
        }
    }

    /// <summary>What the platform charged in a cycle, before tax, plus the tax it charged on it.</summary>
    /// <param name="Commission">Commission, net of what refunds reversed.</param>
    /// <param name="PlatformFee">The marketplace fee.</param>
    /// <param name="PaymentFee">The gateway fee recharged.</param>
    /// <param name="Tax">The GST charged on the three above.</param>
    private sealed record CycleCharges(
        decimal Commission,
        decimal PlatformFee,
        decimal PaymentFee,
        decimal Tax)
    {
        /// <summary>What the tax was computed on.</summary>
        public decimal Taxable => Commission + PlatformFee + PaymentFee;
    }

    [LoggerMessage(EventId = 1880, Level = LogLevel.Error,
        Message = "Commission invoice {InvoiceNumber} was raised but its document could not be rendered.")]
    private static partial void RenderFailed(ILogger logger, string invoiceNumber, Exception exception);
}
