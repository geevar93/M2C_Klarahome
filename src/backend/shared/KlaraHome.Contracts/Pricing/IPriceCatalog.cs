namespace KlaraHome.Contracts.Pricing;

/// <summary>
/// What one offer actually costs right now, and which rule decided it.
/// </summary>
/// <remarks>
/// The explanation is not decoration. A marketplace price is the outcome of a walk over price
/// lists, windows and quantity tiers, and the first question anybody asks when it looks wrong is
/// "which list won". Answering it here costs one string and saves a support call reading SQL.
/// </remarks>
/// <param name="ListingId">The offer.</param>
/// <param name="Mrp">Maximum retail price, from the catalogue. Statutory, and never below <paramref name="UnitPrice"/>.</param>
/// <param name="UnitPrice">What the shopper pays per unit, inclusive of GST.</param>
/// <param name="Quantity">The quantity the price was resolved for; a tier may depend on it.</param>
/// <param name="CurrencyCode">ISO 4217 code the amounts are in.</param>
/// <param name="PriceListId">The price list that won, or null when the offer's own price stood.</param>
/// <param name="PriceListName">Its name, for the explanation.</param>
/// <param name="MinQuantity">The quantity tier that applied, or 1 when no tier did.</param>
public sealed record EffectivePrice(
    Guid ListingId,
    decimal Mrp,
    decimal UnitPrice,
    int Quantity,
    string CurrencyCode,
    Guid? PriceListId,
    string? PriceListName,
    int MinQuantity)
{
    /// <summary>How much is taken off the MRP, per unit. Zero when the offer sells at MRP.</summary>
    public decimal UnitSaving => Mrp > UnitPrice ? Mrp - UnitPrice : 0m;

    /// <summary>The saving as a whole-number percentage of MRP, for a "20% off" badge.</summary>
    public int DiscountPercent
        => Mrp <= 0m || UnitSaving <= 0m ? 0 : (int)Math.Round(UnitSaving / Mrp * 100m, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Resolves the selling price of an offer from outside the Pricing module
/// (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// Search projects a price into its index, a product page renders one, and a cart line shows one
/// before any of them has a basket to quote. None of them may join to <c>pricing.price_lists</c>,
/// so this contract is the whole of their access. It is the mirror of <c>IProductCatalog</c> and
/// <c>IStockAvailability</c>, and exists for the same reason.
/// </para>
/// <para>
/// Deliberately narrower than <see cref="IPriceQuoteEngine"/>: this answers "what does one unit
/// cost", with no promotion, no tax split and no order-level arithmetic. A caller that needs the
/// itemised breakdown asks the quote engine, and nothing in this platform computes a tax figure
/// anywhere else.
/// </para>
/// </remarks>
public interface IPriceCatalog
{
    /// <summary>The effective price of one offer, or null when the catalogue has no such offer.</summary>
    /// <param name="listingId">The offer.</param>
    /// <param name="quantity">How many units, for a quantity tier. Defaults to one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<EffectivePrice?> FindAsync(
        Guid listingId,
        int quantity = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The effective price of several offers at one unit each, keyed by listing. Absent ids are
    /// simply not in the result — an offer that has been archived is a case the caller handles.
    /// </summary>
    /// <param name="listingIds">The offers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyDictionary<Guid, EffectivePrice>> FindManyAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken = default);
}
