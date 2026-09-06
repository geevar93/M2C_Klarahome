using KlaraHome.Contracts.IntegrationEvents;

namespace KlaraHome.Contracts.Returns;

/// <summary>One line of a return, as a consumer outside the Returns module sees it.</summary>
/// <remarks>
/// Carried on the events rather than looked up, for the reason <c>OrderLineFact</c> is: Inventory
/// needs the offer and the quantity, Settlements needs the value, and neither may read
/// <c>returns.return_lines</c> to find them.
/// </remarks>
/// <param name="ReturnLineId">The return line.</param>
/// <param name="OrderLineId">The order line the units came from.</param>
/// <param name="ListingId">The offer sold, so stock can be put back against the right one.</param>
/// <param name="Sku">The stock-keeping unit, frozen at placement.</param>
/// <param name="Quantity">How many units this event concerns.</param>
/// <param name="RefundValue">What those units are worth back to the shopper, inclusive of tax.</param>
/// <param name="Disposition">
/// What QC decided to do with them: <c>Restock</c>, <c>Scrap</c>, <c>Quarantine</c>, or
/// <c>Pending</c> before QC has happened.
/// </param>
public sealed record ReturnLineFact(
    Guid ReturnLineId,
    Guid OrderLineId,
    Guid ListingId,
    string Sku,
    int Quantity,
    decimal RefundValue,
    string Disposition);

/// <summary>
/// A shopper asked to send something back (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// The first fact in the life of an RMA. Notifications acknowledges it, Reporting counts it against
/// the reason code, and nothing else acts on it — a request is not yet an obligation, and the units
/// are still the shopper's until somebody approves.
/// </remarks>
/// <param name="ReturnId">The RMA.</param>
/// <param name="ReturnNumber">Its number, which is what a shopper quotes to support.</param>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="SubOrderId">The seller's part the goods came from.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Type">Whether they want their money back or a replacement: <c>Return</c> or <c>Replacement</c>.</param>
/// <param name="ReasonCode">Why, as a code from the configured list.</param>
/// <param name="ReasonNote">Why, in the shopper's own words.</param>
/// <param name="EstimatedRefund">What it would come to if it were all approved, inclusive of tax.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="Lines">The units asked for.</param>
/// <param name="RequestedAt">When it was asked for.</param>
public sealed record ReturnRequested(
    Guid ReturnId,
    string ReturnNumber,
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    Guid VendorId,
    Guid CustomerId,
    string Type,
    string ReasonCode,
    string? ReasonNote,
    decimal EstimatedRefund,
    string CurrencyCode,
    IReadOnlyList<ReturnLineFact> Lines,
    DateTimeOffset RequestedAt) : IntegrationEvent;

/// <summary>
/// A return was agreed to, and the goods are now expected back.
/// </summary>
/// <remarks>
/// The event the platform actually runs on. A reverse pickup is booked from it, Notifications tells
/// the shopper what to hand over, and Settlements takes note that a seller's earning is provisional.
/// It is deliberately not the moment money moves: nothing is refunded before somebody has looked at
/// what came back, unless the policy on the reason code says the goods need not come back at all.
/// </remarks>
/// <param name="ReturnId">The RMA.</param>
/// <param name="ReturnNumber">Its number.</param>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Type">Return or replacement.</param>
/// <param name="IsPickupRequired">Whether a courier has to collect, or the shopper keeps the goods.</param>
/// <param name="ApprovedAmount">What was approved, inclusive of tax.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="Lines">The units approved.</param>
/// <param name="ApprovedAt">When it was approved.</param>
public sealed record ReturnApproved(
    Guid ReturnId,
    string ReturnNumber,
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    Guid VendorId,
    Guid CustomerId,
    string Type,
    bool IsPickupRequired,
    decimal ApprovedAmount,
    string CurrencyCode,
    IReadOnlyList<ReturnLineFact> Lines,
    DateTimeOffset ApprovedAt) : IntegrationEvent;

/// <summary>
/// A return was refused.
/// </summary>
/// <remarks>
/// Published so the shopper is told why by the same machinery that tells them everything else, and
/// so Reporting can count refusals per reason code — a reason refused nine times in ten is either a
/// policy nobody understands or a product that is mis-described.
/// </remarks>
/// <param name="ReturnId">The RMA.</param>
/// <param name="ReturnNumber">Its number.</param>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Reason">Why it was refused, in words the shopper is shown.</param>
/// <param name="RejectedAt">When it was refused.</param>
public sealed record ReturnRejected(
    Guid ReturnId,
    string ReturnNumber,
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    Guid VendorId,
    Guid CustomerId,
    string? Reason,
    DateTimeOffset RejectedAt) : IntegrationEvent;

/// <summary>
/// The goods came back.
/// </summary>
/// <remarks>
/// The warehouse has the parcel and has not yet said whether what is in it is what was supposed to
/// be. Nothing is restocked and nothing is refunded on this event — it exists so a shopper can be
/// told their return has arrived, which is the commonest support question an RMA generates.
/// </remarks>
/// <param name="ReturnId">The RMA.</param>
/// <param name="ReturnNumber">Its number.</param>
/// <param name="OrderId">The order.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="PickupAwb">The reverse waybill it came back on, when there was one.</param>
/// <param name="ReceivedAt">When it was booked in.</param>
public sealed record ReturnReceived(
    Guid ReturnId,
    string ReturnNumber,
    Guid OrderId,
    Guid SubOrderId,
    Guid VendorId,
    Guid CustomerId,
    string? PickupAwb,
    DateTimeOffset ReceivedAt) : IntegrationEvent;

/// <summary>
/// Quality control looked at what came back and decided.
/// </summary>
/// <remarks>
/// <para>
/// The event that moves goods. Inventory acts on the per-line disposition — a <c>Restock</c> line
/// goes back on sale, a <c>Scrap</c> line is written off, a <c>Quarantine</c> line is neither until
/// somebody decides — and Settlements reverses the seller's earning on the value that passed.
/// </para>
/// <para>
/// A failed QC is published too, carrying <see cref="Passed"/> false with the same lines. A return
/// whose goods came back damaged still moved goods; it simply did not earn a refund, and a consumer
/// that only ever heard about passes would have no way to tell a scrapped unit from a unit that
/// never arrived.
/// </para>
/// </remarks>
/// <param name="ReturnId">The RMA.</param>
/// <param name="ReturnNumber">Its number.</param>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Passed">Whether the goods were as they should have been.</param>
/// <param name="Notes">What the inspector wrote.</param>
/// <param name="RefundableAmount">What passed QC is worth back, inclusive of tax.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="Lines">The units, each with the disposition decided for it.</param>
/// <param name="InspectedAt">When it was inspected.</param>
public sealed record ReturnQcCompleted(
    Guid ReturnId,
    string ReturnNumber,
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    Guid VendorId,
    Guid CustomerId,
    bool Passed,
    string? Notes,
    decimal RefundableAmount,
    string CurrencyCode,
    IReadOnlyList<ReturnLineFact> Lines,
    DateTimeOffset InspectedAt) : IntegrationEvent;

/// <summary>
/// The RMA is finished: refunded, replaced, or closed with nothing owed.
/// </summary>
/// <remarks>
/// The last fact in the life of a return, and the one Reporting measures cycle time against.
/// <see cref="Outcome"/> rather than a boolean, because "refunded", "replaced" and "closed with no
/// refund" are three different things to a shopper and to an accountant.
/// </remarks>
/// <param name="ReturnId">The RMA.</param>
/// <param name="ReturnNumber">Its number.</param>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Outcome">How it ended: <c>Refunded</c>, <c>Replaced</c>, <c>Rejected</c> or <c>Closed</c>.</param>
/// <param name="RefundedAmount">What went back, inclusive of tax. Zero where nothing did.</param>
/// <param name="RefundMode">Where it went: <c>Original</c>, <c>Wallet</c>, or null where nothing did.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="ClosedAt">When it closed.</param>
public sealed record ReturnClosed(
    Guid ReturnId,
    string ReturnNumber,
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    Guid VendorId,
    Guid CustomerId,
    string Outcome,
    decimal RefundedAmount,
    string? RefundMode,
    string CurrencyCode,
    DateTimeOffset ClosedAt) : IntegrationEvent;

/// <summary>
/// A credit note was raised against a seller's tax invoice.
/// </summary>
/// <remarks>
/// The statutory counterpart of <c>Orders.InvoiceIssued</c>, and a separate fact from the refund:
/// under section 34 of the CGST Act a seller's output tax is only reduced by a credit note carrying
/// its own gapless number, and money going back to a shopper's card does not do that on its own.
/// Settlements reverses the seller's supply value on this event; Notifications attaches the
/// document.
/// </remarks>
/// <param name="CreditNoteId">The credit note.</param>
/// <param name="CreditNoteNumber">Its gapless number, unique per seller per financial year.</param>
/// <param name="ReturnId">The RMA that caused it.</param>
/// <param name="OrderId">The order.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="InvoiceId">The tax invoice it credits, when one is known.</param>
/// <param name="VendorId">The seller whose GSTIN it is raised under.</param>
/// <param name="CustomerId">The shopper it is issued to.</param>
/// <param name="FinancialYear">The Indian financial year it belongs to, as <c>2026-27</c>.</param>
/// <param name="TaxableValue">What the tax was computed on.</param>
/// <param name="TaxAmount">The tax being credited back, all heads together.</param>
/// <param name="Total">The credit note total, inclusive of tax.</param>
/// <param name="CurrencyCode">ISO 4217 code the amounts are in.</param>
/// <param name="FileId">The stored PDF, or null when rendering was unavailable.</param>
/// <param name="IssuedAt">When it was raised.</param>
public sealed record CreditNoteIssued(
    Guid CreditNoteId,
    string CreditNoteNumber,
    Guid ReturnId,
    Guid OrderId,
    Guid SubOrderId,
    Guid? InvoiceId,
    Guid VendorId,
    Guid CustomerId,
    string FinancialYear,
    decimal TaxableValue,
    decimal TaxAmount,
    decimal Total,
    string CurrencyCode,
    Guid? FileId,
    DateTimeOffset IssuedAt) : IntegrationEvent;
