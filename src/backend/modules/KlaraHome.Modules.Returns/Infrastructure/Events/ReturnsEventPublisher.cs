using KlaraHome.Contracts.Returns;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Returns.Domain;
using KlaraHome.Modules.Returns.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Returns.Infrastructure.Events;

/// <summary>
/// Announces what happened to a return (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// Everything goes through the keyed outbox bound to this module's context, so an event is written
/// in the same transaction as the fact it describes. A consumer reacting to a QC pass is reacting to
/// a QC pass that certainly happened — which is what lets Inventory move stock on it without asking
/// this module to confirm.
/// </para>
/// <para>
/// The line facts are built here rather than by the callers, so every event that carries lines
/// carries them in the same shape. The disposition on them is the aggregate's own word for it,
/// spelled as a string because the contract may not take this module's enum.
/// </para>
/// </remarks>
/// <param name="outbox">The outbox bound to the Returns context.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ReturnsEventPublisher(
    [FromKeyedServices(typeof(ReturnsDbContext))] IOutbox outbox,
    IClock clock)
{
    /// <summary>Announces that a shopper asked to send something back.</summary>
    /// <param name="request">The RMA.</param>
    public void Requested(ReturnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        outbox.Enqueue(new ReturnRequested(
            request.Id,
            request.ReturnNumber,
            request.OrderId,
            request.OrderNumber,
            request.SubOrderId,
            request.VendorId ?? Guid.Empty,
            request.CustomerId,
            request.Type.ToString(),
            request.ReasonCode,
            request.ReasonNote,
            request.EstimatedRefund,
            request.CurrencyCode,
            Facts(request),
            request.RequestedAt));
    }

    /// <summary>Announces that a return was agreed to.</summary>
    /// <param name="request">The RMA.</param>
    public void Approved(ReturnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        outbox.Enqueue(new ReturnApproved(
            request.Id,
            request.ReturnNumber,
            request.OrderId,
            request.OrderNumber,
            request.SubOrderId,
            request.VendorId ?? Guid.Empty,
            request.CustomerId,
            request.Type.ToString(),
            request.IsPickupRequired,
            request.ApprovedAmount,
            request.CurrencyCode,
            Facts(request),
            request.ApprovedAt ?? clock.UtcNow));
    }

    /// <summary>Announces that a return was refused.</summary>
    /// <param name="request">The RMA.</param>
    public void Rejected(ReturnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        outbox.Enqueue(new ReturnRejected(
            request.Id,
            request.ReturnNumber,
            request.OrderId,
            request.OrderNumber,
            request.SubOrderId,
            request.VendorId ?? Guid.Empty,
            request.CustomerId,
            request.RejectedReason,
            request.ClosedAt ?? clock.UtcNow));
    }

    /// <summary>Announces that the goods came back.</summary>
    /// <param name="request">The RMA.</param>
    public void Received(ReturnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        outbox.Enqueue(new ReturnReceived(
            request.Id,
            request.ReturnNumber,
            request.OrderId,
            request.SubOrderId,
            request.VendorId ?? Guid.Empty,
            request.CustomerId,
            request.PickupAwb,
            request.ReceivedAt ?? clock.UtcNow));
    }

    /// <summary>Announces what quality control decided. The event that moves goods.</summary>
    /// <param name="request">The RMA.</param>
    /// <param name="refundable">What passed is worth back, inclusive of tax.</param>
    public void QcCompleted(ReturnRequest request, decimal refundable)
    {
        ArgumentNullException.ThrowIfNull(request);

        outbox.Enqueue(new ReturnQcCompleted(
            request.Id,
            request.ReturnNumber,
            request.OrderId,
            request.OrderNumber,
            request.SubOrderId,
            request.VendorId ?? Guid.Empty,
            request.CustomerId,
            request.QcPassed ?? false,
            request.QcNotes,
            refundable,
            request.CurrencyCode,
            Facts(request, acceptedOnly: true),
            request.InspectedAt ?? clock.UtcNow));
    }

    /// <summary>Announces that the return is finished.</summary>
    /// <param name="request">The RMA.</param>
    /// <param name="outcome">How it ended.</param>
    public void Closed(ReturnRequest request, string outcome)
    {
        ArgumentNullException.ThrowIfNull(request);

        outbox.Enqueue(new ReturnClosed(
            request.Id,
            request.ReturnNumber,
            request.OrderId,
            request.OrderNumber,
            request.SubOrderId,
            request.VendorId ?? Guid.Empty,
            request.CustomerId,
            outcome,
            request.RefundAmount,
            request.RefundMode?.ToString(),
            request.CurrencyCode,
            request.ClosedAt ?? clock.UtcNow));
    }

    /// <summary>Announces the credit note that reduces the seller's output tax.</summary>
    /// <param name="note">The credit note.</param>
    public void CreditNoteIssued(CreditNote note)
    {
        ArgumentNullException.ThrowIfNull(note);

        outbox.Enqueue(new CreditNoteIssued(
            note.Id,
            note.CreditNoteNumber,
            note.ReturnId,
            note.OrderId,
            note.SubOrderId,
            note.InvoiceId,
            note.VendorId ?? Guid.Empty,
            note.CustomerId,
            note.FinancialYear,
            note.TaxableValue,
            note.TaxAmount,
            note.Total,
            note.CurrencyCode,
            note.FileId,
            note.IssuedAt));
    }

    /// <summary>
    /// The lines, in the shape a consumer outside this module reads them.
    /// </summary>
    /// <param name="request">The RMA.</param>
    /// <param name="acceptedOnly">
    /// Whether to carry what quality control accepted rather than what was asked for. True on the QC
    /// event, because Inventory must move the units that actually arrived and not the ones a shopper
    /// said they were sending.
    /// </param>
    private static IReadOnlyList<ReturnLineFact> Facts(ReturnRequest request, bool acceptedOnly = false)
        =>
        [
            .. request.Lines
                .Select(line => new ReturnLineFact(
                    line.Id,
                    line.OrderLineId,
                    line.ListingId,
                    line.Sku,
                    acceptedOnly ? line.QuantityAccepted : line.Quantity,
                    acceptedOnly ? line.AcceptedRefund : line.RefundAmount,
                    line.Disposition.ToString()))
                .Where(fact => fact.Quantity > 0),
        ];
}
