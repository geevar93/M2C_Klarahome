using KlaraHome.Contracts.Platform;

namespace KlaraHome.Modules.Catalog.Infrastructure.BuyBox;

/// <summary>
/// One offer, reduced to the facts the buy box ranks on.
/// </summary>
/// <param name="ListingId">The offer.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="SellingPrice">What they are asking, inclusive of GST.</param>
/// <param name="VendorRating">Their average review score, or null if they have none yet.</param>
/// <param name="DispatchSlaHours">The longer of their handling time and their own dispatch SLA.</param>
/// <param name="HasStock">
/// Whether Inventory says there is any. Null until Step 11 exists, and treated as "unknown, do not
/// discriminate" rather than as "no" — ranking every offer as out of stock would empty the buy box
/// for the whole catalogue.
/// </param>
/// <param name="PublishedAt">When the offer went live. The final, stable tie-break.</param>
internal sealed record BuyBoxCandidate(
    Guid ListingId,
    Guid VendorId,
    decimal SellingPrice,
    decimal? VendorRating,
    int DispatchSlaHours,
    bool? HasStock,
    DateTimeOffset? PublishedAt);

/// <summary>
/// Picks the offer a shopper is shown by default when several sellers offer the same variant
/// (docs/03-database-design.md §4.4).
/// </summary>
/// <remarks>
/// <para>
/// A pure function over the candidates and the configured rule, deliberately: this decision is
/// made once per variant on a product page and dozens of times on a listing page, so it must not
/// touch a database, and it must be testable without one. Loading the candidates is the caller's
/// job.
/// </para>
/// <para>
/// The criteria are applied in the configured order, each breaking the tie the previous left, and
/// the last word is always the oldest offer. That final tie-break is not decoration: without it,
/// two identically priced sellers would swap the buy box between page loads depending on how the
/// database felt like ordering the rows, the price shown in a search result would disagree with
/// the price on the product page, and nobody would be able to reproduce it.
/// </para>
/// </remarks>
internal static class BuyBoxResolver
{
    /// <summary>The score an unrated seller is given when the settings say to treat them as average.</summary>
    /// <remarks>
    /// Three out of five. A new seller must be able to win a first sale, and ranking them below
    /// every rated competitor is the cold-start failure that makes a marketplace impossible to
    /// join.
    /// </remarks>
    public const decimal AssumedRatingWhenUnrated = 3.0m;

    /// <summary>The winner, or null when there is nothing to choose from.</summary>
    /// <param name="candidates">The live offers for one variant.</param>
    /// <param name="settings">The configured rule.</param>
    public static BuyBoxCandidate? Select(
        IReadOnlyCollection<BuyBoxCandidate> candidates,
        BuyBoxSettings settings)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(settings);

        BuyBoxCandidate? best = null;

        foreach (var candidate in candidates)
        {
            if (best is null || Compare(candidate, best, settings) < 0)
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>The offers in buy-box order, best first.</summary>
    /// <remarks>
    /// The "other sellers" panel under the buy box shows exactly this, minus its head, so ordering
    /// and winning have to be the same comparison rather than two that agree by accident.
    /// </remarks>
    /// <param name="candidates">The live offers for one variant.</param>
    /// <param name="settings">The configured rule.</param>
    public static IReadOnlyList<BuyBoxCandidate> Rank(
        IReadOnlyCollection<BuyBoxCandidate> candidates,
        BuyBoxSettings settings)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(settings);

        var ranked = candidates.ToList();
        ranked.Sort((left, right) => Compare(left, right, settings));

        return ranked;
    }

    /// <summary>
    /// Orders two offers. Negative means <paramref name="left"/> wins.
    /// </summary>
    /// <param name="left">One offer.</param>
    /// <param name="right">The other.</param>
    /// <param name="settings">The configured rule.</param>
    internal static int Compare(BuyBoxCandidate left, BuyBoxCandidate right, BuyBoxSettings settings)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(settings);

        foreach (var criterion in settings.Criteria)
        {
            var verdict = criterion switch
            {
                BuyBoxCriteria.LandedPrice => left.SellingPrice.CompareTo(right.SellingPrice),
                BuyBoxCriteria.VendorRating => RatingOf(right, settings).CompareTo(RatingOf(left, settings)),
                BuyBoxCriteria.DispatchSla => left.DispatchSlaHours.CompareTo(right.DispatchSlaHours),
                BuyBoxCriteria.StockAvailability => StockRank(left).CompareTo(StockRank(right)),

                // An unknown name in configuration is ignored rather than fatal. A typo in a
                // settings screen must not take the buy box out of the whole storefront.
                _ => 0,
            };

            if (verdict != 0)
            {
                return verdict;
            }
        }

        // The stable last word. Oldest offer first, and the listing id — which is time-ordered —
        // for the offers published in the same instant by a bulk import.
        var byAge = Nullable.Compare(left.PublishedAt, right.PublishedAt);

        return byAge != 0 ? byAge : left.ListingId.CompareTo(right.ListingId);
    }

    private static decimal RatingOf(BuyBoxCandidate candidate, BuyBoxSettings settings)
        => candidate.VendorRating ?? (settings.TreatUnratedAsAverage ? AssumedRatingWhenUnrated : 0m);

    /// <summary>
    /// Zero for an offer that can ship, one for one whose stock is unknown, two for one known to
    /// be out.
    /// </summary>
    /// <remarks>
    /// Unknown sits between the two on purpose. Before Inventory exists every offer is unknown and
    /// the criterion is a no-op; once it exists, an offer nobody has counted still beats one that
    /// has been counted and is empty.
    /// </remarks>
    private static int StockRank(BuyBoxCandidate candidate)
        => candidate.HasStock switch
        {
            true => 0,
            null => 1,
            false => 2,
        };
}
