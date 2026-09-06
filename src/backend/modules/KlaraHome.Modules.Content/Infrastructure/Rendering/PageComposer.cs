using System.Text.Json;
using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Content.Application;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Blocks;
using KlaraHome.Modules.Content.Infrastructure.Features;
using KlaraHome.Modules.Content.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Content.Infrastructure.Rendering;

/// <summary>
/// Turns a stored page into the document a storefront renders.
/// </summary>
/// <remarks>
/// <para>
/// Three things happen here and nowhere else. The blocks are <b>windowed</b> to the instant of the
/// request, so a campaign block appears and disappears on its own timetable without the page being
/// republished. Custom HTML is <b>withheld</b> when its flag is off, so switching the flag is a
/// remedy and not merely a prohibition on writing new ones. And every reference a block carries is
/// <b>resolved</b> — the images, the products, the categories — in one batch for the whole page
/// rather than one call per block.
/// </para>
/// <para>
/// The batching is the reason this is a class rather than a method on the feature. A home page with
/// six blocks refers to perhaps forty images, twenty-four products and eight categories; resolving
/// those per block would be eighteen round trips where three will do, and server-side rendering is
/// waiting on every one of them.
/// </para>
/// <para>
/// A collection named by a carousel is expanded here too, which is what makes "show me the sale
/// collection" a merchandising decision rather than a list of product ids somebody has to maintain.
/// The collection's own order is preserved, and its pinned rows come first, exactly as they do on the
/// collection's own page.
/// </para>
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves media, products and categories.</param>
/// <param name="flags">Decides whether custom HTML renders at all.</param>
/// <param name="options">The carousel ceiling.</param>
internal sealed class PageComposer(
    ContentDbContext context,
    ContentRenderer renderer,
    IFeatureFlags flags,
    IOptionsMonitor<ContentOptions> options)
{
    /// <summary>Composes a page for the storefront.</summary>
    /// <param name="page">The page, with its blocks loaded.</param>
    /// <param name="now">The instant the blocks are windowed to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<StorePageResponse> ComposeAsync(
        ContentPage page,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        var blocks = await ComposeBlocksAsync(page.Blocks, now, cancellationToken).ConfigureAwait(false);

        var ogImage = await renderer
            .ResolveImageAsync(page.Seo.OgImageFileId, page.Title, cancellationToken)
            .ConfigureAwait(false);

        var cover = await renderer
            .ResolveImageAsync(page.CoverImageFileId, page.Title, cancellationToken)
            .ConfigureAwait(false);

        return new StorePageResponse(
            page.Id,
            page.Slug,
            page.Type.ToString(),
            page.Title,
            page.Summary,
            ContentProjection.ToSeo(page.Seo, ogImage),
            cover,
            page.Author,
            page.Tags,
            page.PublishedAt,
            page.ContentChangedAt ?? page.UpdatedAt ?? page.CreatedAt,
            blocks);
    }

    /// <summary>Composes a set of blocks, resolving everything they refer to in one pass.</summary>
    /// <param name="source">The blocks.</param>
    /// <param name="now">The instant they are windowed to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<StoreBlockResponse>> ComposeBlocksAsync(
        IReadOnlyList<ContentBlock> source,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var customHtmlAllowed = await flags
            .IsEnabledAsync(ContentFeatures.CustomHtml, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var live = source
            .Where(block => block.IsLiveAt(now))
            .Where(block => customHtmlAllowed || block.Type != BlockType.CustomHtml)
            .OrderBy(block => block.Position)
            .ToList();

        if (live.Count == 0)
        {
            return [];
        }

        var parsed = live
            .Select(block => (Block: block, Config: ContentJson.Parse(block.Config)))
            .ToList();

        var references = parsed
            .Select(entry => BlockReferenceReader.Read(entry.Block.Type, entry.Config))
            .ToList();

        // Every collection any block names, expanded to product ids once. Two carousels pointing at
        // the same collection read it once between them.
        var collectionProducts = await ExpandCollectionsAsync(
            references.SelectMany(reference => reference.CollectionSlugs).Distinct().ToList(),
            cancellationToken).ConfigureAwait(false);

        var merged = BlockReferences.Merge(references);

        var allProducts = merged.ProductIds
            .Concat(collectionProducts.Values.SelectMany(ids => ids))
            .Distinct()
            .ToList();

        var images = await renderer.ResolveImagesAsync(merged.MediaIds, cancellationToken).ConfigureAwait(false);
        var cards = await renderer.ResolveProductsAsync(allProducts, cancellationToken).ConfigureAwait(false);
        var cardsById = cards.ToDictionary(card => card.ProductId);

        var overrides = new Dictionary<Guid, Guid>();

        foreach (var entry in parsed)
        {
            foreach (var pair in BlockReferenceReader.ReadCategoryImageOverrides(entry.Block.Type, entry.Config))
            {
                overrides[pair.Key] = pair.Value;
            }
        }

        var tiles = await renderer
            .ResolveCategoriesAsync(merged.CategoryIds, overrides, cancellationToken)
            .ConfigureAwait(false);

        var tilesById = tiles.ToDictionary(tile => tile.CategoryId);

        var composed = new List<StoreBlockResponse>(live.Count);
        var limit = options.CurrentValue.MaxCarouselProducts;

        for (var index = 0; index < parsed.Count; index++)
        {
            var (block, config) = parsed[index];
            var reference = references[index];

            composed.Add(new StoreBlockResponse(
                block.Id,
                block.Type.ToString(),
                block.Position,
                config,
                [.. reference.MediaIds.Where(images.ContainsKey).Select(id => images[id])],
                ProductsFor(reference, config, collectionProducts, cardsById, limit),
                [.. reference.CategoryIds.Where(tilesById.ContainsKey).Select(id => tilesById[id])]));
        }

        return composed;
    }

    /// <summary>The products one block shows, in the order it means them.</summary>
    /// <remarks>
    /// A block names either a collection or a list of products — the validator refuses both and
    /// refuses neither — so this is a choice between two orders rather than a merge of them. The
    /// block's own <c>limit</c> narrows the result, and the configured ceiling narrows it again,
    /// because a merchandiser typing a large number should not be able to make the home page slow.
    /// </remarks>
    private static IReadOnlyList<ProductCardResponse> ProductsFor(
        BlockReferences reference,
        JsonElement config,
        IReadOnlyDictionary<string, IReadOnlyList<Guid>> collectionProducts,
        Dictionary<Guid, ProductCardResponse> cardsById,
        int ceiling)
    {
        IEnumerable<Guid> ordered = reference.CollectionSlugs.Count > 0
            ? reference.CollectionSlugs
                .Where(collectionProducts.ContainsKey)
                .SelectMany(slug => collectionProducts[slug])
            : reference.ProductIds;

        var take = ceiling;

        if (config.ValueKind == JsonValueKind.Object
            && config.TryGetProperty("limit", out var limit)
            && limit.ValueKind == JsonValueKind.Number
            && limit.TryGetInt32(out var requested)
            && requested > 0)
        {
            take = Math.Min(take, requested);
        }

        return
        [
            .. ordered
                .Distinct()
                .Where(cardsById.ContainsKey)
                .Take(take)
                .Select(id => cardsById[id]),
        ];
    }

    /// <summary>Turns the collection slugs a page names into their product ids, in the collection's order.</summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlyList<Guid>>> ExpandCollectionsAsync(
        List<string> slugs,
        CancellationToken cancellationToken)
    {
        var expanded = new Dictionary<string, IReadOnlyList<Guid>>(StringComparer.Ordinal);

        if (slugs.Count == 0)
        {
            return expanded;
        }

        var collections = await context.Collections
            .AsNoTracking()
            .Where(collection => slugs.Contains(collection.Slug) && collection.IsActive)
            .Select(collection => new { collection.Id, collection.Slug })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (collections.Count == 0)
        {
            return expanded;
        }

        var ids = collections.Select(collection => collection.Id).ToList();
        var ceiling = options.CurrentValue.MaxCarouselProducts;

        var rows = await context.CollectionItems
            .AsNoTracking()
            .Where(item => ids.Contains(item.CollectionId))
            // Pinned rows first, then the collection's own order. The same order the collection's
            // landing page uses, so a carousel is genuinely a window onto that page.
            .OrderBy(item => item.CollectionId)
            .ThenByDescending(item => item.IsPinned)
            .ThenBy(item => item.Position)
            .Select(item => new { item.CollectionId, item.ProductId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var collection in collections)
        {
            expanded[collection.Slug] =
            [
                .. rows
                    .Where(row => row.CollectionId == collection.Id)
                    .Select(row => row.ProductId)
                    .Take(ceiling),
            ];
        }

        return expanded;
    }
}
