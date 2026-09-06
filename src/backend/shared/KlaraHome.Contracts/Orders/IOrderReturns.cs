using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Contracts.Orders;

/// <summary>
/// One line of a delivered sub-order, as the module that handles returns needs it.
/// </summary>
/// <remarks>
/// <para>
/// The tax split is carried rather than recomputed, and that is the whole reason this record exists
/// separately from <see cref="FulfilmentLine"/>. A refund's tax has to be the tax that was charged:
/// re-deriving it from today's GST rate would credit a different figure from the one on the
/// invoice, and the credit note under section 34 of the CGST Act must agree with the invoice it
/// credits.
/// </para>
/// <para>
/// Everything here is per <em>line</em>, not per unit. A partial return apportions it by quantity,
/// which is the Returns module's arithmetic and not this seam's.
/// </para>
/// </remarks>
/// <param name="OrderLineId">The line.</param>
/// <param name="ListingId">The offer sold, which is what stock goes back against.</param>
/// <param name="VariantId">The variant, for the return screen's picture and title.</param>
/// <param name="Sku">The stock-keeping unit, frozen at placement.</param>
/// <param name="Name">What it is called, frozen at placement.</param>
/// <param name="ImageFileId">The picture frozen at placement, for the shopper's return screen.</param>
/// <param name="HsnCode">The HSN the tax was charged under, which the credit note repeats.</param>
/// <param name="Quantity">How many were ordered.</param>
/// <param name="QuantityCancelled">How many were cancelled before delivery.</param>
/// <param name="QuantityReturned">How many have already been returned.</param>
/// <param name="UnitPrice">What one unit cost, inclusive of tax.</param>
/// <param name="TaxableValue">What the tax was computed on, for the whole line.</param>
/// <param name="GstRate">The rate charged, as a percentage.</param>
/// <param name="Cgst">Central GST charged on the line.</param>
/// <param name="Sgst">State GST charged on the line.</param>
/// <param name="Igst">Integrated GST charged on the line.</param>
/// <param name="Cess">Compensation cess charged on the line.</param>
/// <param name="LineTotal">What the shopper paid for the line, inclusive of tax.</param>
/// <param name="IsReturnable">Whether the product allowed returns at all, frozen at placement.</param>
/// <param name="ReturnWindowDays">
/// The product's own window in days, frozen at placement, or null to fall back to the store policy.
/// </param>
public sealed record ReturnableLine(
    Guid OrderLineId,
    Guid ListingId,
    Guid VariantId,
    string Sku,
    string Name,
    Guid? ImageFileId,
    string? HsnCode,
    int Quantity,
    int QuantityCancelled,
    int QuantityReturned,
    decimal UnitPrice,
    decimal TaxableValue,
    decimal GstRate,
    decimal Cgst,
    decimal Sgst,
    decimal Igst,
    decimal Cess,
    decimal LineTotal,
    bool IsReturnable,
    int? ReturnWindowDays)
{
    /// <summary>How many units are still eligible to be sent back.</summary>
    public int QuantityReturnable => Math.Max(0, Quantity - QuantityCancelled - QuantityReturned);
}

/// <summary>
/// One seller's part of a delivered order, as the module that handles returns needs it.
/// </summary>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number, which is what a shopper quotes.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="SubOrderNumber">Its number.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="VendorName">The seller's legal name, for the credit note.</param>
/// <param name="VendorGstin">The seller's GSTIN, under which the credit note is raised.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Status">Where the seller's part currently stands.</param>
/// <param name="PaymentMethod">Prepaid or cash on delivery. It decides what a refund can go back to.</param>
/// <param name="IsPaid">Whether money was actually collected. A COD parcel refused at the door was not.</param>
/// <param name="ShippingTotal">What delivery cost the shopper on this part, inclusive of its tax.</param>
/// <param name="ShippingTax">The tax inside that delivery charge.</param>
/// <param name="Total">What this seller's part came to, inclusive of tax.</param>
/// <param name="CurrencyCode">ISO 4217 code every amount is in.</param>
/// <param name="PlaceOfSupplyStateId">The state the goods went to, which decides the credit note's tax heads.</param>
/// <param name="IsIntraState">Whether the supply was CGST + SGST rather than IGST.</param>
/// <param name="DeliveredAt">When it was delivered, from which the return window runs.</param>
/// <param name="ReturnWindowEndsAt">When the window closes, as the order itself recorded it.</param>
/// <param name="InvoiceId">The tax invoice raised for it, which a credit note credits.</param>
/// <param name="InvoiceNumber">That invoice's number, which the credit note must name.</param>
/// <param name="PickupAddress">Where the goods are, which is where a reverse pickup collects from.</param>
/// <param name="Lines">What is in it, and how much of each may still come back.</param>
public sealed record SubOrderReturnView(
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    string SubOrderNumber,
    Guid VendorId,
    string? VendorName,
    string? VendorGstin,
    Guid CustomerId,
    string Status,
    string PaymentMethod,
    bool IsPaid,
    decimal ShippingTotal,
    decimal ShippingTax,
    decimal Total,
    string CurrencyCode,
    Guid? PlaceOfSupplyStateId,
    bool IsIntraState,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? ReturnWindowEndsAt,
    Guid? InvoiceId,
    string? InvoiceNumber,
    FulfilmentAddress? PickupAddress,
    IReadOnlyList<ReturnableLine> Lines);

/// <summary>One line of a return, as the ordering module is told about it.</summary>
/// <param name="OrderLineId">The line.</param>
/// <param name="Quantity">How many units came back.</param>
public sealed record ReturnedUnits(Guid OrderLineId, int Quantity);

/// <summary>
/// The seam Returns reaches ordering through (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// The third of ordering's outward seams, and it exists for the reason the other two do: Orders owns
/// the record of the sale, the frozen lines and the state machine that moves them, and no other
/// module may read <c>orders.order_lines</c> to find out what was bought. Returns needs exactly
/// three things from a sale — what may still come back, permission to move the sub-order while a
/// return runs, and somewhere to write what happened — and this interface is those three things.
/// </para>
/// <para>
/// Synchronous rather than an integration event, and for the reason the fulfilment seam is: booking
/// a courier to collect goods from a sub-order that was never delivered is a van nobody should have
/// sent. The caller needs the refusal before it tells a courier anything.
/// </para>
/// <para>
/// Every method is idempotent. A redelivered event, a retried request and an operator clicking twice
/// must all leave one return and one set of returned quantities.
/// </para>
/// </remarks>
public interface IOrderReturns
{
    /// <summary>
    /// Reads what may still be sent back, or fails if the sub-order is not this caller's to see.
    /// </summary>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="customerId">
    /// The shopper, when the request is theirs. A sub-order belonging to somebody else does not
    /// resolve, and the failure is the same one an invented id gets.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<SubOrderReturnView>> GetAsync(
        Guid subOrderId,
        Guid? customerId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads several at once, for a shopper's "what can I send back" screen.</summary>
    /// <param name="subOrderIds">The seller's parts.</param>
    /// <param name="customerId">The shopper, when the request is theirs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SubOrderReturnView>> GetManyAsync(
        IReadOnlyCollection<Guid> subOrderIds,
        Guid? customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the seller's part along the return leg of the lifecycle, as the system.
    /// </summary>
    /// <remarks>
    /// The same call <c>IOrderFulfilment.AdvanceAsync</c> makes, restricted to the three states a
    /// return owns — <c>ReturnRequested</c>, <c>ReturnInProgress</c> and <c>Returned</c>. Anything
    /// else is refused here rather than in the state machine, so a bug in this module cannot cancel
    /// an order.
    /// </remarks>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="status">The status to move to, in the ordering module's vocabulary.</param>
    /// <param name="note">What the order timeline should say.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> AdvanceAsync(
        Guid subOrderId,
        string status,
        string? note,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that units have come back, against the frozen lines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what stops the same unit being returned twice: <c>order_lines.quantity_returned</c>
    /// is the platform's only count of what has gone back, and a second request for units already
    /// recorded here finds nothing left to return.
    /// </para>
    /// <para>
    /// It is called when QC decides, not when the shopper asks — a return that never arrives must
    /// not consume the shopper's right to send the goods back.
    /// </para>
    /// </remarks>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="units">The units, per line.</param>
    /// <param name="note">What the order timeline should say.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many units were actually recorded, which may be fewer than were asked for.</returns>
    Task<Result<int>> RecordReturnedAsync(
        Guid subOrderId,
        IReadOnlyCollection<ReturnedUnits> units,
        string? note,
        CancellationToken cancellationToken = default);

    /// <summary>Writes a line on the order's timeline without moving it.</summary>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="note">What happened, in words a shopper can read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> NoteAsync(Guid subOrderId, string note, CancellationToken cancellationToken = default);
}
