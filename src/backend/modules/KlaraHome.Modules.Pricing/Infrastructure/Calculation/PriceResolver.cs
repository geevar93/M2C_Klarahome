using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Modules.Pricing.Domain;
using KlaraHome.Modules.Pricing.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Pricing.Infrastructure.Calculation;

/// <summary>The price one offer resolved to, and the rule that decided it.</summary>
/// <param name="UnitPrice">What one unit costs, inclusive of GST.</param>
/// <param name="PriceListId">The list that won, or null when the offer's own price stood.</param>
/// <param name="PriceListName">Its name, for the explanation.</param>
/// <param name="MinQuantity">The quantity tier that applied.</param>
internal readonly record struct ResolvedPrice(
    decimal UnitPrice,
    Guid? PriceListId,
    string? PriceListName,
    int MinQuantity);

/// <summary>
/// Walks the price lists and decides what an offer costs (docs/03-database-design.md §4.6).
/// </summary>
/// <remarks>
/// <para>
/// The rule, stated once: among the lists that are active, in window, and either platform-wide or
/// the offer's own seller's, the lowest priority number wins; within that list the highest quantity
/// tier at or below the requested quantity wins; and an offer with no item in any applicable list
/// keeps the price on its own listing.
/// </para>
/// <para>
/// The whole walk happens in memory over one query. A price list is a handful of rows and a store
/// has a handful of lists, so pushing the priority-and-tier ordering into SQL would buy nothing and
/// cost the ability to explain the answer — and explaining the answer is most of what this class is
/// for.
/// </para>
/// </remarks>
/// <param name="context">The Pricing data context.</param>
/// <param name="catalog">Supplies the MRP and the fallback price.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class PriceResolver(PricingDbContext context, IProductCatalog catalog, IClock clock) : IPriceCatalog
{
    /// <inheritdoc />
    public async ValueTask<EffectivePrice?> FindAsync(
        Guid listingId,
        int quantity = 1,
        CancellationToken cancellationToken = default)
    {
        var listing = await catalog.FindListingAsync(listingId, cancellationToken).ConfigureAwait(false);

        if (listing is null)
        {
            return null;
        }

        var wanted = Math.Max(1, quantity);
        var listings = new Dictionary<Guid, ListingSummary> { [listingId] = listing };

        var resolved = await ResolveAsync(
                listings,
                new Dictionary<Guid, int> { [listingId] = wanted },
                clock.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

        return Describe(listing, resolved[listingId], wanted);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<Guid, EffectivePrice>> FindManyAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(listingIds);

        if (listingIds.Count == 0)
        {
            return new Dictionary<Guid, EffectivePrice>();
        }

        var listings = await catalog.FindListingsAsync(listingIds, cancellationToken).ConfigureAwait(false);

        if (listings.Count == 0)
        {
            return new Dictionary<Guid, EffectivePrice>();
        }

        var quantities = listings.Keys.ToDictionary(id => id, _ => 1);

        var resolved = await ResolveAsync(listings, quantities, clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        return listings.ToDictionary(
            pair => pair.Key,
            pair => Describe(pair.Value, resolved[pair.Key], 1));
    }

    /// <summary>
    /// Resolves a whole basket at once: one query for the candidate rows, then the walk per offer.
    /// </summary>
    /// <remarks>
    /// The quote engine calls this rather than <see cref="FindManyAsync"/> because it already holds
    /// the listing summaries and the per-line quantities, and a tier depends on the quantity. One
    /// database round trip prices a fifty-line cart.
    /// </remarks>
    /// <param name="listings">The offers, as the catalogue describes them.</param>
    /// <param name="quantities">How many units of each, for the quantity tiers.</param>
    /// <param name="asOf">The instant to price at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyDictionary<Guid, ResolvedPrice>> ResolveAsync(
        IReadOnlyDictionary<Guid, ListingSummary> listings,
        IReadOnlyDictionary<Guid, int> quantities,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listings);
        ArgumentNullException.ThrowIfNull(quantities);

        var result = new Dictionary<Guid, ResolvedPrice>(listings.Count);

        if (listings.Count == 0)
        {
            return result;
        }

        var ids = listings.Keys.ToList();

        // One join, filtered on the window in SQL. The vendor test and the tier choice are in
        // memory because both depend on the line being priced, not on the row.
        var candidates = await (
                from item in context.PriceListItems.AsNoTracking()
                join list in context.PriceLists.AsNoTracking() on item.PriceListId equals list.Id
                where ids.Contains(item.ListingId)
                      && list.IsActive
                      && (list.StartsAt == null || list.StartsAt <= asOf)
                      && (list.EndsAt == null || list.EndsAt > asOf)
                select new PriceCandidate(
                    item.ListingId,
                    list.Id,
                    list.Name,
                    list.VendorId,
                    list.Priority,
                    item.MinQuantity,
                    item.Price.Amount))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byListing = candidates.GroupBy(candidate => candidate.ListingId)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var (listingId, listing) in listings)
        {
            var wanted = quantities.TryGetValue(listingId, out var quantity) ? Math.Max(1, quantity) : 1;

            result[listingId] = Choose(byListing.GetValueOrDefault(listingId), listing, wanted);
        }

        return result;
    }

    /// <summary>Picks the winning row for one offer, or falls back to the listing's own price.</summary>
    /// <param name="candidates">Every applicable row for the offer, before the vendor and tier tests.</param>
    /// <param name="listing">The offer.</param>
    /// <param name="quantity">How many units, for the tier.</param>
    private static ResolvedPrice Choose(List<PriceCandidate>? candidates, ListingSummary listing, int quantity)
    {
        var fallback = new ResolvedPrice(listing.SellingPrice, PriceListId: null, PriceListName: null, MinQuantity: 1);

        if (candidates is null || candidates.Count == 0)
        {
            return fallback;
        }

        var winner = candidates
            .Where(candidate =>
                (candidate.VendorId is null || candidate.VendorId == listing.VendorId)
                && candidate.MinQuantity <= quantity)
            .OrderBy(candidate => candidate.Priority)
            .ThenByDescending(candidate => candidate.MinQuantity)
            .ThenBy(candidate => candidate.PriceListId)
            .FirstOrDefault();

        return winner is null
            ? fallback
            : new ResolvedPrice(winner.Price, winner.PriceListId, winner.PriceListName, winner.MinQuantity);
    }

    /// <summary>States a resolved price for a caller of the published contract.</summary>
    /// <param name="listing">The offer, for its MRP and currency.</param>
    /// <param name="resolved">What the walk decided.</param>
    /// <param name="quantity">The quantity it was resolved for.</param>
    private static EffectivePrice Describe(ListingSummary listing, ResolvedPrice resolved, int quantity)
        => new(
            listing.ListingId,
            listing.Mrp,
            resolved.UnitPrice,
            quantity,
            SharedKernel.Primitives.Money.Inr,
            resolved.PriceListId,
            resolved.PriceListName,
            resolved.MinQuantity);

    /// <summary>One price-list row that could apply to one offer.</summary>
    /// <param name="ListingId">The offer.</param>
    /// <param name="PriceListId">The list.</param>
    /// <param name="PriceListName">Its name.</param>
    /// <param name="VendorId">The seller it is limited to, or null for platform-wide.</param>
    /// <param name="Priority">Its rank. Lower wins.</param>
    /// <param name="MinQuantity">The tier this row is for.</param>
    /// <param name="Price">What it charges.</param>
    private sealed record PriceCandidate(
        Guid ListingId,
        Guid PriceListId,
        string PriceListName,
        Guid? VendorId,
        int Priority,
        int MinQuantity,
        decimal Price);
}
