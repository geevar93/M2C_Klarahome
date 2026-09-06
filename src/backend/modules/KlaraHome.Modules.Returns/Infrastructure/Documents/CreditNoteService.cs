using KlaraHome.Contracts.Media;
using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Returns.Domain;
using KlaraHome.Modules.Returns.Infrastructure.Events;
using KlaraHome.Modules.Returns.Infrastructure.Numbering;
using KlaraHome.Modules.Returns.Infrastructure.Persistence;
using KlaraHome.Modules.Returns.Infrastructure.Refunds;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Returns.Infrastructure.Documents;

/// <summary>
/// Raises the credit note that reduces a seller's output tax.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the refund on purpose, and it runs whether or not money moves. Under section 34 of
/// the CGST Act a supplier reduces their liability by issuing a credit note; a card refund does not
/// do that, and a return refunded entirely to store credit still reverses the supply. The two are
/// raised together in practice and would be wrong to couple in code.
/// </para>
/// <para>
/// It is idempotent on the return. A unique index on <c>(tenant, return_id)</c> is the real
/// guarantee — the check here is what turns a retry into the original note rather than a 500.
/// </para>
/// <para>
/// The PDF is best-effort. A note that exists in the database with no rendered document is a note an
/// operator can re-render; a note that was never issued because a font was missing is a seller
/// paying tax on goods they no longer have.
/// </para>
/// </remarks>
/// <param name="context">The Returns data context.</param>
/// <param name="numbering">Allocates the gapless number.</param>
/// <param name="documents">Renders and stores the PDF.</param>
/// <param name="vendors">Supplies the seller's code, which becomes the series.</param>
/// <param name="reference">Resolves the place of supply's two-digit GST code.</param>
/// <param name="settings">Supplies the operator's own details and the grievance officer.</param>
/// <param name="events">Announces the note.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what was raised and what could not be rendered.</param>
internal sealed partial class CreditNoteService(
    ReturnsDbContext context,
    ReturnNumbering numbering,
    IDocumentStore documents,
    IVendorDirectory vendors,
    IReferenceData reference,
    IStoreSettings settings,
    ReturnsEventPublisher events,
    IClock clock,
    ILogger<CreditNoteService> logger)
{
    /// <summary>
    /// Raises the note for a return, or returns the one already raised.
    /// </summary>
    /// <param name="request">The RMA, after quality control.</param>
    /// <param name="order">The seller's part it relates to.</param>
    /// <param name="breakdown">What is being credited, and how the tax splits.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CreditNote?> IssueAsync(
        ReturnRequest request,
        SubOrderReturnView order,
        RefundBreakdown breakdown,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(order);

        var existing = await context.CreditNotes
            .FirstOrDefaultAsync(note => note.ReturnId == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        // Nothing came back that anybody accepted. There is no supply to reverse, so there is no
        // note to raise — and raising a zero-value one would consume a number in a statutory series
        // for a document that says nothing.
        if (breakdown.GoodsTotal <= 0m)
        {
            NothingToCredit(logger, request.ReturnNumber);
            return null;
        }

        var vendorId = request.VendorId ?? order.VendorId;
        var vendor = await vendors.FindAsync(vendorId, cancellationToken).ConfigureAwait(false);
        var issuedAt = clock.UtcNow;

        var (series, number, financialYear) = await numbering
            .NextCreditNoteNumberAsync(vendor?.Code, vendorId, issuedAt, cancellationToken)
            .ConfigureAwait(false);

        var note = CreditNote.Issue(
            request.Id,
            request.OrderId,
            request.SubOrderId,
            vendorId,
            request.CustomerId,
            number,
            series,
            financialYear,
            issuedAt);

        note.Credits(order.InvoiceId, order.InvoiceNumber);

        var stateCode = order.PlaceOfSupplyStateId is { } stateId
            ? await reference.StateCodeAsync(stateId, cancellationToken).ConfigureAwait(false)
            : null;

        note.Tax(
            stateCode,
            order.IsIntraState,
            breakdown.TaxableValue,
            breakdown.Cgst,
            breakdown.Sgst,
            breakdown.Igst,
            breakdown.Cess,
            breakdown.GoodsTotal + breakdown.ShippingRefund,
            order.CurrencyCode);

        context.CreditNotes.Add(note);
        request.AttachCreditNote(note.Id);

        await RenderAsync(request, note, order, cancellationToken).ConfigureAwait(false);

        events.CreditNoteIssued(note);

        Issued(logger, note.CreditNoteNumber, request.ReturnNumber, note.Total);

        return note;
    }

    /// <summary>
    /// Renders the PDF and attaches it, or logs why it could not.
    /// </summary>
    /// <remarks>
    /// Every failure is swallowed deliberately. Rendering reaches a font file and object storage,
    /// and neither is a reason for a statutory document not to exist — an operator re-renders it
    /// from the note that is already in the database.
    /// </remarks>
    private async Task RenderAsync(
        ReturnRequest request,
        CreditNote note,
        SubOrderReturnView order,
        CancellationToken cancellationToken)
    {
        try
        {
            var legal = await settings.GetAsync<LegalSettings>(cancellationToken).ConfigureAwait(false);
            var support = await settings.GetAsync<SupportSettings>(cancellationToken).ConfigureAwait(false);

            var definition = CreditNoteDocumentBuilder.Build(request, note, order, legal, support);

            var file = await documents
                .RenderAsync(
                    definition,
                    $"credit-note-{note.CreditNoteNumber.Replace('/', '-')}.pdf",
                    ownerType: "credit-note",
                    note.Id,
                    cancellationToken)
                .ConfigureAwait(false);

            note.Attach(file.Id);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            NotRendered(logger, note.CreditNoteNumber, exception.Message);
        }
    }

    [LoggerMessage(EventId = 1720, Level = LogLevel.Information,
        Message = "Credit note {CreditNoteNumber} was raised for return {ReturnNumber}, for {Total}.")]
    private static partial void Issued(
        ILogger logger,
        string creditNoteNumber,
        string returnNumber,
        decimal total);

    [LoggerMessage(EventId = 1721, Level = LogLevel.Information,
        Message = "No credit note was raised for return {ReturnNumber}: nothing was accepted at inspection.")]
    private static partial void NothingToCredit(ILogger logger, string returnNumber);

    [LoggerMessage(EventId = 1722, Level = LogLevel.Error,
        Message = "Credit note {CreditNoteNumber} was raised but its document could not be rendered: {Detail}")]
    private static partial void NotRendered(ILogger logger, string creditNoteNumber, string detail);
}
