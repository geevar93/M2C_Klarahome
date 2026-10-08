using System.Text.Json;
using System.Text.Json.Nodes;
using KlaraHome.Contracts.Catalog;
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
/// <item>The hero takes the picture of the newest purchasable product that has one.</item>
/// <item>
/// A banner takes the first product picture in its category (descendants included, newest first),
/// or the category's own picture when no product has one. A banner with no picture is dropped, a grid
/// below its <see cref="StorefrontBlockFill.MinItems"/> is dropped, and a grid left with one banner
/// is that one banner.
/// </item>
/// <item>
/// A category tile shows the category's own picture; failing that it carries an image override
/// pointing at the first product picture in the category.
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
internal sealed class StorefrontTemplateFiller(ICatalogTaxonomy taxonomy, IProductProjectionSource products)
{
    /// <summary>The most category tiles a template shows.</summary>
    internal const int MaxTiles = 6;

    /// <summary>How many carousel products a block without a <c>limit</c> gets.</summary>
    internal const int DefaultLimit = 6;

    /// <summary>How many projections are read to find the recent products.</summary>
    private const int ProjectionWindow = 200;

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
    public async Task<IReadOnlyList<BlockBody>> FillAsync(
        StorefrontTemplate template,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);

        var needsCategories = template.Blocks.Any(block => block.Type is BlockType.CategoryTiles or BlockType.BannerGrid);
        var needsProducts = template.Blocks.Any(block =>
            block.Type is BlockType.Hero or BlockType.CategoryTiles or BlockType.BannerGrid or BlockType.ProductCarousel);

        IReadOnlyList<CategoryNode> topLevel = [];
        IReadOnlyList<Item> pool = [];

        if (needsCategories)
        {
            var nodes = await taxonomy
                .ListCategoriesAsync(activeOnly: true, cancellationToken)
                .ConfigureAwait(false);

            topLevel =
            [
                .. nodes
                    .Where(node => node.IsActive && node.ParentId is null)
                    .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase),
            ];
        }

        if (needsProducts)
        {
            var page = await products
                .EnumerateAsync(afterVariantId: null, ProjectionWindow, cancellationToken)
                .ConfigureAwait(false);

            pool =
            [
                .. page.Items
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

        var bodies = new List<BlockBody>(template.Blocks.Count);
        var shown = new HashSet<Guid>();

        foreach (var block in template.Blocks)
        {
            var config = JsonNode.Parse(block.ConfigJson)!.AsObject();
            var fill = block.Fill ?? new StorefrontBlockFill();

            switch (block.Type)
            {
                case BlockType.Hero:
                    if (pool.FirstOrDefault(item => item.ImageId is not null)?.ImageId is { } heroImage)
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
                                && FirstImage(pool, category) is { } image)
                            {
                                tile["imageFileId"] = image.ToString();
                            }

                            return (JsonNode)tile;
                        })]);
                    break;

                case BlockType.BannerGrid:
                    var banners = topLevel
                        .Select(category => (
                            Category: category,
                            Count: pool.Count(item => BelongsTo(item, category)),
                            Image: FirstImage(pool, category) ?? category.ImageFileId))
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

    /// <summary>The first (newest) product picture in a category, descendants included.</summary>
    private static Guid? FirstImage(IReadOnlyList<Item> pool, CategoryNode category)
        => pool.FirstOrDefault(item => item.ImageId is not null && BelongsTo(item, category))?.ImageId;

    /// <summary>Whether a product sits in the category or any category below it.</summary>
    private static bool BelongsTo(Item item, CategoryNode category)
        => item.CategoryId == category.Id
           || item.CategoryPath.Contains("/" + category.Id.ToString() + "/", StringComparison.OrdinalIgnoreCase);
}
