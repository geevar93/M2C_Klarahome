using KlaraHome.Contracts.Media;
using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Orders.Application;
using KlaraHome.Modules.Orders.Domain;
using KlaraHome.Modules.Orders.Infrastructure.Events;
using KlaraHome.Modules.Orders.Infrastructure.Numbering;
using KlaraHome.Modules.Orders.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Orders.Infrastructure.Invoicing;

/// <summary>
/// Raises a seller's tax invoice for one sub-order (docs/02-domain-model.md §7.2).
/// </summary>
/// <remarks>
/// <para>
/// One invoice per sub-order, once, ever. The uniqueness is enforced by an index rather than by this
/// code — two operators pressing "raise invoice" at the same moment both reach the insert and one is
/// rejected by the database — and this class's job is to allocate the number, state the tax split,
/// render the document and record the fact.
/// </para>
/// <para>
/// The tax split is <em>summed</em> from the order lines rather than recomputed. Those figures came
/// from the quote engine, which is the only thing on this platform that computes GST; an invoice
/// that resolved its own rates would be a second answer to what the customer owes, and the
/// discrepancy would surface at a return rather than at a code review.
/// </para>
/// <para>
/// A failure to render the PDF does not fail the invoice. The invoice is the numbered record, the
/// document is a rendering of it, and refusing to invoice a dispatched parcel because a storage
/// bucket was unreachable would stop the goods rather than fix the problem. The file id stays null
/// and the document can be rendered again.
/// </para>
/// </remarks>
/// <param name="context">The Ordering data context.</param>
/// <param name="numbering">Allocates the gapless invoice number.</param>
/// <param name="documents">Renders and stores the PDF.</param>
/// <param name="settings">Supplies the operator's legal identity and the grievance details.</param>
/// <param name="events">Announces the invoice.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports a rendering failure that did not stop the invoice.</param>
internal sealed partial class InvoiceService(
    OrdersDbContext context,
    OrderNumbering numbering,
    IDocumentStore documents,
    IStoreSettings settings,
    OrdersEventPublisher events,
    IClock clock,
    ILogger<InvoiceService> logger)
{
    /// <summary>
    /// Raises the invoice, or reports why it could not be raised.
    /// </summary>
    /// <remarks>
    /// It does not save. The caller's transaction is what makes the number gapless — a rollback has
    /// to give the number back — so committing here would defeat the mechanism the counter row exists
    /// to provide.
    /// </remarks>
    /// <param name="order">The order, with the sub-order's lines loaded.</param>
    /// <param name="subOrder">The seller's part to invoice.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<Invoice>> IssueAsync(
        Order order,
        SubOrder subOrder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(subOrder);

        if (subOrder.Status is SubOrderStatus.PendingPayment or SubOrderStatus.PaymentFailed)
        {
            return OrdersErrors.NotInvoiceable;
        }

        var existing = await context.Invoices
            .FirstOrDefaultAsync(invoice => invoice.SubOrderId == subOrder.Id, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            // An invoice with no document is the one case where asking again is not asking for a
            // second invoice: the number was allocated and the render failed — a bucket that was
            // briefly unreachable — and there is no other way for an operator to obtain the PDF.
            // Rendering it now attaches the document to the invoice that already exists, so the
            // series gains neither a hole nor a duplicate.
            if (existing.FileId is null)
            {
                await RenderAsync(order, subOrder, existing, cancellationToken).ConfigureAwait(false);

                return existing.FileId is null
                    ? OrdersErrors.InvoiceRenderFailed
                    : Result.Success(existing);
            }

            return OrdersErrors.AlreadyInvoiced;
        }

        var now = clock.UtcNow;
        var vendorId = subOrder.VendorId ?? Guid.Empty;

        var (series, number, financialYear) = await numbering
            .NextInvoiceNumberAsync(subOrder.VendorCode, vendorId, now, cancellationToken)
            .ConfigureAwait(false);

        var invoice = Invoice.Issue(order.Id, subOrder.Id, vendorId, number, series, financialYear, now);

        var live = subOrder.Lines.Where(line => !line.IsFullyCancelled).ToArray();

        var cgst = live.Sum(line => line.Cgst);
        var sgst = live.Sum(line => line.Sgst);
        var igst = live.Sum(line => line.Igst);

        invoice.Tax(
            order.ShippingAddress.StateCode,

            // Read off the lines rather than compared between two GSTINs. The quote engine already
            // decided the split per line against the selling seller's own registration, and
            // re-deciding it here from the addresses would be the second opinion this module exists
            // not to have.
            isIntraState: igst == 0m,
            live.Sum(line => line.TaxableValue),
            cgst,
            sgst,
            igst,
            live.Sum(line => line.Cess),
            live.Sum(line => line.LineTotal - line.CancelledValue) + subOrder.ShippingTotal,
            subOrder.CurrencyCode);

        await RenderAsync(order, subOrder, invoice, cancellationToken).ConfigureAwait(false);

        context.Invoices.Add(invoice);

        order.Record(OrderEvent.Record(
            order.Id,
            subOrder.Id,
            OrderEventTypes.Invoiced,
            OrderActor.System,
            actorId: null,
            $"Tax invoice {invoice.InvoiceNumber} raised.",
            payload: null,
            visible: true,
            now));

        events.Invoiced(order, invoice);

        return Result.Success(invoice);
    }

    /// <summary>
    /// Renders the PDF and attaches it, or leaves the invoice without one.
    /// </summary>
    /// <remarks>
    /// Deliberately swallowing. The document is a rendering of the invoice and not the invoice
    /// itself; a bucket that is briefly unreachable must not stop a parcel from being dispatched with
    /// a validly numbered invoice against it.
    /// </remarks>
    private async Task RenderAsync(
        Order order,
        SubOrder subOrder,
        Invoice invoice,
        CancellationToken cancellationToken)
    {
        try
        {
            var branding = await settings.GetAsync<BrandingSettings>(cancellationToken).ConfigureAwait(false);
            var legal = await settings.GetAsync<LegalSettings>(cancellationToken).ConfigureAwait(false);
            var support = await settings.GetAsync<SupportSettings>(cancellationToken).ConfigureAwait(false);

            var document = InvoiceDocumentBuilder.Build(order, subOrder, invoice, branding, legal, support);

            var file = await documents
                .RenderAsync(
                    document,
                    $"invoice-{invoice.InvoiceNumber.Replace('/', '-')}.pdf",
                    "Invoice",
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

    [LoggerMessage(
        EventId = 7310,
        Level = LogLevel.Error,
        Message = "Invoice {InvoiceNumber} was raised but its document could not be rendered")]
    private static partial void RenderFailed(ILogger logger, string invoiceNumber, Exception exception);
}
