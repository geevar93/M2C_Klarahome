using KlaraHome.Contracts.Catalog;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Catalog.Infrastructure.Directory;

/// <summary>
/// The published read of <c>catalog.listings</c> (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// Inventory keys stock on a listing id, a cart line holds one and an order line freezes a
/// snapshot of one. None of them may join to this schema, so every question they have is answered
/// here — and it is a projection, not an entity handed across a boundary.
/// </para>
/// <para>
/// The projection is an expression tree so the join and the column list run in Postgres, and the
/// bytes a caller does not need — descriptions, SEO blobs, specification JSON — are never read off
/// the disk at all.
/// </para>
/// </remarks>
/// <param name="context">The Catalog data context.</param>
internal sealed class ProductCatalogDirectory(CatalogDbContext context) : IProductCatalog
{
    /// <inheritdoc />
    public async ValueTask<ListingSummary?> FindListingAsync(
        Guid listingId,
        CancellationToken cancellationToken = default)
    {
        var summary = await Query(context.Listings.AsNoTracking().Where(listing => listing.Id == listingId))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (summary is null)
        {
            return null;
        }

        var resolved = await WithImageAsync([summary], cancellationToken).ConfigureAwait(false);

        return resolved[0];
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<Guid, ListingSummary>> FindListingsAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(listingIds);

        if (listingIds.Count == 0)
        {
            return new Dictionary<Guid, ListingSummary>();
        }

        var ids = listingIds.Distinct().ToList();

        var rows = await Query(context.Listings.AsNoTracking().Where(listing => ids.Contains(listing.Id)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var resolved = await WithImageAsync(rows, cancellationToken).ConfigureAwait(false);

        return resolved.ToDictionary(row => row.ListingId);
    }

    /// <summary>
    /// The four-table join every question in this contract needs, without the image.
    /// </summary>
    /// <remarks>
    /// The category join is the fourth table and it is there for the materialised path, not for the
    /// id: a promotion scoped to a parent category has to apply to every descendant, and the path
    /// is how Pricing answers that without asking this module to walk the tree per cart line.
    /// <see cref="ListingSummary.IsPurchasable"/> is computed in SQL rather than reconstructed by
    /// each caller: an offer is purchasable only when it, its variant and its product are all live,
    /// and three separate booleans on the wire would be three chances to combine them differently.
    /// Whether there is any <em>stock</em> is Inventory's question, deliberately not this one.
    /// </remarks>
    /// <remarks>
    /// The caller passes the listings it wants rather than filtering the result, because a
    /// predicate over <see cref="ListingSummary"/> is a predicate over a constructed record:
    /// PostgreSQL cannot see inside one, so EF refuses to translate the query at all.
    /// </remarks>
    /// <param name="listings">The listings to project, already narrowed.</param>
    private IQueryable<ListingSummary> Query(IQueryable<Listing> listings)
        => from listing in listings
           join variant in context.Variants.AsNoTracking() on listing.VariantId equals variant.Id
           join product in context.Products.AsNoTracking() on listing.ProductId equals product.Id
           join category in context.Categories.AsNoTracking() on product.CategoryId equals category.Id
           select new ListingSummary(
               listing.Id,
               listing.VendorId!.Value,
               variant.Id,
               product.Id,
               product.CategoryId,
               category.Path,
               product.BrandId,
               variant.Sku,
               variant.NameSuffix == null ? product.Name : product.Name + " - " + variant.NameSuffix,
               listing.Status == ListingStatus.Active
                   && variant.Status == VariantStatus.Active
                   && product.Status == ProductStatus.Active,
               listing.Mrp.Amount,
               listing.SellingPrice.Amount,
               product.HsnCode,
               product.GstRate,
               variant.WeightGrams,
               listing.IsCodAllowed,
               listing.MaxOrderQuantity,
               listing.HandlingTimeHours,
               product.IsReturnable,
               product.ReturnWindowDays,
               null);

    /// <summary>
    /// Fills in the image a cart line or an order line renders: the variant's own first, and the
    /// product's as a fallback.
    /// </summary>
    /// <remarks>
    /// A second query rather than a correlated subquery in the projection, because "the first image
    /// of either owner, by position" is a per-row ordering that Postgres would evaluate once per
    /// listing. One read for the whole batch is what makes a fifty-line cart cheap.
    /// </remarks>
    private async Task<List<ListingSummary>> WithImageAsync(
        List<ListingSummary> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return rows;
        }

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

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];

            var file = byVariant.TryGetValue(row.VariantId, out var variantImage)
                ? variantImage
                : byProduct.TryGetValue(row.ProductId, out var productImage) ? productImage : (Guid?)null;

            rows[index] = row with { PrimaryImageFileId = file };
        }

        return rows;
    }
}
