using KlaraHome.Contracts.IntegrationEvents;

namespace KlaraHome.Contracts.Orders;

/// <summary>One line of an order, as a consumer outside the Orders module sees it.</summary>
/// <remarks>
/// Carried on the events rather than looked up, because every consumer that reacts to a sub-order
/// needs the same few facts — which offer, how many, and what they were worth — and none of them
/// may read <c>orders.order_lines</c> to find them.
/// </remarks>
/// <param name="OrderLineId">The line.</param>
/// <param name="ListingId">The offer sold.</param>
/// <param name="Sku">The stock-keeping unit, frozen at placement.</param>
/// <param name="Quantity">Units this event concerns — the whole line, or the part being cancelled.</param>
/// <param name="LineTotal">What the shopper pays for those units, inclusive of tax.</param>
public sealed record OrderLineFact(
    Guid OrderLineId,
    Guid ListingId,
    string Sku,
    int Quantity,
    decimal LineTotal);

/// <summary>
/// An order was created (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// The first fact in the life of a sale. Payments starts a collection from it, Notifications sends
/// the confirmation, and Reporting counts it. It carries the totals rather than the lines: a
/// consumer that needs the lines is reacting to a <em>sub-order</em>, which is where a marketplace
/// keeps them.
/// </para>
/// <para>
/// Published from the outbox in the transaction that wrote the order, so a consumer reacting to it
/// is reacting to an order that certainly exists.
/// </para>
/// </remarks>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">The human-readable number a shopper quotes to support.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="CartId">The basket it came from, which is also the stock-reservation reference.</param>
/// <param name="Status">Where the order starts: awaiting payment, or already confirmed for COD.</param>
/// <param name="PaymentMethod">Prepaid or cash on delivery.</param>
/// <param name="GrandTotal">What the shopper agreed to pay.</param>
/// <param name="AmountPayable">The grand total less any store credit: what a gateway is asked for.</param>
/// <param name="CurrencyCode">ISO 4217 code both amounts are in.</param>
/// <param name="VendorIds">The sellers the order split across, one sub-order each.</param>
/// <param name="PlacedAt">When it was placed.</param>
public sealed record OrderPlaced(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    Guid CartId,
    string Status,
    string PaymentMethod,
    decimal GrandTotal,
    decimal AmountPayable,
    string CurrencyCode,
    IReadOnlyList<Guid> VendorIds,
    DateTimeOffset PlacedAt) : IntegrationEvent;

/// <summary>
/// One seller's part of an order was confirmed and is theirs to fulfil.
/// </summary>
/// <remarks>
/// The event a marketplace actually runs on: Shipping creates a shipment from it, Settlements opens
/// the seller's entry, and Notifications tells them there is something to pack. It is per sub-order
/// rather than per order because two sellers fulfil independently and are told independently.
/// </remarks>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="SubOrderNumber">Its number, which is what appears on the seller's worklist.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Total">What this seller's part is worth, inclusive of tax.</param>
/// <param name="CurrencyCode">ISO 4217 code the total is in.</param>
/// <param name="DispatchDueAt">When the seller must have handed the parcel over.</param>
/// <param name="Lines">What is in it.</param>
public sealed record SubOrderConfirmed(
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    string SubOrderNumber,
    Guid VendorId,
    Guid CustomerId,
    decimal Total,
    string CurrencyCode,
    DateTimeOffset? DispatchDueAt,
    IReadOnlyList<OrderLineFact> Lines) : IntegrationEvent;

/// <summary>
/// A seller's part of an order was cancelled, in whole or in part.
/// </summary>
/// <remarks>
/// <para>
/// Inventory puts the units back on sale, Payments refunds what was collected for them, Settlements
/// reverses the seller's entry, and Notifications tells the shopper. <see cref="Lines"/> carries the
/// quantities <em>this</em> cancellation covers, which is what makes a partial cancellation
/// expressible: three of five units going back is three units of movement, not five.
/// </para>
/// <para>
/// <see cref="WasConfirmed"/> is the flag that decides whether there is stock to give back at all. A
/// sub-order cancelled before it was ever confirmed still holds its units as a <em>reservation</em>
/// against the cart, which Orders releases directly; one cancelled afterwards has already had them
/// committed out of stock, and only a restock puts them back.
/// </para>
/// </remarks>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="SubOrderNumber">Its number.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="InitiatedBy">Who cancelled it: <c>customer</c>, <c>vendor</c>, <c>platform</c> or <c>system</c>.</param>
/// <param name="Reason">Why, in the words the shopper was shown.</param>
/// <param name="IsPartial">Whether some of the sub-order survives.</param>
/// <param name="WasConfirmed">Whether the stock behind it had already been committed out of supply.</param>
/// <param name="CancelledTotal">What is coming off the bill, inclusive of tax.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="Lines">The units this cancellation covers.</param>
public sealed record SubOrderCancelled(
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    string SubOrderNumber,
    Guid VendorId,
    Guid CustomerId,
    string InitiatedBy,
    string? Reason,
    bool IsPartial,
    bool WasConfirmed,
    decimal CancelledTotal,
    string CurrencyCode,
    IReadOnlyList<OrderLineFact> Lines) : IntegrationEvent;

/// <summary>
/// A seller's part of an order moved to a new state.
/// </summary>
/// <remarks>
/// The general timeline event, published on every transition the machine allows — including the
/// ones that have an event of their own. A consumer that cares about one specific moment listens
/// for the specific event; Reporting and Notifications listen for this one and switch on the state,
/// which is what stops a new state in the machine needing a new contract.
/// </remarks>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="SubOrderNumber">Its number.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="FromStatus">Where it was.</param>
/// <param name="ToStatus">Where it is now.</param>
/// <param name="OrderStatus">What the parent order's derived status became.</param>
/// <param name="ActorType">Who moved it: <c>customer</c>, <c>vendor</c>, <c>platform</c> or <c>system</c>.</param>
/// <param name="Reason">Why, when a reason was given.</param>
public sealed record SubOrderStatusChanged(
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    string SubOrderNumber,
    Guid VendorId,
    Guid CustomerId,
    string FromStatus,
    string ToStatus,
    string OrderStatus,
    string ActorType,
    string? Reason) : IntegrationEvent;

/// <summary>
/// A tax invoice was raised against one seller's part of an order.
/// </summary>
/// <remarks>
/// Notifications attaches it to the dispatch message, Settlements takes the taxable value from it,
/// and Reporting counts turnover on it rather than on the order — an order that was never invoiced
/// is not a supply.
/// </remarks>
/// <param name="OrderId">The order.</param>
/// <param name="SubOrderId">The seller's part it covers.</param>
/// <param name="VendorId">The seller whose GSTIN it is raised under.</param>
/// <param name="CustomerId">The shopper it is billed to.</param>
/// <param name="InvoiceId">The invoice.</param>
/// <param name="InvoiceNumber">Its gapless number, unique per seller per financial year.</param>
/// <param name="FinancialYear">The Indian financial year it belongs to, as <c>2026-27</c>.</param>
/// <param name="TaxableValue">What the tax was computed on.</param>
/// <param name="Total">The invoice total, inclusive of tax.</param>
/// <param name="CurrencyCode">ISO 4217 code both amounts are in.</param>
/// <param name="FileId">The stored PDF, or null when rendering was unavailable.</param>
/// <param name="IssuedAt">When it was raised.</param>
public sealed record InvoiceIssued(
    Guid OrderId,
    Guid SubOrderId,
    Guid VendorId,
    Guid CustomerId,
    Guid InvoiceId,
    string InvoiceNumber,
    string FinancialYear,
    decimal TaxableValue,
    decimal Total,
    string CurrencyCode,
    Guid? FileId,
    DateTimeOffset IssuedAt) : IntegrationEvent;

/// <summary>
/// An order reached the end of its life with every seller's part accounted for.
/// </summary>
/// <remarks>
/// Raised when the return window has closed on the last delivered sub-order, which is the moment
/// settlement becomes payable and loyalty becomes earnable (docs/02-domain-model.md §5.1). It is
/// deliberately not raised at delivery: a delivered order can still become a return.
/// </remarks>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="GrandTotal">What was agreed.</param>
/// <param name="CurrencyCode">ISO 4217 code the total is in.</param>
/// <param name="CompletedAt">When it completed.</param>
public sealed record OrderCompleted(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    decimal GrandTotal,
    string CurrencyCode,
    DateTimeOffset CompletedAt) : IntegrationEvent;
