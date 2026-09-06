using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Modules.Pricing.Infrastructure;

/// <summary>
/// The platform's own limits on the price engine.
/// </summary>
/// <remarks>
/// Configuration rather than store settings, on the same split every module here draws: resource
/// limits and safety valves live where a shopkeeper cannot change them. The commercial levers — the
/// COD fee, the loyalty rate, the store-credit ceiling — are in the <c>pricing</c> settings section,
/// because they are a business decision and change more often than the product is deployed.
/// </remarks>
internal sealed class PricingOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Pricing";

    /// <summary>
    /// The most lines one quote will price.
    /// </summary>
    /// <remarks>
    /// A quote is an anonymous, unauthenticated endpoint that reads the catalogue, the price lists
    /// and every live promotion. Without a ceiling it is a denial-of-service primitive with a
    /// friendly name.
    /// </remarks>
    [Range(1, 500)]
    public int MaxQuoteLines { get; set; } = 100;

    /// <summary>
    /// The most promotions one quote will consider.
    /// </summary>
    /// <remarks>
    /// The evaluator is linear in candidates and every one of them walks the basket, so the cost is
    /// the product of the two. A store with more live campaigns than this has a merchandising
    /// problem rather than a pricing one, and the cap makes the symptom visible.
    /// </remarks>
    [Range(1, 1000)]
    public int MaxPromotionCandidates { get; set; } = 100;

    /// <summary>
    /// The most items one bulk price upsert may carry.
    /// </summary>
    /// <remarks>
    /// A price list is edited in batches from a spreadsheet, and a single unbounded request would
    /// hold a transaction open across the whole file. Bulk catalogue work above this belongs in the
    /// import job the Catalog module already runs.
    /// </remarks>
    [Range(1, 10_000)]
    public int MaxPriceListItemsPerRequest { get; set; } = 1_000;

    /// <summary>
    /// Whether a price may be set for an offer the catalogue does not know about.
    /// </summary>
    /// <remarks>
    /// Off. A price keyed on a listing nobody can buy is a row that will never be read, and it is
    /// the shape a mistyped column in a spreadsheet takes.
    /// </remarks>
    public bool RequireKnownListing { get; set; } = true;
}
