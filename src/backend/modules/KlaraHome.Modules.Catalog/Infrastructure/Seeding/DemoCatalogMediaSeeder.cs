using KlaraHome.Contracts.Media;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Catalog.Infrastructure.Seeding;

/// <summary>
/// Attaches the demonstration images to the demonstration products and categories.
/// </summary>
/// <remarks>
/// <para>
/// **It only fills gaps.** A <c>DEMO-</c> product with no gallery gets its picture as the primary
/// image; a <c>demo-</c> category with no tile image gets one. A product that already has any media,
/// or a category that already has an image, is somebody's editing and is left exactly as it is - so
/// a designer who swaps in real photography does not lose it on the next deploy, and a designer who
/// deliberately clears an image gets it back only by deleting the whole demonstration row.
/// </para>
/// <para>
/// **It is a separate seeder from <see cref="DemoCatalogSeeder"/> on purpose.** That one creates rows
/// and never updates one it did not create, which is the property that makes it safe to re-run; this
/// one is the opposite shape - it edits existing rows, conditionally - and mixing the two would blur
/// which fence applies to which write. It also depends on the Media module's seeder having run
/// first, which the order below guarantees, and which the catalogue seeder does not need.
/// </para>
/// <para>
/// The images are looked up through <see cref="IDemoMediaCatalogue"/>; an empty answer (storage
/// unconfigured, demo data off in Media) attaches nothing and says so.
/// </para>
/// <para>
/// See <see cref="DemoDataOptions"/> for why a demo seeder exists at all and what fences it.
/// </para>
/// </remarks>
/// <param name="context">The Catalog data context.</param>
/// <param name="media">Finds the stored demonstration images.</param>
/// <param name="environment">Refuses to run in Production whatever configuration says.</param>
/// <param name="logger">Reports what was attached.</param>
internal sealed partial class DemoCatalogMediaSeeder(
    CatalogDbContext context,
    IDemoMediaCatalogue media,
    IHostEnvironment environment,
    ILogger<DemoCatalogMediaSeeder> logger) : IDataSeeder
{
    private const string DemoSkuPrefix = "DEMO-";

    /// <inheritdoc />
    public string Name => "Catalog.DemoCatalogueMedia";

    /// <summary>After the catalogue (910) and the image upload (905), before the search index (920).</summary>
    public int Order => 912;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        // The second fence. See DemoVendorSeeder for why registration alone is not enough.
        if (environment.IsProduction())
        {
            return;
        }

        var images = await media.ListAsync(cancellationToken).ConfigureAwait(false);

        if (images.Count == 0)
        {
            NoImages(logger);
            return;
        }

        var products = await AttachProductImagesAsync(images, cancellationToken).ConfigureAwait(false);
        var categories = await AttachCategoryImagesAsync(images, cancellationToken).ConfigureAwait(false);

        if (products + categories > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            ImagesAttached(logger, products, categories);
        }
    }

    /// <summary>Gives each media-less demonstration product its picture as the primary image.</summary>
    private async Task<int> AttachProductImagesAsync(
        IReadOnlyDictionary<string, Guid> images,
        CancellationToken cancellationToken)
    {
        // One demonstration SKU per product, so the SKU names the picture.
        var variants = await context.Variants
            .AsNoTracking()
            .Where(variant => variant.Sku.StartsWith(DemoSkuPrefix))
            .Select(variant => new { variant.ProductId, variant.Sku })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var productIds = variants.ConvertAll(variant => variant.ProductId);

        var withMedia = await context.MediaAssets
            .AsNoTracking()
            .Where(asset => asset.ProductId != null && productIds.Contains(asset.ProductId.Value))
            .Select(asset => asset.ProductId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var skipped = withMedia.ToHashSet();
        var done = new HashSet<Guid>();
        var attached = 0;

        var names = await context.Products
            .AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, product => product.Name, cancellationToken)
            .ConfigureAwait(false);

        foreach (var variant in variants)
        {
            if (skipped.Contains(variant.ProductId)
                || !done.Add(variant.ProductId)
                || !images.TryGetValue("demo-product-" + variant.Sku, out var fileId))
            {
                continue;
            }

            context.MediaAssets.Add(CatalogMediaAsset.ForProduct(
                variant.ProductId,
                fileId,
                CatalogMediaKind.Image,
                names.GetValueOrDefault(variant.ProductId),
                position: 0));

            attached++;
        }

        return attached;
    }

    /// <summary>Gives each image-less demonstration category its tile image.</summary>
    private async Task<int> AttachCategoryImagesAsync(
        IReadOnlyDictionary<string, Guid> images,
        CancellationToken cancellationToken)
    {
        var categories = await context.Categories
            .Where(category => category.Slug.StartsWith("demo-") && category.ImageFileId == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var attached = 0;

        foreach (var category in categories)
        {
            if (!images.TryGetValue("demo-category-" + category.Slug, out var fileId))
            {
                continue;
            }

            category.Describe(
                category.Name,
                category.Slug,
                category.Description,
                fileId,
                category.AttributeSetId,
                category.Seo);

            attached++;
        }

        return attached;
    }

    [LoggerMessage(
        EventId = 9211,
        Level = LogLevel.Information,
        Message = "Attached demonstration images to {Products} product(s) and {Categories} categor(ies).")]
    private static partial void ImagesAttached(ILogger logger, int products, int categories);

    [LoggerMessage(
        EventId = 9212,
        Level = LogLevel.Information,
        Message = "No demonstration images are in the media library, so none were attached.")]
    private static partial void NoImages(ILogger logger);
}
