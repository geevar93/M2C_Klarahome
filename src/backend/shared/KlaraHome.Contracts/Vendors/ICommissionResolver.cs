namespace KlaraHome.Contracts.Vendors;

/// <summary>
/// What the platform charges a seller for one sale.
/// </summary>
/// <param name="PlanId">The plan the answer came from.</param>
/// <param name="PlanName">Its name, so a settlement statement can say which plan was applied.</param>
/// <param name="RatePercent">The commission rate as a percentage — <c>12.5000</c> means 12.5%.</param>
/// <param name="FixedFee">A flat fee charged per unit sold, on top of the rate. Often zero.</param>
/// <param name="MatchedCategoryId">
/// The category whose rule matched, or null when the plan's default was used. Recorded because
/// "why was I charged this" is the second question every seller asks.
/// </param>
public sealed record CommissionQuote(
    Guid PlanId,
    string PlanName,
    decimal RatePercent,
    decimal FixedFee,
    Guid? MatchedCategoryId);

/// <summary>
/// Resolves the commission due on a sale (docs/03-database-design.md §4.3).
/// </summary>
/// <remarks>
/// <para>
/// Settlements computes what a seller is owed and needs the rate that applied; Catalog and the
/// vendor portal show a seller what a listing will earn them. Both ask here rather than reading
/// the plan tables, because resolution is an algorithm — most specific category, then price band,
/// then the plan default — and two implementations of it would eventually disagree about money.
/// </para>
/// <para>
/// The quote is resolved at the moment it is asked for. A settlement that must survive a later
/// change of plan stores the numbers it was given, it does not re-resolve them.
/// </para>
/// </remarks>
public interface ICommissionResolver
{
    /// <summary>
    /// The commission due for one seller selling in one category at one price.
    /// </summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="categoryId">The category of the item sold, or null when it is not known.</param>
    /// <param name="unitPrice">The selling price of one unit, which selects the price band.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The quote, or null when the seller has no commission plan assigned.</returns>
    ValueTask<CommissionQuote?> ResolveAsync(
        Guid vendorId,
        Guid? categoryId,
        decimal unitPrice,
        CancellationToken cancellationToken = default);
}
