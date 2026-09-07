using KlaraHome.Modules.Returns.Domain;

namespace KlaraHome.Modules.Returns.Application;

/// <summary>One line of a return, as the API states it.</summary>
/// <param name="Id">The return line.</param>
/// <param name="OrderLineId">The order line the units came from.</param>
/// <param name="ListingId">The offer sold.</param>
/// <param name="Sku">The stock-keeping unit.</param>
/// <param name="Name">What it is called, frozen when the return was raised.</param>
/// <param name="ImageFileId">The picture, for the shopper's screen.</param>
/// <param name="Quantity">How many units were asked for.</param>
/// <param name="QuantityAccepted">How many quality control accepted.</param>
/// <param name="RefundAmount">What the units asked for are worth back, inclusive of tax.</param>
/// <param name="AcceptedRefund">What was accepted is worth back, inclusive of tax.</param>
/// <param name="Disposition">What became of them.</param>
/// <param name="QcNote">What the inspector wrote about this line.</param>
internal sealed record ReturnLineResponse(
    Guid Id,
    Guid OrderLineId,
    Guid ListingId,
    string Sku,
    string Name,
    Guid? ImageFileId,
    int Quantity,
    int QuantityAccepted,
    decimal RefundAmount,
    decimal AcceptedRefund,
    ReturnDisposition Disposition,
    string? QcNote);

/// <summary>A return, as the API states it.</summary>
/// <param name="Id">The RMA.</param>
/// <param name="ReturnNumber">Its number, which is what a shopper quotes.</param>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="SubOrderNumber">Its number.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="Type">Money back, or a replacement.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="ReasonCode">Why, as a code.</param>
/// <param name="ReasonNote">Why, in the shopper's own words.</param>
/// <param name="EvidenceFileIds">The photographs attached.</param>
/// <param name="EstimatedRefund">What the shopper was quoted.</param>
/// <param name="ApprovedAmount">What was agreed to.</param>
/// <param name="RefundAmount">What actually went back.</param>
/// <param name="ReturnShippingFee">What the shopper was charged for the collection.</param>
/// <param name="ShippingRefundAmount">The delivery charge going back too, when it is.</param>
/// <param name="RefundMode">Where the money went.</param>
/// <param name="CurrencyCode">ISO 4217 code every amount is in.</param>
/// <param name="IsPickupRequired">Whether a courier collects.</param>
/// <param name="PickupAwb">The waybill it is travelling back on.</param>
/// <param name="PickupScheduledFor">When the courier is due to call.</param>
/// <param name="QcPassed">Whether it passed inspection. Null until it has been inspected.</param>
/// <param name="QcNotes">What the inspector wrote.</param>
/// <param name="RejectedReason">Why it was refused.</param>
/// <param name="CustomerId">Who asked. The back office needs it to raise the replacement order
/// against the same shopper.</param>
/// <param name="CreditNoteId">The credit note raised for it.</param>
/// <param name="ReplacementOrderId">The order sent out in its place, once one has been raised.</param>
/// <param name="RequestedAt">When the shopper asked.</param>
/// <param name="ApprovedAt">When it was approved.</param>
/// <param name="ReceivedAt">When the warehouse booked it in.</param>
/// <param name="RefundedAt">When the money went back.</param>
/// <param name="ClosedAt">When it finished.</param>
/// <param name="Lines">What is on it.</param>
/// <param name="NextStatuses">
/// What the caller may move it to from here. Read straight off the transition table, so an admin
/// screen never draws a button for an edge that does not exist.
/// </param>
internal sealed record ReturnResponse(
    Guid Id,
    string ReturnNumber,
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    string SubOrderNumber,
    Guid? VendorId,
    string Type,
    string Status,
    string ReasonCode,
    string? ReasonNote,
    IReadOnlyList<Guid> EvidenceFileIds,
    decimal EstimatedRefund,
    decimal ApprovedAmount,
    decimal RefundAmount,
    decimal ReturnShippingFee,
    decimal ShippingRefundAmount,
    string? RefundMode,
    string CurrencyCode,
    bool IsPickupRequired,
    string? PickupAwb,
    DateTimeOffset? PickupScheduledFor,
    bool? QcPassed,
    string? QcNotes,
    string? RejectedReason,
    Guid CustomerId,
    Guid? CreditNoteId,
    Guid? ReplacementOrderId,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? RefundedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<ReturnLineResponse> Lines,
    IReadOnlyList<string> NextStatuses);

/// <summary>A return in a list, without its lines.</summary>
/// <param name="Id">The RMA.</param>
/// <param name="ReturnNumber">Its number.</param>
/// <param name="OrderNumber">The order's number.</param>
/// <param name="SubOrderNumber">The seller's part's number.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="Type">Money back, or a replacement.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="ReasonCode">Why.</param>
/// <param name="EstimatedRefund">What the shopper was quoted.</param>
/// <param name="RefundAmount">What actually went back.</param>
/// <param name="CurrencyCode">ISO 4217 code the amounts are in.</param>
/// <param name="Units">How many units it covers.</param>
/// <param name="RequestedAt">When the shopper asked.</param>
internal sealed record ReturnSummaryResponse(
    Guid Id,
    string ReturnNumber,
    string OrderNumber,
    string SubOrderNumber,
    Guid? VendorId,
    string Type,
    string Status,
    string ReasonCode,
    decimal EstimatedRefund,
    decimal RefundAmount,
    string CurrencyCode,
    int Units,
    DateTimeOffset RequestedAt);

/// <summary>A reason a shopper may give, as the API states it.</summary>
/// <param name="Id">The reason.</param>
/// <param name="Code">Its stable code.</param>
/// <param name="Label">What the shopper reads.</param>
/// <param name="Description">The help beneath it.</param>
/// <param name="IsActive">Whether it is offered.</param>
/// <param name="SortOrder">Where it sits on the dropdown.</param>
/// <param name="RequiresEvidence">Whether a photograph is needed.</param>
/// <param name="IsPickupRequired">Whether a courier collects.</param>
/// <param name="RequiresQc">Whether the goods are inspected.</param>
/// <param name="IsAutoApproved">Whether it is approved without a human.</param>
/// <param name="ShippingPayer">Who pays the reverse freight.</param>
/// <param name="IsVendorFault">Whether the seller bears the cost at settlement.</param>
/// <param name="AllowsReplacement">Whether a replacement may be asked for.</param>
internal sealed record ReturnReasonResponse(
    Guid Id,
    string Code,
    string Label,
    string? Description,
    bool IsActive,
    int SortOrder,
    bool RequiresEvidence,
    bool IsPickupRequired,
    bool RequiresQc,
    bool IsAutoApproved,
    string ShippingPayer,
    bool IsVendorFault,
    bool AllowsReplacement);

/// <summary>
/// A reason as a shopper sees it, which is a good deal less than an operator does.
/// </summary>
/// <remarks>
/// Whether a reason is auto-approved, whether the seller is charged for it and whether the goods are
/// inspected are all rules a shopper could game if they could read them. What they need is the label,
/// whether they must attach a photograph, and whether somebody is coming to collect.
/// </remarks>
/// <param name="Code">Its stable code, which is what the request names.</param>
/// <param name="Label">What the shopper reads.</param>
/// <param name="Description">The help beneath it.</param>
/// <param name="RequiresEvidence">Whether they must attach a photograph.</param>
/// <param name="IsPickupRequired">Whether somebody will collect the goods.</param>
/// <param name="AllowsReplacement">Whether they may ask for a replacement instead.</param>
internal sealed record ReturnReasonOption(
    string Code,
    string Label,
    string? Description,
    bool RequiresEvidence,
    bool IsPickupRequired,
    bool AllowsReplacement);

/// <summary>One line a shopper could send back, and what it is worth.</summary>
/// <param name="OrderLineId">The line.</param>
/// <param name="Sku">The stock-keeping unit.</param>
/// <param name="Name">What it is called.</param>
/// <param name="ImageFileId">The picture.</param>
/// <param name="QuantityReturnable">How many units may still be sent back.</param>
/// <param name="UnitPrice">What one unit cost, inclusive of tax.</param>
/// <param name="EstimatedRefund">What all the returnable units would come to, inclusive of tax.</param>
/// <param name="IsReturnable">Whether it may be sent back at all.</param>
/// <param name="Reason">Why not, when it may not.</param>
internal sealed record ReturnableLineResponse(
    Guid OrderLineId,
    string Sku,
    string Name,
    Guid? ImageFileId,
    int QuantityReturnable,
    decimal UnitPrice,
    decimal EstimatedRefund,
    bool IsReturnable,
    string? Reason);

/// <summary>What a shopper may send back from one seller's part, and until when.</summary>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="SubOrderNumber">Its number.</param>
/// <param name="OrderNumber">The order's number.</param>
/// <param name="IsEligible">Whether anything at all may be sent back.</param>
/// <param name="WindowClosesAt">When the window closes.</param>
/// <param name="WindowSource">Which policy decided the window: product, vendor or store.</param>
/// <param name="Reason">Why nothing may be sent back, when nothing may.</param>
/// <param name="CurrencyCode">ISO 4217 code the amounts are in.</param>
/// <param name="Lines">The lines, each with what is left of it.</param>
/// <param name="Reasons">The reasons a shopper may give.</param>
internal sealed record ReturnEligibilityResponse(
    Guid SubOrderId,
    string SubOrderNumber,
    string OrderNumber,
    bool IsEligible,
    DateTimeOffset? WindowClosesAt,
    string WindowSource,
    string? Reason,
    string CurrencyCode,
    IReadOnlyList<ReturnableLineResponse> Lines,
    IReadOnlyList<ReturnReasonOption> Reasons);

/// <summary>A credit note, as the API states it.</summary>
/// <param name="Id">The credit note.</param>
/// <param name="CreditNoteNumber">Its gapless number.</param>
/// <param name="ReturnId">The RMA that caused it.</param>
/// <param name="OrderId">The order.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="InvoiceNumber">The invoice it credits.</param>
/// <param name="FinancialYear">The financial year it belongs to.</param>
/// <param name="TaxableValue">What the tax was computed on.</param>
/// <param name="Cgst">Central GST credited.</param>
/// <param name="Sgst">State GST credited.</param>
/// <param name="Igst">Integrated GST credited.</param>
/// <param name="Cess">Compensation cess credited.</param>
/// <param name="Total">The total, inclusive of tax.</param>
/// <param name="CurrencyCode">ISO 4217 code the amounts are in.</param>
/// <param name="FileId">The stored PDF.</param>
/// <param name="IssuedAt">When it was raised.</param>
internal sealed record CreditNoteResponse(
    Guid Id,
    string CreditNoteNumber,
    Guid ReturnId,
    Guid OrderId,
    Guid SubOrderId,
    Guid? VendorId,
    string? InvoiceNumber,
    string FinancialYear,
    decimal TaxableValue,
    decimal Cgst,
    decimal Sgst,
    decimal Igst,
    decimal Cess,
    decimal Total,
    string CurrencyCode,
    Guid? FileId,
    DateTimeOffset IssuedAt);

/// <summary>
/// Turns this module's aggregates into the shapes the API returns.
/// </summary>
/// <remarks>
/// One place, so two endpoints cannot answer the same question with two different shapes. The
/// enums are spelled with <c>ToString</c> rather than as numbers for the reason every other module
/// does it: a number in a payload is a number a frontend hard-codes, and inserting a state into the
/// lifecycle would silently change what every stored value meant.
/// </remarks>
internal static class ReturnProjection
{
    /// <summary>Projects a return in full.</summary>
    /// <param name="request">The RMA.</param>
    /// <param name="actor">Who is asking, which decides the buttons.</param>
    public static ReturnResponse ToResponse(ReturnRequest request, ReturnActor actor)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ReturnResponse(
            request.Id,
            request.ReturnNumber,
            request.OrderId,
            request.OrderNumber,
            request.SubOrderId,
            request.SubOrderNumber,
            request.VendorId,
            request.Type.ToString(),
            request.Status.ToString(),
            request.ReasonCode,
            request.ReasonNote,
            request.EvidenceFileIds,
            request.EstimatedRefund,
            request.ApprovedAmount,
            request.RefundAmount,
            request.ReturnShippingFee,
            request.ShippingRefundAmount,
            request.RefundMode?.ToString(),
            request.CurrencyCode,
            request.IsPickupRequired,
            request.PickupAwb,
            request.PickupScheduledFor,
            request.QcPassed,
            request.QcNotes,
            request.RejectedReason,
            request.CustomerId,
            request.CreditNoteId,
            request.ReplacementOrderId,
            request.RequestedAt,
            request.ApprovedAt,
            request.ReceivedAt,
            request.RefundedAt,
            request.ClosedAt,
            [.. request.Lines.Select(ToLine)],
            [.. ReturnLifecycle.NextFor(request.Status, actor).Select(status => status.ToString())]);
    }

    /// <summary>Projects a return for a list.</summary>
    /// <param name="request">The RMA.</param>
    public static ReturnSummaryResponse ToSummary(ReturnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ReturnSummaryResponse(
            request.Id,
            request.ReturnNumber,
            request.OrderNumber,
            request.SubOrderNumber,
            request.VendorId,
            request.Type.ToString(),
            request.Status.ToString(),
            request.ReasonCode,
            request.EstimatedRefund,
            request.RefundAmount,
            request.CurrencyCode,
            request.TotalQuantity,
            request.RequestedAt);
    }

    /// <summary>Projects one line.</summary>
    /// <param name="line">The return line.</param>
    public static ReturnLineResponse ToLine(ReturnLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new ReturnLineResponse(
            line.Id,
            line.OrderLineId,
            line.ListingId,
            line.Sku,
            line.Snapshot.Name,
            line.Snapshot.ImageFileId,
            line.Quantity,
            line.QuantityAccepted,
            line.RefundAmount,
            line.AcceptedRefund,
            line.Disposition,
            line.QcNote);
    }

    /// <summary>Projects a reason for an operator.</summary>
    /// <param name="reason">The reason.</param>
    public static ReturnReasonResponse ToReason(ReturnReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);

        return new ReturnReasonResponse(
            reason.Id,
            reason.Code,
            reason.Label,
            reason.Description,
            reason.IsActive,
            reason.SortOrder,
            reason.RequiresEvidence,
            reason.IsPickupRequired,
            reason.RequiresQc,
            reason.IsAutoApproved,
            reason.ShippingPayer.ToString(),
            reason.IsVendorFault,
            reason.AllowsReplacement);
    }

    /// <summary>Projects a reason for a shopper, which is a good deal less.</summary>
    /// <param name="reason">The reason.</param>
    public static ReturnReasonOption ToOption(ReturnReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);

        return new ReturnReasonOption(
            reason.Code,
            reason.Label,
            reason.Description,
            reason.RequiresEvidence,
            reason.IsPickupRequired,
            reason.AllowsReplacement);
    }

    /// <summary>Projects a credit note.</summary>
    /// <param name="note">The credit note.</param>
    public static CreditNoteResponse ToCreditNote(CreditNote note)
    {
        ArgumentNullException.ThrowIfNull(note);

        return new CreditNoteResponse(
            note.Id,
            note.CreditNoteNumber,
            note.ReturnId,
            note.OrderId,
            note.SubOrderId,
            note.VendorId,
            note.InvoiceNumber,
            note.FinancialYear,
            note.TaxableValue,
            note.Cgst,
            note.Sgst,
            note.Igst,
            note.Cess,
            note.Total,
            note.CurrencyCode,
            note.FileId,
            note.IssuedAt);
    }
}
