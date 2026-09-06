namespace KlaraHome.Contracts.Orders;

/// <summary>
/// One line of one order that reached the customer, as a module outside Orders sees it.
/// </summary>
/// <remarks>
/// Deliberately thin. This exists to answer "did this person receive this thing, and when" — it
/// carries no money, no tax and no address, because the one caller it was built for does not need
/// any of that and a wider contract would invite one that did.
/// </remarks>
/// <param name="OrderLineId">The line. This is the token that proves the purchase.</param>
/// <param name="OrderId">The order it belongs to.</param>
/// <param name="OrderNumber">The human-readable number, so a review can be traced to a sale.</param>
/// <param name="SubOrderId">The seller's part of the order.</param>
/// <param name="VendorId">The seller who sold it, which is who a review's stars also land on.</param>
/// <param name="ListingId">The offer bought.</param>
/// <param name="VariantId">The sellable thing bought.</param>
/// <param name="Sku">The stock-keeping unit, frozen at placement.</param>
/// <param name="Name">What the line was called on the order, frozen at placement.</param>
/// <param name="Quantity">How many, net of anything cancelled.</param>
/// <param name="DeliveredAt">When the seller's parcel was delivered.</param>
public sealed record PurchasedLine(
    Guid OrderLineId,
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    Guid VendorId,
    Guid ListingId,
    Guid VariantId,
    string Sku,
    string Name,
    int Quantity,
    DateTimeOffset DeliveredAt);

/// <summary>
/// Answers whether somebody actually received something, from outside the Orders module
/// (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// Added at Step 21 for reviews, and it is the whole of the acceptance criterion "a review can only
/// be posted against a delivered purchase". Putting the check behind a contract rather than in the
/// Reviews module means there is exactly one implementation of what "delivered" means, and it is in
/// the module that owns the state machine that decides it.
/// </para>
/// <para>
/// It is the fifth seam onto Orders and the narrowest by a distance.
/// <see cref="IOrderPlacement"/> creates an order, <see cref="IOrderFulfilment"/> moves one,
/// <see cref="IOrderReturns"/> reverses one and <see cref="IOrderSettlement"/> reads one for money;
/// this one answers a single yes-or-no question about the past and cannot write anything at all.
/// </para>
/// <para>
/// Every method takes the customer as a parameter rather than reading an ambient caller. A review is
/// written by the person who bought the thing, and a contract whose answer depended on who happened
/// to be signed in would be one an admin tool could not call on somebody's behalf — and, far worse,
/// one that would quietly return another shopper's purchases if the ambient context were ever wrong.
/// </para>
/// </remarks>
public interface IOrderPurchases
{
    /// <summary>
    /// The line, if this customer bought it and it was delivered. Null for every other case —
    /// somebody else's line, a line that was cancelled, and a line that is still in transit are all
    /// the same answer to the only question being asked.
    /// </summary>
    /// <param name="orderLineId">The line offered as proof of purchase.</param>
    /// <param name="customerId">Who claims to have bought it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<PurchasedLine?> FindDeliveredLineAsync(
        Guid orderLineId,
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Everything this customer has received, newest delivery first.
    /// </summary>
    /// <remarks>
    /// The list behind "review your recent purchases". Optionally narrowed to one variant, which is
    /// the form the product page asks in: it needs to know whether the shopper reading it may write
    /// a review, and if so against which line.
    /// </remarks>
    /// <param name="customerId">The shopper.</param>
    /// <param name="variantId">Only lines for this variant, or null for all of them.</param>
    /// <param name="limit">The most lines to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyList<PurchasedLine>> ListDeliveredLinesAsync(
        Guid customerId,
        Guid? variantId,
        int limit,
        CancellationToken cancellationToken = default);
}
