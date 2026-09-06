using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure.BuyBox;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Catalog.Infrastructure.Directory;

/// <summary>
/// The catalogue, in the shape a projection needs (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// The wider sibling of <see cref="ProductCatalogDirectory"/>, and deliberately a separate class
/// rather than more methods on that one. They answer for different jobs: that contract prices a
/// cart line and freezes an order line, this one fills a read-model with the words a shopper
/// searches by. Merging them would mean a cart paying to read a brand name it never renders.
/// </para>
/// <para>
/// The buy box is resolved <em>here</em>, with the same resolver and the same configured rule the
/// product page uses. That is the whole reason the contract carries the flag: a consumer that
/// picked its own winner — the cheapest, say — would eventually link a search result to a page
/// showing a different seller at a different price, and nobody would be able to say which was
/// wrong.
/// </para>
/// <para>
/// Stock is not consulted, exactly as on the product page. Whether there is any is Inventory's
/// question, the consumer of this contract subscribes to <c>StockLevelChanged</c> for it, and a
/// resolver that treated "unknown" as "out of stock" would empty every buy box in the catalogue.
/// </para>
/// </remarks>
/// <param name="context">The Catalog data context.</param>
/// <param name="vendors">Resolves the sellers behind the offers.</param>
/// <param name="settings">Supplies the configured buy-box rule.</param>
internal sealed class ProductProjectionSource(
    CatalogDbContext context,
    IVendorDirectory vendors,
    IStoreSettings settings) : IProductProjectionSource
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ProductProjection>> FindByVariantsAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(variantIds);

        if (variantIds.Count == 0)
        {
            return [];
        }

        var ids = variantIds.Distinct().ToList();

        return await BuildAsync(ids, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ProductProjection>> FindByProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        if (productIds.Count == 0)
        {
            return [];
        }

        var ids = productIds.Distinct().ToList();

        // The variants first, then the ordinary build. Resolving the products' variants here rather
        // than widening the join keeps one assembly path for every caller — a second one would be a
        // second place the buy box could be resolved differently.
        var variantIds = await context.Variants
            .AsNoTracking()
            .Where(variant => ids.Contains(variant.ProductId))
            .Select(variant => variant.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return variantIds.Count == 0
            ? []
            : await BuildAsync(variantIds, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ProductProjection>> FindBySlugAsync(
        string productSlug,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productSlug))
        {
            return [];
        }

        var variantIds = await context.Variants
            .AsNoTracking()
            .Where(variant => context.Products
                .Any(product => product.Id == variant.ProductId && product.Slug == productSlug))
            .Select(variant => variant.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return variantIds.Count == 0
            ? []
            : await BuildAsync(variantIds, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<Guid, Guid>> FindVariantsOfAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(listingIds);

        if (listingIds.Count == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        var ids = listingIds.Distinct().ToList();

        var pairs = await context.Listings
            .AsNoTracking()
            .Where(listing => ids.Contains(listing.Id))
            .Select(listing => new { listing.Id, listing.VariantId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return pairs.ToDictionary(pair => pair.Id, pair => pair.VariantId);
    }

    /// <inheritdoc />
    public async ValueTask<ProductProjectionPage> EnumerateAsync(
        Guid? afterVariantId,
        int size,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(size, 1, 500);

        // Variants rather than listings, so a rebuild never sees half of a variant's offers and
        // therefore never writes a buy box that is only the winner of the page it happened to see.
        var page = await context.Variants
            .AsNoTracking()
            .Where(variant => afterVariantId == null || variant.Id.CompareTo(afterVariantId.Value) > 0)
            .OrderBy(variant => variant.Id)
            .Select(variant => variant.Id)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (page.Count == 0)
        {
            return new ProductProjectionPage([], [], null);
        }

        var items = await BuildAsync(page, cancellationToken).ConfigureAwait(false);

        // The cursor is the last variant walked, not the last one that produced a row. A variant
        // with no live offer produces nothing, and a cursor taken from the results would walk the
        // same empty stretch of catalogue for ever.
        return new ProductProjectionPage(page, items, page[^1]);
    }

    /// <summary>
    /// Assembles the projections for a set of variants: the five-table read, one directory call for
    /// every seller involved, one settings read, and one buy-box resolution per variant.
    /// </summary>
    /// <param name="variantIds">The variants.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<List<ProductProjection>> BuildAsync(
        List<Guid> variantIds,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from listing in context.Listings.AsNoTracking()
            join variant in context.Variants.AsNoTracking() on listing.VariantId equals variant.Id
            join product in context.Products.AsNoTracking() on listing.ProductId equals product.Id
            join category in context.Categories.AsNoTracking() on product.CategoryId equals category.Id
            join brand in context.Brands.AsNoTracking() on product.BrandId equals brand.Id into brands
            from brand in brands.DefaultIfEmpty()
            where variantIds.Contains(listing.VariantId)
            select new Row(
                listing.Id,
                listing.VendorId!.Value,
                variant.Id,
                product.Id,
                variant.Sku,
                product.Name,
                variant.NameSuffix == null ? product.Name : product.Name + " - " + variant.NameSuffix,
                product.Slug,
                product.ShortDescription,
                product.CategoryId,
                category.Name,
                category.Slug,
                category.Path,
                product.BrandId,
                brand == null ? null : brand.Name,
                brand == null ? null : brand.Slug,
                listing.Status == ListingStatus.Active
                    && variant.Status == VariantStatus.Active
                    && product.Status == ProductStatus.Active,
                listing.Mrp.Amount,
                listing.SellingPrice.Amount,
                listing.SellingPrice.Currency,
                product.RatingAverage,
                product.RatingCount,
                listing.IsCodAllowed,
                product.IsReturnable,
                listing.HandlingTimeHours,
                listing.PublishedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return [];
        }

        var sellers = await vendors
            .FindManyAsync([.. rows.Select(row => row.VendorId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        var rule = await settings.GetAsync<BuyBoxSettings>(cancellationToken).ConfigureAwait(false);

        var attributes = await AttributesOfAsync(
            [.. rows.Select(row => row.ProductId).Distinct()],
            [.. rows.Select(row => row.VariantId).Distinct()],
            cancellationToken).ConfigureAwait(false);

        var images = await ImagesOfAsync(rows, cancellationToken).ConfigureAwait(false);
        var winners = ResolveBuyBoxes(rows, sellers, rule);
        var result = new List<ProductProjection>(rows.Count);

        foreach (var row in rows)
        {
            // A seller the directory does not know is dropped rather than projected. The withdrawal
            // event may not have been dispatched yet, and an index that offered a seller who cannot
            // ship would send a shopper to a page that refuses their basket.
            if (!sellers.TryGetValue(row.VendorId, out var seller))
            {
                continue;
            }

            var byVariant = attributes.GetValueOrDefault(row.VariantId, []);
            var byProduct = attributes.GetValueOrDefault(row.ProductId, []);

            result.Add(new ProductProjection(
                row.ListingId,
                row.VendorId,
                seller.DisplayName,
                seller.Slug,
                seller.Rating,
                row.VariantId,
                row.ProductId,
                row.Sku,
                row.ProductName,
                row.VariantName,
                row.ProductSlug,
                row.ShortDescription,
                row.CategoryId,
                row.CategoryName,
                row.CategorySlug,
                row.CategoryPath,
                row.BrandId,
                row.BrandName,
                row.BrandSlug,
                row.IsPurchasable && seller.IsActive,
                winners.Contains(row.ListingId),
                row.Mrp,
                row.SellingPrice,
                row.CurrencyCode,
                row.RatingAverage,
                row.RatingCount,
                row.IsCodAllowed,
                row.IsReturnable,
                images.GetValueOrDefault(row.ListingId),
                row.PublishedAt,
                [.. byProduct, .. byVariant]));
        }

        return result;
    }

    /// <summary>
    /// The winning listing id for each variant, by the configured rule.
    /// </summary>
    /// <remarks>
    /// Only purchasable offers from trading sellers are candidates. A variant whose every offer is
    /// paused has no winner at all, and its consumer is expected to drop the row rather than show
    /// an offer nobody can buy.
    /// </remarks>
    /// <param name="rows">Every offer read.</param>
    /// <param name="sellers">The sellers behind them.</param>
    /// <param name="rule">The configured buy-box rule.</param>
    private static HashSet<Guid> ResolveBuyBoxes(
        List<Row> rows,
        IReadOnlyDictionary<Guid, VendorSummary> sellers,
        BuyBoxSettings rule)
    {
        var winners = new HashSet<Guid>();

        foreach (var group in rows.GroupBy(row => row.VariantId))
        {
            var candidates = new List<BuyBoxCandidate>();

            foreach (var row in group)
            {
                if (!row.IsPurchasable
                    || !sellers.TryGetValue(row.VendorId, out var seller)
                    || !seller.IsActive)
                {
                    continue;
                }

                candidates.Add(new BuyBoxCandidate(
                    row.ListingId,
                    row.VendorId,
                    row.SellingPrice,
                    seller.Rating,
                    Math.Max(row.HandlingTimeHours, seller.DispatchSlaHours),
                    HasStock: null,
                    row.PublishedAt));
            }

            if (BuyBoxResolver.Select(candidates, rule) is { } winner)
            {
                winners.Add(winner.ListingId);
            }
        }

        return winners;
    }

    /// <summary>
    /// The described properties of the products and variants involved, keyed by owner id.
    /// </summary>
    /// <remarks>
    /// Two reads and one join to the attribute vocabulary, rather than one read per row. Only
    /// attributes that are filterable or searchable are returned: an attribute that is neither is a
    /// specification a product page prints, and putting it in a projection would grow the index for
    /// nothing.
    /// </remarks>
    /// <param name="productIds">The products.</param>
    /// <param name="variantIds">The variants.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<Dictionary<Guid, List<ProductAttributeProjection>>> AttributesOfAsync(
        List<Guid> productIds,
        List<Guid> variantIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, List<ProductAttributeProjection>>();

        var productValues = await (
            from value in context.ProductAttributeValues.AsNoTracking()
            join attribute in context.Attributes.AsNoTracking() on value.AttributeId equals attribute.Id
            join option in context.AttributeOptions.AsNoTracking() on value.ValueOptionId equals option.Id into options
            from option in options.DefaultIfEmpty()
            where productIds.Contains(value.ProductId)
                  && (attribute.IsFilterable || attribute.IsSearchable)
            select new
            {
                Owner = value.ProductId,
                attribute.Code,
                attribute.Name,
                Option = option == null ? null : option.Value,
                OptionLabel = option == null ? null : option.Label,
                value.ValueText,
                value.ValueNumber,
                value.ValueBoolean,
                attribute.IsFilterable,
                attribute.IsSearchable,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var variantValues = await (
            from value in context.VariantAttributeValues.AsNoTracking()
            join attribute in context.Attributes.AsNoTracking() on value.AttributeId equals attribute.Id
            join option in context.AttributeOptions.AsNoTracking() on value.OptionId equals option.Id
            where variantIds.Contains(value.VariantId)
            select new
            {
                Owner = value.VariantId,
                attribute.Code,
                attribute.Name,
                Option = (string?)option.Value,
                OptionLabel = (string?)option.Label,
                ValueText = (string?)null,
                ValueNumber = (decimal?)null,
                ValueBoolean = (bool?)null,
                attribute.IsFilterable,
                attribute.IsSearchable,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var value in productValues.Concat(variantValues))
        {
            var raw = value.Option
                      ?? value.ValueText
                      ?? value.ValueNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                      ?? (value.ValueBoolean is { } flag ? (flag ? "true" : "false") : null);

            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var label = value.OptionLabel ?? raw;

            if (!result.TryGetValue(value.Owner, out var list))
            {
                list = [];
                result[value.Owner] = list;
            }

            list.Add(new ProductAttributeProjection(
                value.Code,
                value.Name,
                raw,
                label,
                value.IsFilterable,
                value.IsSearchable));
        }

        return result;
    }

    /// <summary>The image each offer renders: the variant's own first, the product's as a fallback.</summary>
    /// <param name="rows">The offers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<Dictionary<Guid, Guid>> ImagesOfAsync(List<Row> rows, CancellationToken cancellationToken)
    {
        var variantIds = rows.ConvertAll(row => row.VariantId);
        var productIds = rows.ConvertAll(row => row.ProductId);

        var assets = await context.MediaAssets
            .AsNoTracking()
            .Where(asset =>
                asset.Kind == CatalogMediaKind.Image
                && ((asset.VariantId != null && variantIds.Contains(asset.VariantId.Value))
                    || (asset.ProductId != null
                        && asset.VariantId == null
                        && productIds.Contains(asset.ProductId.Value))))
            .OrderBy(asset => asset.Position)
            .Select(asset => new { asset.ProductId, asset.VariantId, asset.FileId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byVariant = assets
            .Where(asset => asset.VariantId is not null)
            .GroupBy(asset => asset.VariantId!.Value)
            .ToDictionary(group => group.Key, group => group.First().FileId);

        var byProduct = assets
            .Where(asset => asset.VariantId is null && asset.ProductId is not null)
            .GroupBy(asset => asset.ProductId!.Value)
            .ToDictionary(group => group.Key, group => group.First().FileId);

        var result = new Dictionary<Guid, Guid>();

        foreach (var row in rows)
        {
            if (byVariant.TryGetValue(row.VariantId, out var variantImage))
            {
                result[row.ListingId] = variantImage;
            }
            else if (byProduct.TryGetValue(row.ProductId, out var productImage))
            {
                result[row.ListingId] = productImage;
            }
        }

        return result;
    }

    /// <summary>
    /// The five-table read, before the seller, the buy box, the attributes and the image are added.
    /// </summary>
    /// <remarks>
    /// A named type rather than an anonymous one so it can cross a method boundary, and a record so
    /// the projection is an expression tree EF can translate — the column list runs in Postgres and
    /// the bytes nobody needs, the long description and the SEO blob, are never read off the disk.
    /// </remarks>
    private sealed record Row(
        Guid ListingId,
        Guid VendorId,
        Guid VariantId,
        Guid ProductId,
        string Sku,
        string ProductName,
        string VariantName,
        string ProductSlug,
        string? ShortDescription,
        Guid CategoryId,
        string CategoryName,
        string CategorySlug,
        string CategoryPath,
        Guid? BrandId,
        string? BrandName,
        string? BrandSlug,
        bool IsPurchasable,
        decimal Mrp,
        decimal SellingPrice,
        string CurrencyCode,
        decimal? RatingAverage,
        int RatingCount,
        bool IsCodAllowed,
        bool IsReturnable,
        int HandlingTimeHours,
        DateTimeOffset? PublishedAt);
}
