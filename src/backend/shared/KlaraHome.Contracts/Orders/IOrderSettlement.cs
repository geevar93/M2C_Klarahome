using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Contracts.Orders;

/// <summary>
/// One line of a sold sub-order, as the module that pays the seller needs it.
/// </summary>
/// <remarks>
/// <para>
/// The commission is carried rather than resolved, and that is the whole reason this record exists
/// separately from <see cref="ReturnableLine"/>. What the platform charges was decided when the
/// order was placed and frozen onto the line; re-resolving it at settlement would charge a seller
/// today's rate for last month's sale, which is the one thing a settlement statement must never do.
/// </para>
/// <para>
/// The taxable value is carried for the same reason and for a second one: TCS under section 52 of
/// the CGST Act is charged on the <em>net value of taxable supplies</em>, not on what the shopper
/// paid, and the two differ by the GST inside the price.
/// </para>
/// </remarks>
/// <param name="OrderLineId">The line.</param>
/// <param name="ListingId">The offer sold.</param>
/// <param name="CategoryId">
/// The category it was sold in, frozen at placement. Carried so a settlement can say which of a
/// plan's category rules applied, and so a re-resolution is possible when a line has no commission.
/// </param>
/// <param name="Sku">The stock-keeping unit, frozen at placement.</param>
/// <param name="Name">What it is called, frozen at placement.</param>
/// <param name="Quantity">How many were ordered.</param>
/// <param name="QuantityCancelled">How many were cancelled.</param>
/// <param name="QuantityReturned">How many have come back.</param>
/// <param name="UnitPrice">What one unit cost, inclusive of tax.</param>
/// <param name="TaxableValue">What the tax was computed on, for the whole line.</param>
/// <param name="TaxAmount">The tax charged on the line, every head together.</param>
/// <param name="LineTotal">What the shopper paid for the line, inclusive of tax.</param>
/// <param name="CommissionRate">The rate that was applied, as a percentage.</param>
/// <param name="CommissionAmount">What the platform charged on the line, in money.</param>
/// <param name="CommissionPlanId">The plan the rate came from, so a statement can name it.</param>
public sealed record SettleableLine(
    Guid OrderLineId,
    Guid ListingId,
    Guid? CategoryId,
    string Sku,
    string Name,
    int Quantity,
    int QuantityCancelled,
    int QuantityReturned,
    decimal UnitPrice,
    decimal TaxableValue,
    decimal TaxAmount,
    decimal LineTotal,
    decimal CommissionRate,
    decimal CommissionAmount,
    Guid? CommissionPlanId)
{
    /// <summary>How many units are still sold — neither cancelled nor sent back.</summary>
    public int QuantityLive => Math.Max(0, Quantity - QuantityCancelled - QuantityReturned);
}

/// <summary>
/// One seller's part of an order, as the module that pays them needs it.
/// </summary>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="SubOrderNumber">Its number, which is what appears on a statement line.</param>
/// <param name="VendorId">The seller being paid.</param>
/// <param name="VendorCode">Their short code, quoted on the statement.</param>
/// <param name="VendorName">Their legal name.</param>
/// <param name="VendorGstin">Their GST registration, under which the supply was made.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Status">Where the seller's part currently stands.</param>
/// <param name="PaymentMethod">
/// Prepaid or cash on delivery. It decides <em>when</em> the money is the platform's to pay out:
/// a prepaid sale is earned on delivery, and a cash sale only when the courier remits.
/// </param>
/// <param name="IsPaid">Whether money was actually collected against the order.</param>
/// <param name="ItemsTotal">What the goods came to, inclusive of tax.</param>
/// <param name="DiscountTotal">What came off them.</param>
/// <param name="ShippingTotal">What delivery cost the shopper on this part, inclusive of its tax.</param>
/// <param name="ShippingTax">The tax inside that delivery charge.</param>
/// <param name="TaxableValue">What the seller's GST was computed on, for the whole part.</param>
/// <param name="TaxTotal">The GST charged on it.</param>
/// <param name="Total">What the shopper paid for this seller's part, inclusive of tax.</param>
/// <param name="CurrencyCode">ISO 4217 code every amount is in.</param>
/// <param name="PlacedAt">When the order was placed.</param>
/// <param name="DeliveredAt">When it was delivered, which is when the earning arises.</param>
/// <param name="ReturnWindowEndsAt">When the goods stop being returnable.</param>
/// <param name="InvoiceId">The tax invoice raised for it.</param>
/// <param name="InvoiceNumber">That invoice's number, quoted on the statement.</param>
/// <param name="FinancialYear">The Indian financial year the invoice belongs to, as <c>2026-27</c>.</param>
/// <param name="Lines">What is in it.</param>
public sealed record SubOrderSettlementView(
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    string SubOrderNumber,
    Guid VendorId,
    string? VendorCode,
    string? VendorName,
    string? VendorGstin,
    Guid CustomerId,
    string Status,
    string PaymentMethod,
    bool IsPaid,
    decimal ItemsTotal,
    decimal DiscountTotal,
    decimal ShippingTotal,
    decimal ShippingTax,
    decimal TaxableValue,
    decimal TaxTotal,
    decimal Total,
    string CurrencyCode,
    DateTimeOffset PlacedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? ReturnWindowEndsAt,
    Guid? InvoiceId,
    string? InvoiceNumber,
    string? FinancialYear,
    IReadOnlyList<SettleableLine> Lines);

/// <summary>
/// The seam Settlements reaches ordering through (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// The fourth of ordering's outward seams and the only read-only one. Settlements never moves a
/// sub-order, never cancels one and never writes to it: it reads what was sold, what it was worth
/// and what the platform charged for it, and everything it does with those numbers happens in its
/// own schema. A seam that could write would be a seam through which a settlement run could change
/// the sale it is settling.
/// </para>
/// <para>
/// It is deliberately not a widening of <see cref="IOrderReturns"/>. That one is read on a shopper's
/// return screen and is confined to a customer; this one is read by a background job for a seller and
/// is confined to nobody, because a settlement run covers every shopper at once.
/// </para>
/// </remarks>
public interface IOrderSettlement
{
    /// <summary>What one seller's part of an order is worth, and what was charged on it.</summary>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<SubOrderSettlementView>> GetAsync(
        Guid subOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same for several parts at once. Absent ids are simply not in the result — a settlement
    /// run reading a sub-order that has since been purged is a case the caller handles.
    /// </summary>
    /// <param name="subOrderIds">The parts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SubOrderSettlementView>> GetManyAsync(
        IReadOnlyCollection<Guid> subOrderIds,
        CancellationToken cancellationToken = default);
}
