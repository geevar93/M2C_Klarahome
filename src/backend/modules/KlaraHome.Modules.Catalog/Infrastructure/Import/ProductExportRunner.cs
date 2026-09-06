using System.Globalization;
using System.Text;
using KlaraHome.Infrastructure.Storage;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Catalog.Infrastructure.Import;

/// <summary>
/// Writes the catalogue out in the same shape the importer reads.
/// </summary>
/// <remarks>
/// <para>
/// Round-tripping is the point: an operator exports, edits in a spreadsheet, and re-uploads the
/// same file. Any column the exporter writes that the importer does not read, or the other way
/// round, breaks that — so both are driven from <see cref="ImportColumns.All"/>.
/// </para>
/// <para>
/// Streamed in pages rather than materialised whole. A catalogue export is the one read in this
/// module with no natural bound, and building a hundred thousand rows in memory to hand to a string
/// builder is how a worker gets killed by its container's memory limit.
/// </para>
/// </remarks>
/// <param name="context">The Catalog data context.</param>
/// <param name="storage">Where the produced file goes.</param>
internal sealed class ProductExportRunner(CatalogDbContext context, IFileStorage storage)
{
    /// <summary>How many variants are read per page.</summary>
    private const int PageSize = 500;

    /// <summary>Writes the export and returns the object key it was stored under.</summary>
    /// <param name="job">The job, already claimed and tracked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string> RunAsync(CatalogJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var builder = new StringBuilder();
        builder.Append(Csv.Write([[.. ImportColumns.All]]));

        var after = Guid.Empty;
        var written = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Keyset on the variant's UUIDv7 id: no OFFSET, so page one hundred costs the same as
            // page one, and a product created mid-export cannot shift a row into a page already
            // written.
            var rows = await (
                from variant in context.Variants.AsNoTracking()
                join product in context.Products.AsNoTracking() on variant.ProductId equals product.Id
                join category in context.Categories.AsNoTracking() on product.CategoryId equals category.Id
                where variant.Id.CompareTo(after) > 0
                orderby variant.Id
                select new { variant, product, CategorySlug = category.Slug })
                .Take(PageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (rows.Count == 0)
            {
                break;
            }

            var variantIds = rows.ConvertAll(row => row.variant.Id);
            var brandIds = rows.Where(row => row.product.BrandId is not null)
                .Select(row => row.product.BrandId!.Value)
                .Distinct()
                .ToList();

            var brands = brandIds.Count == 0
                ? []
                : await context.Brands
                    .AsNoTracking()
                    .Where(brand => brandIds.Contains(brand.Id))
                    .ToDictionaryAsync(brand => brand.Id, brand => brand.Slug, cancellationToken)
                    .ConfigureAwait(false);

            // A seller's export carries their own offer against each variant; the platform's
            // carries none, because there is no single offer to name.
            var listings = job.VendorId is { } vendorId
                ? await context.Listings
                    .AsNoTracking()
                    .Where(listing => listing.VendorId == vendorId && variantIds.Contains(listing.VariantId))
                    .ToDictionaryAsync(listing => listing.VariantId, cancellationToken)
                    .ConfigureAwait(false)
                : [];

            foreach (var row in rows)
            {
                listings.TryGetValue(row.variant.Id, out var listing);
                builder.Append(Csv.Write([Row(row.variant, row.product, row.CategorySlug, brands, listing)]));
                written++;
            }

            after = rows[^1].variant.Id;
        }

        job.CountRows(written);

        for (var index = 0; index < written; index++)
        {
            job.RowSucceeded();
        }

        var key = $"catalog/exports/{job.TenantId:N}/{job.Id:N}.csv";

        using var content = new MemoryStream(Encoding.UTF8.GetBytes(builder.ToString()));

        await storage
            .PutAsync(key, content, "text/csv; charset=utf-8", StorageVisibility.Private, cancellationToken)
            .ConfigureAwait(false);

        return key;
    }

    private static List<string?> Row(
        Variant variant,
        Product product,
        string categorySlug,
        IReadOnlyDictionary<Guid, string> brands,
        Listing? listing)
        =>
        [
            variant.Sku,
            product.Name,
            product.Slug,
            categorySlug,
            product.BrandId is { } brandId ? brands.GetValueOrDefault(brandId) : null,
            product.ShortDescription,
            product.Description,
            product.HsnCode,
            Text(product.GstRate),
            product.CountryOfOrigin,
            product.Manufacturer.Name,
            product.Manufacturer.Address,
            product.Importer.Name,
            product.Importer.Address,
            product.IsReturnable ? "true" : "false",
            Text(product.ReturnWindowDays),
            variant.NameSuffix,
            variant.Barcode,
            Text(variant.Mrp.Amount),
            variant.NetQuantity,
            Text(variant.WeightGrams),
            Text(variant.LengthMm),
            Text(variant.WidthMm),
            Text(variant.HeightMm),
            listing is null ? null : Text(listing.SellingPrice.Amount),
            listing?.VendorSku,
            listing is null ? null : listing.IsCodAllowed ? "true" : "false",
            Text(listing?.MaxOrderQuantity),
        ];

    /// <summary>
    /// Renders a number for the file.
    /// </summary>
    /// <remarks>
    /// Invariant culture, always. A price written with a comma as its decimal separator is a price
    /// the importer reads as a different number, and the round trip would silently change it.
    /// </remarks>
    private static string? Text(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? Text(int? value) => value?.ToString(CultureInfo.InvariantCulture);
}
