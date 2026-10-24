using System.Text.Json;
using System.Text.Json.Nodes;
using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Media;
using KlaraHome.Modules.Content.Application;
using KlaraHome.Modules.Content.Domain;

namespace KlaraHome.Modules.Content.Infrastructure.Blocks;

/// <summary>
/// Turns a storefront template into blocks that are alive on the first render.
/// </summary>
/// <remarks>
/// <para>
/// A template on its own is copy and structure. Filling it from the catalogue is what makes the
/// preview look like a shop: the hero gets a product picture, category tiles get the top-level
/// categories with a picture each, banners link to the best-stocked categories, carousels get recent
/// products. Everything is read through the same cross-module contracts the renderer already uses
/// (<see cref="ICatalogTaxonomy"/> and <see cref="IProductProjectionSource"/>), so Content takes no
/// dependency on Catalog's tables.
/// </para>
/// <para>
/// The rules, in one place:
/// </para>
/// <list type="bullet">
/// <item>
/// The hero takes, in order: the picture of the hero the current home page already has, the
/// demonstration hero picture (<c>demo-hero</c>), the newest purchasable product picture.
/// </item>
/// <item>
/// A category tile shows, in order: the category's own picture; the picture the current home page's
/// tile for that category already overrides it with; the demonstration category picture
/// (<c>demo-category-&lt;slug&gt;</c>); a picture a category below it carries; an override pointing at the
/// first product picture in the category.
/// </item>
/// <item>
/// A banner takes, in order: the category's own picture, the demonstration category picture, a
/// picture a category below it carries, the
/// second product picture in the category (so it never repeats a tile's product shot; the first when
/// the category has only one). A banner grid skips the categories a category-tiles block on the same
/// template already shows. A banner with no picture is dropped, a grid below its
/// <see cref="StorefrontBlockFill.MinItems"/> is dropped, and a grid left with one banner is that one
/// banner.
/// </item>
/// <item>
/// A carousel holds at most its own <c>limit</c> products, newest first, within its price cap if it
/// has one, and prefers products no earlier carousel on the page already shows. A capped carousel
/// with too few qualifying products is dropped.
/// </item>
/// </list>
/// <para>
/// The projections carry <c>SellingPrice</c> (inclusive of GST) and <c>PrimaryImageFileId</c>, so
/// those are what the price cap and the pictures use. An empty catalogue fills nothing and that is
/// not an error: reference fields stay empty and the result still binds in draft mode
/// (<see cref="PageBlockBinder"/> with <c>allowIncomplete</c>), except that a banner grid with no
/// picture to show is dropped.
/// </para>
/// </remarks>
/// <param name="taxonomy">The category tree.</param>
/// <param name="products">The product projections.</param>
/// <param name="media">Finds the demonstration pictures, if this deployment has them.</param>
internal sealed class StorefrontTemplateFiller(
    ICatalogTaxonomy taxonomy,
    IProductProjectionSource products,
    IDemoMediaCatalogue media)
{
    /// <summary>The most category tiles a template shows.</summary>
    internal const int MaxTiles = 6;

    /// <summary>How many carousel products a block without a <c>limit</c> gets.</summary>
    internal const int DefaultLimit = 6;

    /// <summary>How many variants each page of the catalogue walk reads.</summary>
    private const int ProjectionWindow = 200;

    /// <summary>The most pages the walk reads (10,000 variants), so a preview stays bounded.</summary>
    private const int MaxProjectionPages = 50;

    /// <summary>One product, reduced to what the filler needs.</summary>
    private sealed record Item(
        Guid ProductId,
        string CategoryPath,
        Guid CategoryId,
        decimal Price,
        Guid? ImageId,
        DateTimeOffset PublishedAt);

    /// <summary>The blocks of a template, with the catalogue filled in.</summary>
    /// <param name="template">The template.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="current">The home page being replaced, whose hero and tile pictures are kept; null when there is none.</param>
    public async Task<IReadOnlyList<BlockBody>> FillAsync(
        StorefrontTemplate template,
        CancellationToken cancellationToken,
        ContentPage? current = null)
    {
        ArgumentNullException.ThrowIfNull(template);

        var needsCategories = template.Blocks.Any(block => block.Type is BlockType.CategoryTiles or BlockType.BannerGrid);
        var needsProducts = template.Blocks.Any(block =>
            block.Type is BlockType.Hero or BlockType.CategoryTiles or BlockType.BannerGrid or BlockType.ProductCarousel);

        IReadOnlyList<CategoryNode> topLevel = [];
        IReadOnlyList<CategoryNode> active = [];
        IReadOnlyList<Item> pool = [];

        if (needsCategories)
        {
            var nodes = await taxonomy
                .ListCategoriesAsync(activeOnly: true, cancellationToken)
                .ConfigureAwait(false);

            active = [.. nodes.Where(node => node.IsActive)];

            topLevel =
            [
                .. nodes
                    .Where(node => node.IsActive && node.ParentId is null)
                    .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase),
            ];
        }

        if (needsProducts)
        {
            // The walk is in variant-id order, which is oldest first, so "newest" can only be decided
            // after reading all of it. One page alone would fill a large catalogue from its oldest
            // products.
            var items = new List<ProductProjection>();
            Guid? cursor = null;

            for (var pages = 0; pages < MaxProjectionPages; pages++)
            {
                var page = await products
                    .EnumerateAsync(cursor, ProjectionWindow, cancellationToken)
                    .ConfigureAwait(false);

                items.AddRange(page.Items);
                cursor = page.NextVariantCursor;

                if (cursor is null)
                {
                    break;
                }
            }

            pool =
            [
                .. items
                    .Where(item => item.IsPurchasable)
                    .GroupBy(item => item.ProductId)
                    .Select(group =>
                    {
                        var rep = group.FirstOrDefault(item => item.IsBuyBox) ?? group.First();

                        return new Item(
                            group.Key,
                            rep.CategoryPath,
                            rep.CategoryId,
                            rep.SellingPrice,
                            rep.PrimaryImageFileId
                                ?? group.Select(item => item.PrimaryImageFileId).FirstOrDefault(id => id is not null),
                            group.Max(item => item.PublishedAt) ?? DateTimeOffset.MinValue);
                    })
                    .OrderByDescending(item => item.PublishedAt),
            ];
        }

        IReadOnlyDictionary<string, Guid> demo = new Dictionary<string, Guid>();

        if (template.Blocks.Any(block => block.Type is BlockType.Hero or BlockType.CategoryTiles or BlockType.BannerGrid))
        {
            demo = await media.ListAsync(cancellationToken).ConfigureAwait(false);
        }

        var (currentHero, currentTiles) = ReadCurrent(current);
        HashSet<Guid> tileCategories = template.Blocks.Any(block => block.Type == BlockType.CategoryTiles)
            ? [.. topLevel.Take(MaxTiles).Select(category => category.Id)]
            : [];

        var bodies = new List<BlockBody>(template.Blocks.Count);
        var shown = new HashSet<Guid>();

        foreach (var block in template.Blocks)
        {
            var config = JsonNode.Parse(block.ConfigJson)!.AsObject();
            var fill = block.Fill ?? new StorefrontBlockFill();

            switch (block.Type)
            {
                case BlockType.Hero:
                    if ((currentHero
                         ?? (demo.TryGetValue("demo-hero", out var demoHero) ? demoHero : (Guid?)null)
                         ?? pool.FirstOrDefault(item => item.ImageId is not null)?.ImageId) is { } heroImage)
                    {
                        config["imageFileId"] = heroImage.ToString();
                    }

                    break;

                case BlockType.CategoryTiles:
                    config["items"] = new JsonArray(
                        [.. topLevel.Take(MaxTiles).Select(category =>
                        {
                            var tile = new JsonObject { ["categoryId"] = category.Id.ToString() };

                            if (category.ImageFileId is null
                                && (CurrentTile(currentTiles, category)
                                    ?? DemoCategory(demo, category)
                                    ?? FromBelow(active, demo, category)
                                    ?? FirstImage(pool, category)) is { } image)
                            {
                                tile["imageFileId"] = image.ToString();
                            }

                            return (JsonNode)tile;
                        })]);
                    break;

                case BlockType.BannerGrid:
                    var banners = topLevel
                        .Where(category => !tileCategories.Contains(category.Id))
                        .Select(category => (
                            Category: category,
                            Count: pool.Count(item => BelongsTo(item, category)),
                            Image: category.ImageFileId ?? DemoCategory(demo, category) ?? FromBelow(active, demo, category) ?? BannerImage(pool, category)))
                        .Where(candidate => candidate.Image is not null)
                        .OrderByDescending(candidate => candidate.Count)
                        .ThenBy(candidate => candidate.Category.Name, StringComparer.OrdinalIgnoreCase)
                        .Skip(fill.Skip)
                        .Take(fill.Take)
                        .ToList();

                    if (banners.Count < Math.Max(1, fill.MinItems))
                    {
                        continue;
                    }

                    config["items"] = new JsonArray(
                        [.. banners.Select(candidate => (JsonNode)new JsonObject
                        {
                            ["imageFileId"] = candidate.Image!.Value.ToString(),
                            ["imageAlt"] = candidate.Category.Name,
                            ["caption"] = candidate.Category.Name,
                            ["href"] = "/c/" + candidate.Category.Slug,
                        })]);
                    break;

                case BlockType.ProductCarousel:
                    var limit = config["limit"]?.GetValue<int>() ?? DefaultLimit;
                    var qualifying = pool
                        .Where(item => fill.MaxPrice is not { } cap || item.Price <= cap)
                        .ToList();

                    if (fill.MaxPrice is not null && qualifying.Count < Math.Max(1, fill.MinItems))
                    {
                        continue;
                    }

                    // Newest first, but products an earlier carousel already shows come last, so
                    // two carousels only share products when the catalogue is too small to avoid it.
                    var chosen = qualifying
                        .Where(item => !shown.Contains(item.ProductId))
                        .Concat(qualifying.Where(item => shown.Contains(item.ProductId)))
                        .Take(limit)
                        .Select(item => item.ProductId)
                        .ToList();

                    shown.UnionWith(chosen);

                    if (chosen.Count > 0)
                    {
                        config["productIds"] = new JsonArray([.. chosen.Select(id => (JsonNode)id.ToString())]);
                    }

                    break;

                default:
                    break;
            }

            bodies.Add(new BlockBody(
                Id: null,
                block.Type.ToString(),
                JsonSerializer.SerializeToElement(config),
                IsVisible: true,
                StartsAt: null,
                EndsAt: null));
        }

        return bodies;
    }

    /// <summary>The hero picture and the per-category tile overrides of the page being replaced.</summary>
    private static (Guid? Hero, Dictionary<Guid, Guid> Tiles) ReadCurrent(ContentPage? page)
    {
        Guid? hero = null;
        var tiles = new Dictionary<Guid, Guid>();

        foreach (var block in (page?.Blocks ?? []).OrderBy(block => block.Position))
        {
            JsonNode? node;

            try
            {
                node = JsonNode.Parse(block.Config);
            }
            catch (JsonException)
            {
                continue;
            }

            if (node is not JsonObject config)
            {
                continue;
            }

            if (block.Type == BlockType.Hero)
            {
                if (hero is null
                    && Guid.TryParse(config["imageFileId"]?.GetValue<string>(), out var heroId)
                    && heroId != Guid.Empty)
                {
                    hero = heroId;
                }
            }
            else if (block.Type == BlockType.CategoryTiles && config["items"] is JsonArray items)
            {
                foreach (var item in items.OfType<JsonObject>())
                {
                    if (Guid.TryParse(item["categoryId"]?.GetValue<string>(), out var categoryId)
                        && Guid.TryParse(item["imageFileId"]?.GetValue<string>(), out var imageId)
                        && imageId != Guid.Empty)
                    {
                        tiles.TryAdd(categoryId, imageId);
                    }
                }
            }
        }

        return (hero, tiles);
    }

    /// <summary>The picture the current home page gives a category's tile, if any.</summary>
    private static Guid? CurrentTile(Dictionary<Guid, Guid> tiles, CategoryNode category)
    => tiles.TryGetValue(category.Id, out var id) ? id : null;

    /// <summary>
    /// A picture a category's descendants carry, own picture before demonstration picture, nearest and
    /// then alphabetically first. A top-level category usually has no picture of its own while the
    /// categories under it do, and either is a better face for it than a product shot.
    /// </summary>
    private static Guid? FromBelow(IReadOnlyList<CategoryNode> active, IReadOnlyDictionary<string, Guid> demo, CategoryNode category)
        => active
            .Where(node => node.Id != category.Id
                           && node.Path.Contains("/" + category.Id.ToString() + "/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(node => node.Level)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .Select(node => node.ImageFileId ?? DemoCategory(demo, node))
            .FirstOrDefault(id => id is not null);

    /// <summary>The demonstration picture for a category, when this deployment has it.</summary>
    private static Guid? DemoCategory(IReadOnlyDictionary<string, Guid> demo, CategoryNode category)
        => demo.TryGetValue("demo-category-" + category.Slug, out var id) ? id : null;

    /// <summary>The second product picture in a category (the first when there is only one).</summary>
    private static Guid? BannerImage(IReadOnlyList<Item> pool, CategoryNode category)
    {
        var images = pool
            .Where(item => item.ImageId is not null && BelongsTo(item, category))
            .Select(item => item.ImageId!.Value)
            .Distinct()
            .Take(2)
            .ToList();

        return images.Count == 0 ? null : images[^1];
    }

    /// <summary>The first (newest) product picture in a category, descendants included.</summary>
    private static Guid? FirstImage(IReadOnlyList<Item> pool, CategoryNode category)
        => pool.FirstOrDefault(item => item.ImageId is not null && BelongsTo(item, category))?.ImageId;

    /// <summary>Whether a product sits in the category or any category below it.</summary>
    private static bool BelongsTo(Item item, CategoryNode category)
        => item.CategoryId == category.Id
           || item.CategoryPath.Contains("/" + category.Id.ToString() + "/", StringComparison.OrdinalIgnoreCase);
}
