using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Media;
using KlaraHome.Modules.Content.Application;

namespace KlaraHome.Modules.Content.Infrastructure.Rendering;

/// <summary>
/// Turns the ids the CMS stores into the things a storefront can render.
/// </summary>
/// <remarks>
/// <para>
/// The CMS stores references and nothing else: a block holds a media id, a collection holds product
/// ids, a category tile holds a category id. None of those may be joined to — they live in other
/// schemas — so something has to resolve them, and this is the single place that does. One class
/// rather than three because they are always needed together: a product card is a product and its
/// picture, and a carousel that resolved products here and images somewhere else would make two
/// passes over the same page.
/// </para>
/// <para>
/// Everything here is batched. A home page with six blocks can easily refer to forty images and
/// twenty-four products, and the difference between two calls and sixty-four is the difference
/// between a server-rendered page and a timeout.
/// </para>
/// <para>
/// A reference that resolves to nothing is dropped, never fatal. A product that was archived after a
/// merchandiser pinned it, or an image somebody deleted from the library, must not be able to take
/// the home page down — the card simply is not rendered, and the admin screen shows the editor which
/// references are dead.
/// </para>
/// </remarks>
/// <param name="media">Resolves stored files into URLs and renditions.</param>
/// <param name="catalogue">Resolves products, with the buy box the catalogue itself picked.</param>
/// <param name="taxonomy">Resolves categories, for a tile and for a breadcrumb.</param>
internal sealed class ContentRenderer(
    IMediaLibrary media,
    IProductProjectionSource catalogue,
    ICatalogTaxonomy taxonomy)
{
    /// <summary>Resolves a set of media files, keyed by id. Unknown ids are absent.</summary>
    /// <param name="fileIds">The files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyDictionary<Guid, ContentImageResponse>> ResolveImagesAsync(
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileIds);

        if (fileIds.Count == 0)
        {
            return new Dictionary<Guid, ContentImageResponse>();
        }

        var files = await media.GetManyAsync(fileIds, cancellationToken).ConfigureAwait(false);

        return files.ToDictionary(entry => entry.Key, entry => ToImage(entry.Value, alt: null));
    }

    /// <summary>Resolves one media file, or null when it is unknown.</summary>
    /// <param name="fileId">The file, or null.</param>
    /// <param name="alt">The alt text the referring record carries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ContentImageResponse?> ResolveImageAsync(
        Guid? fileId,
        string? alt,
        CancellationToken cancellationToken)
    {
        if (fileId is null)
        {
            return null;
        }

        var file = await media.GetAsync(fileId.Value, cancellationToken).ConfigureAwait(false);

        return file is null ? null : ToImage(file, alt);
    }

    /// <summary>
    /// Resolves a set of products into cards, in the order they were named.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One card per product, and choosing which of a product's variants the card is for is the only
    /// judgement in this class. It takes the buy-box offer of the <b>cheapest purchasable variant</b>
    /// — which is the number a shopper reads as "from ₹—" and the variant a product page opens on.
    /// Taking the first variant by id would make the price on a card depend on the order rows were
    /// inserted, which is a card whose price changes for no reason a merchandiser can explain.
    /// </para>
    /// <para>
    /// The <em>seller</em> is never chosen here. Which offer wins a variant is a rule the Catalog
    /// module owns and resolves, and this method only ever reads the flag it set — so a card and the
    /// product page it links to cannot name two sellers.
    /// </para>
    /// </remarks>
    /// <param name="productIds">The products, in the order they should render.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<ProductCardResponse>> ResolveProductsAsync(
        IReadOnlyList<Guid> productIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        if (productIds.Count == 0)
        {
            return [];
        }

        var projections = await catalogue
            .FindByProductsAsync(productIds, cancellationToken)
            .ConfigureAwait(false);

        var chosen = new Dictionary<Guid, ProductProjection>();

        foreach (var projection in projections.Where(row => row.IsBuyBox))
        {
            if (!chosen.TryGetValue(projection.ProductId, out var incumbent)
                || IsBetterCard(projection, incumbent))
            {
                chosen[projection.ProductId] = projection;
            }
        }

        var imageIds = chosen.Values
            .Where(row => row.PrimaryImageFileId is not null)
            .Select(row => row.PrimaryImageFileId!.Value)
            .Distinct()
            .ToList();

        var images = await ResolveImagesAsync(imageIds, cancellationToken).ConfigureAwait(false);

        var cards = new List<ProductCardResponse>(productIds.Count);

        // Driven by the caller's order rather than by the query's. A carousel's order is a
        // merchandising decision and the database has no opinion about it.
        foreach (var productId in productIds)
        {
            if (!chosen.TryGetValue(productId, out var row))
            {
                continue;
            }

            cards.Add(ToCard(row, Lookup(images, row.PrimaryImageFileId, row.ProductName)));
        }

        return cards;
    }

    /// <summary>Resolves a set of categories into tiles, in the order they were named.</summary>
    /// <param name="categoryIds">The categories.</param>
    /// <param name="overrides">Tile images an editor chose instead of the category's own, by category.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<CategoryTileResponse>> ResolveCategoriesAsync(
        IReadOnlyList<Guid> categoryIds,
        IReadOnlyDictionary<Guid, Guid>? overrides,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(categoryIds);

        if (categoryIds.Count == 0)
        {
            return [];
        }

        var nodes = await taxonomy.ListCategoriesAsync(activeOnly: true, cancellationToken).ConfigureAwait(false);
        var byId = nodes.ToDictionary(node => node.Id);

        var imageIds = new List<Guid>();

        foreach (var categoryId in categoryIds)
        {
            var chosen = ResolveTileImage(categoryId, byId, overrides);

            if (chosen is not null && !imageIds.Contains(chosen.Value))
            {
                imageIds.Add(chosen.Value);
            }
        }

        var images = await ResolveImagesAsync(imageIds, cancellationToken).ConfigureAwait(false);

        var tiles = new List<CategoryTileResponse>(categoryIds.Count);

        foreach (var categoryId in categoryIds)
        {
            if (!byId.TryGetValue(categoryId, out var node))
            {
                continue;
            }

            var imageId = ResolveTileImage(categoryId, byId, overrides);

            tiles.Add(new CategoryTileResponse(
                node.Id,
                node.Name,
                node.Slug,
                Lookup(images, imageId, node.Name)));
        }

        return tiles;
    }

    /// <summary>Turns a media file into the shape a page carries.</summary>
    /// <param name="file">The stored file.</param>
    /// <param name="alt">The alt text the referring record carries.</param>
    internal static ContentImageResponse ToImage(MediaFile file, string? alt)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new ContentImageResponse(
            file.Id,
            file.Url,
            file.Width,
            file.Height,
            alt,
            [.. file.Variants.Select(variant =>
                new ContentImageVariantResponse(variant.Name, variant.Width, variant.Url))]);
    }

    /// <summary>Turns a resolved offer into the card a carousel or a collection renders.</summary>
    /// <param name="row">The projection, already known to be the buy box.</param>
    /// <param name="image">Its picture, resolved.</param>
    internal static ProductCardResponse ToCard(ProductProjection row, ContentImageResponse? image)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new ProductCardResponse(
            row.ProductId,
            row.VariantId,
            row.ListingId,
            row.ProductName,
            row.ProductSlug,
            row.BrandName,
            row.Mrp,
            row.SellingPrice,
            row.CurrencyCode,
            DiscountPercent(row.Mrp, row.SellingPrice),
            row.RatingAverage,
            row.RatingCount,
            image,
            row.IsPurchasable);
    }

    /// <summary>How far below MRP a price sits, to two places.</summary>
    /// <remarks>
    /// Computed rather than stored, because it is entirely a function of two numbers that are stored
    /// — and a stored copy would be a third number to keep in step with both of them.
    /// </remarks>
    /// <param name="mrp">Maximum retail price.</param>
    /// <param name="price">What is being asked.</param>
    internal static decimal DiscountPercent(decimal mrp, decimal price)
        => mrp <= 0m || price >= mrp ? 0m : Math.Round((mrp - price) / mrp * 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>Whether one offer makes a better card for a product than another.</summary>
    private static bool IsBetterCard(ProductProjection candidate, ProductProjection incumbent)
    {
        // Purchasable beats not, whatever the prices. A card showing the cheapest variant of a
        // product when that variant is out of stock is a card that opens on a page saying so.
        if (candidate.IsPurchasable != incumbent.IsPurchasable)
        {
            return candidate.IsPurchasable;
        }

        return candidate.SellingPrice < incumbent.SellingPrice;
    }

    /// <summary>An editor's chosen tile image, or the category's own.</summary>
    private static Guid? ResolveTileImage(
        Guid categoryId,
        Dictionary<Guid, CategoryNode> byId,
        IReadOnlyDictionary<Guid, Guid>? overrides)
    {
        if (overrides is not null && overrides.TryGetValue(categoryId, out var chosen))
        {
            return chosen;
        }

        return byId.TryGetValue(categoryId, out var node) ? node.ImageFileId : null;
    }

    /// <summary>Finds a resolved image, attaching the alt text the caller knows about.</summary>
    private static ContentImageResponse? Lookup(
        IReadOnlyDictionary<Guid, ContentImageResponse> images,
        Guid? fileId,
        string? alt)
        => fileId is not null && images.TryGetValue(fileId.Value, out var image)
            ? image with { Alt = alt }
            : null;
}
