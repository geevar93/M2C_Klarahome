using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Contracts.Orders;

/// <summary>Where a parcel is going, as the module that books couriers needs it.</summary>
/// <remarks>
/// A snapshot rather than an address id. The order froze the address at placement precisely so a
/// customer editing their address book cannot redirect a parcel already in a courier's hands, and
/// handing out the id would let Shipping re-read the live one and undo that.
/// </remarks>
/// <param name="Name">Who receives it.</param>
/// <param name="Mobile">The number the courier rings, in E.164.</param>
/// <param name="Line1">Building and unit.</param>
/// <param name="Line2">Street, area or locality.</param>
/// <param name="Landmark">A nearby landmark. Couriers in India navigate by these.</param>
/// <param name="City">City or town.</param>
/// <param name="StateId">The <c>platform.states</c> row.</param>
/// <param name="Pincode">Six-digit PIN code. Serviceability and rating both key on it.</param>
public sealed record FulfilmentAddress(
    string Name,
    string? Mobile,
    string Line1,
    string? Line2,
    string? Landmark,
    string City,
    Guid StateId,
    string Pincode);

/// <summary>One line of a seller's part, as the packer sees it.</summary>
/// <param name="OrderLineId">The line.</param>
/// <param name="Sku">The stock-keeping unit, frozen at placement.</param>
/// <param name="Name">What it is called, for the pick list and the label.</param>
/// <param name="Quantity">How many were ordered and not cancelled.</param>
/// <param name="UnitWeightGrams">
/// What one unit weighs, frozen on the line at placement. It is what a courier prices on, and it is
/// carried here rather than looked up because the catalogue may have been edited since.
/// </param>
/// <param name="LineTotal">What the shopper pays for them, inclusive of tax.</param>
/// <param name="WarehouseId">
/// The stock location the units were allocated from at placement, or null when the offer was not
/// stocked. It is what turns a pick list into a route somebody can actually walk: with two
/// warehouses and no location on the line, both pickers are given every parcel.
/// </param>
public sealed record FulfilmentLine(
    Guid OrderLineId,
    string Sku,
    string Name,
    int Quantity,
    int UnitWeightGrams,
    decimal LineTotal,
    Guid? WarehouseId);

/// <summary>
/// One seller's part of an order, as the module that moves parcels needs it.
/// </summary>
/// <remarks>
/// Everything a consignment is booked from and nothing else. Shipping never learns what the shopper
/// paid in total, what discount they had or which promotion applied — a courier booking is an
/// address, a weight, a value for insurance and a cash figure for the door.
/// </remarks>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number, which is what goes on the label.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="SubOrderNumber">Its number, which is what the seller's worklist shows.</param>
/// <param name="VendorId">The seller dispatching it.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Status">Where the seller's part currently stands.</param>
/// <param name="PaymentMethod">Prepaid or cash on delivery.</param>
/// <param name="IsCod">Whether cash is to be collected at the door.</param>
/// <param name="AmountDueAtDelivery">What the courier must collect, or zero for a prepaid parcel.</param>
/// <param name="DeclaredValue">What the goods are worth, for the courier's insurance and paperwork.</param>
/// <param name="CurrencyCode">ISO 4217 code both amounts are in.</param>
/// <param name="DispatchDueAt">When the seller must have handed the parcel over.</param>
/// <param name="ShippingOptionCode">The delivery service the shopper chose at checkout.</param>
/// <param name="Destination">Where it is going.</param>
/// <param name="Lines">What is in it.</param>
public sealed record SubOrderFulfilmentView(
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    string SubOrderNumber,
    Guid VendorId,
    Guid CustomerId,
    string Status,
    string PaymentMethod,
    bool IsCod,
    decimal AmountDueAtDelivery,
    decimal DeclaredValue,
    string CurrencyCode,
    DateTimeOffset? DispatchDueAt,
    string? ShippingOptionCode,
    FulfilmentAddress Destination,
    IReadOnlyList<FulfilmentLine> Lines);

/// <summary>
/// The seam Shipping reaches ordering through (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// The mirror of <see cref="IOrderPaymentSync"/>, declared at Step 16 for the reason that one was
/// declared at Step 14: Orders owns the record of the sale and the state machine that moves it,
/// Shipping owns the conversation with the courier, and neither reads the other's schema. The
/// architecture diagram's edge from ordering to shipping is this interface.
/// </para>
/// <para>
/// Synchronous rather than an integration event, and for a narrower reason than payments had:
/// booking an AWB against a sub-order that has already been cancelled is a parcel a courier will
/// collect and nobody will pay for. The caller needs to know the transition succeeded before it
/// tells a courier anything.
/// </para>
/// <para>
/// Every method is idempotent. A tracking webhook is delivered at least once and the polling
/// fallback can reach the same scan independently, so a sub-order already <c>Delivered</c> is a
/// success rather than a conflict.
/// </para>
/// </remarks>
public interface IOrderFulfilment
{
    /// <summary>Reads what a consignment is booked from, or fails if the sub-order is not visible.</summary>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<SubOrderFulfilmentView>> GetAsync(
        Guid subOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads several at once, for a pick list that spans a morning of orders.</summary>
    /// <param name="subOrderIds">The seller's parts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SubOrderFulfilmentView>> GetManyAsync(
        IReadOnlyCollection<Guid> subOrderIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the seller's part along the lifecycle as the system, on the courier's word.
    /// </summary>
    /// <remarks>
    /// The transition is taken as <c>System</c> — the one actor no HTTP caller can claim to be — and
    /// it goes through the same workflow an operator's transition does, so a courier scan writes the
    /// same timeline and raises the same events. An edge the machine does not have is refused here
    /// rather than invented: a parcel the courier says is out for delivery on a sub-order that was
    /// cancelled yesterday is a discrepancy, not an instruction.
    /// </remarks>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="status">The status to move to, in the ordering module's vocabulary.</param>
    /// <param name="note">What the timeline should say. The courier's own words, normally.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> AdvanceAsync(
        Guid subOrderId,
        string status,
        string? note,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a line on the order's timeline without moving it.
    /// </summary>
    /// <remarks>
    /// What a scan that is not a state change deserves: "reached Bengaluru hub" is worth showing a
    /// shopper and is not worth a transition. Every intermediate courier scan lands here, which is
    /// what makes the order timeline the single place a support call is answered from.
    /// </remarks>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="note">What happened, in words a shopper can read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> NoteAsync(Guid subOrderId, string note, CancellationToken cancellationToken = default);
}
