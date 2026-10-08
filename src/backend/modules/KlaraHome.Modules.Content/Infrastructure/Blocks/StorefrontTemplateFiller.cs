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
/// preview look like a shop: category tiles get the top-level categories, carousels get recent
/// products, the banner grid links to the first two categories. Everything is read through the same
/// cross-module contracts the renderer already uses (<see cref="ICatalogTaxonomy"/> and
/// <see cref="IProductProjectionSource"/>), so Content takes no dependency on Catalog's tables.
/// </para>
/// <para>
/// An empty catalogue fills nothing and that is not an error: the reference fields stay empty and the
/// result still binds in draft mode (<see cref="PageBlockBinder"/> with <c>allowIncomplete</c>). The
/// hero picture is never filled; the editor's picker does that.
/// </para>
/// </remarks>
/// <param name="taxonomy">The category tree.</param>
/// <param name="products">The product projections.</param>
internal sealed class StorefrontTemplateFiller(ICatalogTaxonomy taxonomy, IProductProjectionSource products)
{
    /// <summary>The most category tiles a template shows.</summary>
    internal const int MaxTiles = 6;

    /// <summary>The most products a template carousel is filled with.</summary>
    internal const int MaxProducts = 8;

    /// <summary>How many projections are read to find the recent products.</summary>
    private const int ProjectionWindow = 200;

    /// <summary>The blocks of a template, with the catalogue's categories and products filled in.</summary>
    /// <param name="template">The template.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<BlockBody>> FillAsync(
        StorefrontTemplate template,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);

        var needsCategories = template.Blocks.Any(block => block.Type is BlockType.CategoryTiles or BlockType.BannerGrid);
        var needsProducts = template.Blocks.Any(block => block.Type == BlockType.ProductCarousel);

        IReadOnlyList<CategoryNode> categories = [];
        IReadOnlyList<Guid> productIds = [];

        if (needsCategories)
        {
            var nodes = await taxonomy
                .ListCategoriesAsync(activeOnly: true, cancellationToken)
                .ConfigureAwait(false);

            categories =
            [
                .. nodes
                    .Where(node => node.IsActive && node.ParentId is null)
                    .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(MaxTiles),
            ];
        }

        if (needsProducts)
        {
            var page = await products
                .EnumerateAsync(afterVariantId: null, ProjectionWindow, cancellationToken)
                .ConfigureAwait(false);

            productIds =
            [
                .. page.Items
                    .Where(item => item.IsPurchasable)
                    .OrderByDescending(item => item.PublishedAt ?? DateTimeOffset.MinValue)
                    .Select(item => item.ProductId)
                    .Distinct()
                    .Take(MaxProducts),
            ];
        }

        var bodies = new List<BlockBody>(template.Blocks.Count);

        foreach (var block in template.Blocks)
        {
            var config = JsonNode.Parse(block.ConfigJson)!.AsObject();

            switch (block.Type)
            {
                case BlockType.CategoryTiles:
                    config["items"] = new JsonArray(
                        [.. categories.Select(category => (JsonNode)new JsonObject
                        {
                            ["categoryId"] = category.Id.ToString(),
                        })]);
                    break;

                case BlockType.BannerGrid:
                    config["items"] = new JsonArray(
                        [.. categories.Take(2).Select(category => (JsonNode)new JsonObject
                        {
                            ["caption"] = category.Name,
                            ["href"] = "/c/" + category.Slug,
                        })]);
                    break;

                case BlockType.ProductCarousel when productIds.Count > 0:
                    config["productIds"] = new JsonArray(
                        [.. productIds.Select(id => (JsonNode)id.ToString())]);
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
}
