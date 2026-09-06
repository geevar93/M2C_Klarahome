using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Payments;
using KlaraHome.Contracts.Returns;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure.Accounting;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Settlements.Infrastructure.Events;

/// <summary>
/// Keeps a seller's account in step with what happened to their goods and their money.
/// </summary>
/// <remarks>
/// <para>
/// Four facts reach this module, and between them they are the whole of a seller's earnings.
/// </para>
/// <para>
/// <b>A parcel was delivered.</b> That is when a prepaid sale is earned: the shopper has the goods,
/// the money is the platform's, and what the seller is owed becomes a number. Nothing is earned at
/// confirmation or at dispatch, because an order that never arrives was never a sale.
/// </para>
/// <para>
/// <b>A courier remitted cash.</b> The cash-on-delivery half of the same fact, and it arrives days
/// later. The money exists when the courier collects it and is the platform's when they hand it over,
/// and a settlement that credited a seller on delivery would be paying out of its own pocket for the
/// week in between. So a cash parcel earns on remittance and a prepaid one on delivery, through one
/// method with one source key.
/// </para>
/// <para>
/// <b>A seller's part was cancelled</b>, and <b>a credit note was raised</b>. Both reverse a supply,
/// proportionally, and both are harmless when nothing was ever earned — a cancellation before
/// delivery has nothing to reverse, and posts nothing.
/// </para>
/// <para>
/// <b><c>RefundProcessed</c> is deliberately not consumed.</b> This ledger reverses <em>supplies</em>,
/// not payments: a cancellation and a credit note are the two documents that undo a supply, and a
/// refund is money moving in response to one of them. Subscribing to all three would reverse the same
/// sale twice and charge the seller for the privilege. Where money goes back is the Payments module's
/// business and appears on its own reconciliation, not on the seller's statement.
/// </para>
/// <para>
/// Delivery is at-least-once, so every method here is idempotent — and not by being careful, but
/// because every posting carries a source key with a unique index behind it.
/// </para>
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="poster">The one place the ledger is written to.</param>
/// <param name="orders">Reads whether a sale was prepaid or cash, over the contract.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what was earned and what was reversed.</param>
internal sealed partial class SettlementLifecycleHandlers(
    SettlementsDbContext context,
    SettlementPoster poster,
    IOrderSettlement orders,
    IClock clock,
    ILogger<SettlementLifecycleHandlers> logger)
    : IIntegrationEventHandler<SubOrderStatusChanged>,
        IIntegrationEventHandler<SubOrderCancelled>,
        IIntegrationEventHandler<CodCashRecorded>,
        IIntegrationEventHandler<CreditNoteIssued>
{
    /// <summary>The sub-order state that means the goods arrived.</summary>
    /// <remarks>
    /// Spelled as a string because no module may take another's enum. It is compared
    /// case-insensitively for the same reason the shipping module compares its own: the word crosses a
    /// serialisation boundary, and a casing change on the far side must not silently stop paying
    /// sellers.
    /// </remarks>
    private const string Delivered = "Delivered";

    /// <summary>The payment method that means the money arrives later.</summary>
    private const string CashOnDelivery = "CashOnDelivery";

    /// <summary>
    /// Earns a prepaid sale when the parcel arrives.
    /// </summary>
    /// <remarks>
    /// Subscribed to the general status event rather than to a delivery-specific one, because
    /// delivery is a state and not an event in the ordering machine — and a handler that switched on
    /// the state is a handler that keeps working when a state is inserted before it.
    /// </remarks>
    public async Task HandleAsync(SubOrderStatusChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!string.Equals(integrationEvent.ToStatus, Delivered, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var sale = await orders
            .GetAsync(integrationEvent.SubOrderId, cancellationToken)
            .ConfigureAwait(false);

        if (sale.IsFailure)
        {
            return;
        }

        // A cash parcel is not earned yet. The courier has the money and the platform does not, and
        // `CodCashRecorded` is what says otherwise.
        if (string.Equals(sale.Value.PaymentMethod, CashOnDelivery, StringComparison.OrdinalIgnoreCase))
        {
            CashAwaited(logger, integrationEvent.SubOrderNumber);
            return;
        }

        var posted = await poster
            .PostEarningAsync(integrationEvent.SubOrderId, clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (posted.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Earns a cash sale when the courier hands the money over.
    /// </summary>
    /// <remarks>
    /// Only on remittance. The same event is published when the cash is collected at the door, and
    /// crediting a seller then would credit them money that is in a van.
    /// </remarks>
    public async Task HandleAsync(CodCashRecorded integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!integrationEvent.IsRemitted)
        {
            return;
        }

        var posted = await poster
            .PostEarningAsync(integrationEvent.SubOrderId, integrationEvent.OccurredAt, cancellationToken)
            .ConfigureAwait(false);

        if (posted.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reverses what a cancellation took off a seller's part.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyed on the event's own id, which is stable across redeliveries and different for each
    /// cancellation — so two partial cancellations against one parcel post two reversals and a
    /// redelivered one posts none.
    /// </para>
    /// <para>
    /// A cancellation before delivery finds nothing credited and posts nothing, which is the common
    /// case: the sale had not been earned, so there is nothing to take back.
    /// </para>
    /// </remarks>
    public async Task HandleAsync(SubOrderCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.CancelledTotal <= 0m)
        {
            return;
        }

        // The taxable value is not on the cancellation event, so it is taken from the sale itself and
        // apportioned. It is only ever used as a TCS base, and TCS is charged on the net of a period
        // — so an approximation here is a rounding on a rounding rather than a figure a seller sees.
        var taxable = await TaxableShareAsync(
                integrationEvent.SubOrderId,
                integrationEvent.CancelledTotal,
                cancellationToken)
            .ConfigureAwait(false);

        var posted = await poster
            .PostReversalAsync(
                integrationEvent.VendorId,
                integrationEvent.SubOrderId,
                integrationEvent.CancelledTotal,
                taxable,
                integrationEvent.CurrencyCode,
                LedgerReferenceTypes.SubOrder,
                integrationEvent.SubOrderId,
                integrationEvent.EventId.ToString(),
                $"Cancellation on {integrationEvent.SubOrderNumber}",
                clock.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

        if (posted.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reverses the supply a credit note credits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The credit note rather than the refund, and that is the whole of this module's rule about
    /// returns. Under section 34 of the CGST Act a supplier reduces their liability by issuing a note,
    /// and the note is raised whether or not money moved — a return refunded entirely to store credit
    /// still reverses the supply, and the seller's account must show it.
    /// </para>
    /// <para>
    /// It carries its own taxable value, so nothing is apportioned here: the note was built from the
    /// frozen order line and its tax is the tax that was charged.
    /// </para>
    /// </remarks>
    public async Task HandleAsync(CreditNoteIssued integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.Total <= 0m)
        {
            return;
        }

        var posted = await poster
            .PostReversalAsync(
                integrationEvent.VendorId,
                integrationEvent.SubOrderId,
                integrationEvent.Total,
                integrationEvent.TaxableValue,
                integrationEvent.CurrencyCode,
                LedgerReferenceTypes.CreditNote,
                integrationEvent.CreditNoteId,
                integrationEvent.CreditNoteId.ToString(),
                $"Credit note {integrationEvent.CreditNoteNumber}",
                clock.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

        if (posted.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The taxable value inside an amount, in the proportion the original sale had.
    /// </summary>
    /// <remarks>
    /// Read from what was posted rather than recomputed from a GST rate. The rate that applied is on
    /// the invoice and may since have changed, and the ratio between what was charged and what it was
    /// taxed on is the one figure that is certainly still true.
    /// </remarks>
    private async Task<decimal> TaxableShareAsync(
        Guid subOrderId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var sale = await context.LedgerEntries
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(entry => entry.TenantId == context.TenantId
                            && entry.SubOrderId == subOrderId
                            && entry.EntryType == LedgerEntryTypes.Sale)
            .Select(entry => new { entry.Amount, entry.TaxableValue })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return sale is null or { Amount: <= 0m }
            ? 0m
            : SettlementCalculator.Round(amount * (sale.TaxableValue / sale.Amount));
    }

    [LoggerMessage(EventId = 1840, Level = LogLevel.Debug,
        Message = "{SubOrderNumber} was delivered against cash, so nothing is earned until the courier "
                  + "remits.")]
    private static partial void CashAwaited(ILogger logger, string subOrderNumber);
}
