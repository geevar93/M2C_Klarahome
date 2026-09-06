using KlaraHome.Contracts.Catalog;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Content.Infrastructure.Collections;

/// <summary>
/// Turns a rule into rows.
/// </summary>
/// <remarks>
/// <para>
/// A rule-based collection is <b>materialised</b>, not evaluated on demand, and that is the central
/// decision of this part of the module. Evaluating on demand would mean the storefront walking the
/// catalogue on every home-page render, across a schema boundary it may not cross; materialising
/// means the storefront reads one indexed table and the walking happens in the background, on a
/// timer, where nobody is waiting for it.
/// </para>
/// <para>
/// The price of that decision is staleness, and it is paid in two ways. A catalogue event updates the
/// membership of every rule-based collection for the one product that changed, within seconds — which
/// covers a product going live, changing price, or being withdrawn. A periodic sweep rebuilds a whole
/// collection from scratch, which covers the two things no event can: a rule that was edited, and the
/// "new in" condition that stops being true simply because time passed.
/// </para>
/// <para>
/// Pins survive both. A refresh only ever deletes rows it wrote itself — the
/// <c>is_from_rule</c> column exists for precisely that — so a merchandiser's chosen three stay at the
/// front through every rebuild, and stay there even after the rule stops matching them.
/// </para>
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="catalogue">The catalogue, in the shape a rule can be evaluated against.</param>
/// <param name="options">The walk's bounds.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what a refresh did.</param>
internal sealed partial class CollectionMaterializer(
    ContentDbContext context,
    IProductProjectionSource catalogue,
    IOptionsMonitor<ContentOptions> options,
    IClock clock,
    ILogger<CollectionMaterializer> logger)
{
    /// <summary>
    /// Rebuilds one rule-based collection from the catalogue.
    /// </summary>
    /// <remarks>
    /// The expensive path, and the correct one. It walks the catalogue variant by variant, evaluates
    /// the rule against each product's winning offer, sorts what matched, takes the rule's limit, and
    /// replaces every row the rule had written. The walk stops as soon as it has enough or has read
    /// its ceiling, so a rule that matches the first hundred products costs a hundred products.
    /// </remarks>
    /// <param name="collection">The collection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many products the rule put in it.</returns>
    public async Task<int> RefreshAsync(ProductCollection collection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var settings = options.CurrentValue;
        var rule = CollectionRules.Read(collection.Rules);
        var now = clock.UtcNow;

        var matches = await WalkAsync(rule, settings, now, cancellationToken).ConfigureAwait(false);

        var ordered = CollectionRuleEvaluator
            .Sort(rule, matches)
            .Take(Math.Clamp(rule.Limit, 1, CollectionRuleSet.MaxLimit))
            .Select(row => row.ProductId)
            .Distinct()
            .ToList();

        var existing = await context.CollectionItems
            .Where(item => item.CollectionId == collection.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pinned = existing.Where(item => item.IsPinned).OrderBy(item => item.Position).ToList();
        var pinnedProducts = pinned.Select(item => item.ProductId).ToHashSet();

        // Only the rows the rule wrote. A hand-picked row is somebody's decision and a rule has no
        // business overturning it.
        context.CollectionItems.RemoveRange(existing.Where(item => item is { IsFromRule: true, IsPinned: false }));

        var position = 0;

        foreach (var item in pinned)
        {
            item.MoveTo(position++, isPinned: true);
        }

        foreach (var productId in ordered.Where(id => !pinnedProducts.Contains(id)))
        {
            context.CollectionItems.Add(CollectionItem.Create(
                collection.Id,
                productId,
                position++,
                isPinned: false,
                isFromRule: true,
                now));
        }

        collection.RecordRefresh(position, now);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        CollectionRefreshed(logger, collection.Slug, position, matches.Count);

        return position;
    }

    /// <summary>
    /// Brings every rule-based collection up to date for a handful of products that just changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cheap path, taken when a catalogue event arrives. It asks one question per collection per
    /// product — does this product match this rule now — and adds or removes exactly the rows that
    /// answer differently from what is stored. A store has tens of rule-based collections, not
    /// thousands, so this is tens of in-memory evaluations against projections that were fetched once.
    /// </para>
    /// <para>
    /// It appends rather than re-sorting, and that is the deliberate limitation. Getting a new
    /// product into the right position would mean re-evaluating the whole collection, which is the
    /// expensive path this one exists to avoid; the periodic sweep restores the order. A product that
    /// appears at the end of a carousel for up to half an hour is a far better outcome than a product
    /// that does not appear at all until the sweep runs.
    /// </para>
    /// </remarks>
    /// <param name="productIds">The products that changed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ApplyProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        if (productIds.Count == 0)
        {
            return;
        }

        var collections = await context.Collections
            .Where(collection => collection.Kind == CollectionKind.Rule)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (collections.Count == 0)
        {
            return;
        }

        var projections = await catalogue
            .FindByProductsAsync(productIds, cancellationToken)
            .ConfigureAwait(false);

        // One row per product: the buy box of its cheapest variant, which is the same offer a card
        // would render and therefore the offer a price condition must be about.
        var byProduct = projections
            .Where(row => row.IsBuyBox)
            .GroupBy(row => row.ProductId)
            .ToDictionary(group => group.Key, group => group.OrderBy(row => row.SellingPrice).First());

        var collectionIds = collections.Select(collection => collection.Id).ToList();

        var rows = await context.CollectionItems
            .Where(item => collectionIds.Contains(item.CollectionId) && productIds.Contains(item.ProductId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = clock.UtcNow;
        var changed = false;

        foreach (var collection in collections)
        {
            var rule = CollectionRules.Read(collection.Rules);
            var next = 0;

            foreach (var productId in productIds.Distinct())
            {
                var stored = rows.FirstOrDefault(item =>
                    item.CollectionId == collection.Id && item.ProductId == productId);

                var shouldBeIn = byProduct.TryGetValue(productId, out var projection)
                                 && (rule.IncludeOutOfStock || projection.IsPurchasable)
                                 && CollectionRuleEvaluator.Matches(rule, projection, now);

                if (shouldBeIn && stored is null)
                {
                    if (next == 0)
                    {
                        next = await NextPositionAsync(collection.Id, cancellationToken).ConfigureAwait(false);
                    }

                    context.CollectionItems.Add(CollectionItem.Create(
                        collection.Id,
                        productId,
                        next++,
                        isPinned: false,
                        isFromRule: true,
                        now));

                    changed = true;
                }
                else if (!shouldBeIn && stored is { IsPinned: false, IsFromRule: true })
                {
                    context.CollectionItems.Remove(stored);
                    changed = true;
                }
            }
        }

        if (!changed)
        {
            return;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The cached counts are corrected in a second pass rather than tracked through the loop: the
        // count is what the table holds, and reading it back is both simpler and immune to a row that
        // a unique index refused.
        await RecountAsync(collections, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes a product from every collection that holds it, pins included.
    /// </summary>
    /// <remarks>
    /// The one operation that does overrule a pin, and it is not a merchandising decision — the
    /// product is gone. A pinned card pointing at an archived product is a tile that renders as a gap
    /// or, worse, links to a 404 from the home page.
    /// </remarks>
    /// <param name="productId">The product.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RemoveProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var rows = await context.CollectionItems
            .Where(item => item.ProductId == productId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return;
        }

        var affected = rows.Select(item => item.CollectionId).Distinct().ToList();

        context.CollectionItems.RemoveRange(rows);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var collections = await context.Collections
            .Where(collection => affected.Contains(collection.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        await RecountAsync(collections, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Walks the catalogue, gathering the products a rule matches.</summary>
    private async Task<List<ProductProjection>> WalkAsync(
        CollectionRuleSet rule,
        ContentOptions settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var matches = new List<ProductProjection>();
        var seen = new HashSet<Guid>();
        var walked = 0;
        Guid? cursor = null;

        if (rule.Conditions.Count == 0)
        {
            return matches;
        }

        while (walked < settings.MaxWalkedVariants && !cancellationToken.IsCancellationRequested)
        {
            var page = await catalogue
                .EnumerateAsync(cursor, settings.CatalogWalkPageSize, cancellationToken)
                .ConfigureAwait(false);

            walked += page.VariantIds.Count;

            foreach (var projection in page.Items.Where(row => row.IsBuyBox))
            {
                if (!rule.IncludeOutOfStock && !projection.IsPurchasable)
                {
                    continue;
                }

                if (!CollectionRuleEvaluator.Matches(rule, projection, now))
                {
                    continue;
                }

                // A product with several variants offers several candidates; the cheapest one wins,
                // which is the same offer a card would render.
                var incumbent = matches.FindIndex(row => row.ProductId == projection.ProductId);

                if (incumbent < 0)
                {
                    matches.Add(projection);
                    seen.Add(projection.ProductId);
                }
                else if (projection.SellingPrice < matches[incumbent].SellingPrice)
                {
                    matches[incumbent] = projection;
                }
            }

            if (page.NextVariantCursor is null)
            {
                break;
            }

            // Enough products to satisfy the limit even after ordering, so the rest of the catalogue
            // is not read. The margin exists because the sort may prefer a product further down.
            if (seen.Count >= rule.Limit * 4 && rule.Sort == CollectionSort.Newest)
            {
                break;
            }

            cursor = page.NextVariantCursor;
        }

        return matches;
    }

    /// <summary>The position after the last row in a collection.</summary>
    private async Task<int> NextPositionAsync(Guid collectionId, CancellationToken cancellationToken)
    {
        var highest = await context.CollectionItems
            .Where(item => item.CollectionId == collectionId)
            .Select(item => (int?)item.Position)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);

        return (highest ?? -1) + 1;
    }

    /// <summary>Brings the cached item counts back in step with the rows.</summary>
    private async Task RecountAsync(
        List<ProductCollection> collections,
        CancellationToken cancellationToken)
    {
        if (collections.Count == 0)
        {
            return;
        }

        var ids = collections.Select(collection => collection.Id).ToList();

        var counts = await context.CollectionItems
            .Where(item => ids.Contains(item.CollectionId))
            .GroupBy(item => item.CollectionId)
            .Select(group => new { CollectionId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = clock.UtcNow;

        foreach (var collection in collections)
        {
            var count = counts.FirstOrDefault(row => row.CollectionId == collection.Id)?.Count ?? 0;

            collection.RecordRefresh(count, now);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 8010,
        Level = LogLevel.Information,
        Message = "Collection {Slug} refreshed: {Stored} products stored from {Matched} matches")]
    private static partial void CollectionRefreshed(ILogger logger, string slug, int stored, int matched);
}
