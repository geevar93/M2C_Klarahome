using System.Globalization;
using KlaraHome.Contracts.Media;
using KlaraHome.Modules.Catalog.Application.Taxonomy;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Catalog.Application.Products;

/// <summary>
/// Assembles the full picture of a product: its attribute values with their labels, its gallery
/// with resolved URLs, and its variants with theirs.
/// </summary>
/// <remarks>
/// <para>
/// One service rather than a projection per endpoint, because the admin product page, the
/// storefront PDP and the export all need the same assembly and the three would otherwise drift on
/// which attribute labels they resolve and which media they include.
/// </para>
/// <para>
/// Everything is loaded in a fixed number of queries — attributes, options, media and variants,
/// one read each, for however many products are in hand. The reason is the product listing page,
/// which asks for twenty-four products at once: a per-product read would make it twenty-four times
/// four round trips.
/// </para>
/// </remarks>
/// <param name="context">The Catalog data context.</param>
/// <param name="media">Resolves <c>media.files</c> ids to URLs (ADR-016).</param>
internal sealed class ProductReader(CatalogDbContext context, IMediaLibrary media)
{
    /// <summary>The whole picture of one product, or null when there is no such product.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ProductResponse?> ReadAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == productId, cancellationToken)
            .ConfigureAwait(false);

        if (product is null)
        {
            return null;
        }

        var detail = await LoadAsync([product], cancellationToken).ConfigureAwait(false);

        return detail[product.Id];
    }

    /// <summary>The whole picture of several products, keyed by id.</summary>
    /// <param name="products">The products, already loaded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Dictionary<Guid, ProductResponse>> LoadAsync(
        IReadOnlyList<Product> products,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(products);

        if (products.Count == 0)
        {
            return [];
        }

        var productIds = products.Select(product => product.Id).ToList();

        var variants = await context.Variants
            .AsNoTracking()
            .Where(variant => productIds.Contains(variant.ProductId))
            .OrderBy(variant => variant.Position)
            .ThenBy(variant => variant.Sku)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var variantIds = variants.ConvertAll(variant => variant.Id);

        var productValues = await context.ProductAttributeValues
            .AsNoTracking()
            .Where(value => productIds.Contains(value.ProductId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var variantValues = await context.VariantAttributeValues
            .AsNoTracking()
            .Where(value => variantIds.Contains(value.VariantId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var assets = await context.MediaAssets
            .AsNoTracking()
            .Where(asset =>
                (asset.ProductId != null && productIds.Contains(asset.ProductId.Value))
                || (asset.VariantId != null && variantIds.Contains(asset.VariantId.Value)))
            .OrderBy(asset => asset.Position)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var vocabulary = await LoadVocabularyAsync(
            [
                .. productValues.Select(value => value.AttributeId),
                .. variantValues.Select(value => value.AttributeId),
            ],
            cancellationToken).ConfigureAwait(false);

        // One batch call, not one per image. The listing page resolves several hundred thumbnails
        // and that is exactly the pattern IMediaLibrary.GetManyAsync exists for.
        IReadOnlyDictionary<Guid, MediaFile> files = assets.Count == 0
            ? new Dictionary<Guid, MediaFile>()
            : await media
                .GetManyAsync(assets.ConvertAll(asset => asset.FileId), cancellationToken)
                .ConfigureAwait(false);

        var result = new Dictionary<Guid, ProductResponse>(products.Count);

        foreach (var product in products)
        {
            var ownVariants = variants.Where(variant => variant.ProductId == product.Id).ToList();

            result[product.Id] = new ProductResponse(
                product.Id,
                product.Name,
                product.Slug,
                product.Status,
                product.CategoryId,
                product.BrandId,
                product.VendorId,
                product.ShortDescription,
                product.Description,
                product.HsnCode,
                product.GstRate,
                product.CountryOfOrigin,
                ToPayload(product.Manufacturer),
                ToPayload(product.Packer),
                ToPayload(product.Importer),
                product.IsReturnable,
                product.ReturnWindowDays,
                product.Warranty,
                [.. product.Specifications.Select(spec => new SpecificationPayload(spec.Label, spec.Value, spec.Group))],
                CategoryProjection.ToPayload(product.Seo),
                [
                    .. productValues
                        .Where(value => value.ProductId == product.Id)
                        .Select(value => Describe(value, vocabulary))
                        .OfType<AttributeValueResponse>()
                        .OrderBy(value => value.Name, StringComparer.Ordinal),
                ],
                Gallery(assets.Where(asset => asset.ProductId == product.Id && asset.VariantId is null), files),
                [
                    .. ownVariants.Select(variant => new VariantResponse(
                        variant.Id,
                        variant.ProductId,
                        variant.Sku,
                        variant.Barcode,
                        variant.NameSuffix,
                        variant.Status,
                        variant.Mrp.Amount,
                        variant.NetQuantity,
                        variant.ShelfLifeDays,
                        variant.ExpiresOn,
                        variant.WeightGrams,
                        variant.LengthMm,
                        variant.WidthMm,
                        variant.HeightMm,
                        variant.Position,
                        variant.IsDefault,
                        [
                            .. variantValues
                                .Where(value => value.VariantId == variant.Id)
                                .Select(value => DescribeOption(value, vocabulary))
                                .OfType<AttributeValueResponse>()
                                .OrderBy(value => value.Name, StringComparer.Ordinal),
                        ],
                        Gallery(assets.Where(asset => asset.VariantId == variant.Id), files))),
                ],
                product.RatingAverage,
                product.RatingCount,
                [.. product.ComplianceGaps(), .. ownVariants.SelectMany(variant => variant.ComplianceGaps())],
                product.PublishedAt,
                product.CreatedAt);
        }

        return result;
    }

    /// <summary>The image a card or an order line renders, for several products at once.</summary>
    /// <param name="productIds">The products.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Dictionary<Guid, Guid>> PrimaryImagesAsync(
        IReadOnlyList<Guid> productIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        if (productIds.Count == 0)
        {
            return [];
        }

        var rows = await context.MediaAssets
            .AsNoTracking()
            .Where(asset =>
                asset.ProductId != null
                && productIds.Contains(asset.ProductId.Value)
                && asset.Kind == CatalogMediaKind.Image)
            .OrderBy(asset => asset.Position)
            .Select(asset => new { ProductId = asset.ProductId!.Value, asset.FileId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(row => row.ProductId)
            .ToDictionary(group => group.Key, group => group.First().FileId);
    }

    /// <summary>The attributes and options referenced by a set of values, keyed by id.</summary>
    /// <param name="attributeIds">The attributes in play.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<AttributeVocabulary> LoadVocabularyAsync(
        IReadOnlyList<Guid> attributeIds,
        CancellationToken cancellationToken)
    {
        if (attributeIds.Count == 0)
        {
            return new AttributeVocabulary(
                new Dictionary<Guid, ProductAttribute>(),
                new Dictionary<Guid, AttributeOption>());
        }

        var distinct = attributeIds.Distinct().ToList();

        var attributes = await context.Attributes
            .AsNoTracking()
            .Where(attribute => distinct.Contains(attribute.Id))
            .ToDictionaryAsync(attribute => attribute.Id, cancellationToken)
            .ConfigureAwait(false);

        var options = await context.AttributeOptions
            .AsNoTracking()
            .Where(option => distinct.Contains(option.AttributeId))
            .ToDictionaryAsync(option => option.Id, cancellationToken)
            .ConfigureAwait(false);

        return new AttributeVocabulary(attributes, options);
    }

    private static IReadOnlyList<MediaResponse> Gallery(
        IEnumerable<CatalogMediaAsset> assets,
        IReadOnlyDictionary<Guid, MediaFile> files)
        // A file id that no longer resolves gives a null URL rather than failing the page: the
        // reference is soft by design (ADR-016), and the file may have been deleted since.
        =>
        [
            .. assets
                .OrderBy(asset => asset.Position)
                .Select(asset => new MediaResponse(
                    asset.Id,
                    asset.FileId,
                    asset.Kind,
                    asset.AltText,
                    asset.Position,
                    files.TryGetValue(asset.FileId, out var file) ? file.Url : null)),
        ];

    private static PartyPayload ToPayload(PartyDetails party)
        => new(party.Name, party.Address, party.Contact);

    private static AttributeValueResponse? Describe(ProductAttributeValue value, AttributeVocabulary vocabulary)
    {
        if (!vocabulary.Attributes.TryGetValue(value.AttributeId, out var attribute))
        {
            return null;
        }

        var display = attribute.DataType switch
        {
            AttributeDataType.Number => value.ValueNumber?.ToString(CultureInfo.InvariantCulture),
            AttributeDataType.Boolean => value.ValueBoolean?.ToString(),
            AttributeDataType.Date => value.ValueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            AttributeDataType.Select or AttributeDataType.MultiSelect =>
                value.ValueOptionId is { } optionId && vocabulary.Options.TryGetValue(optionId, out var option)
                    ? option.Label
                    : null,
            _ => value.ValueText,
        };

        return new AttributeValueResponse(
            attribute.Id,
            attribute.Code,
            attribute.Name,
            attribute.DataType,
            attribute.Unit,
            display,
            value.ValueOptionId);
    }

    private static AttributeValueResponse? DescribeOption(
        VariantAttributeValue value,
        AttributeVocabulary vocabulary)
    {
        if (!vocabulary.Attributes.TryGetValue(value.AttributeId, out var attribute))
        {
            return null;
        }

        var label = vocabulary.Options.TryGetValue(value.OptionId, out var option) ? option.Label : null;

        return new AttributeValueResponse(
            attribute.Id,
            attribute.Code,
            attribute.Name,
            attribute.DataType,
            attribute.Unit,
            label,
            value.OptionId);
    }

    /// <summary>The attributes and options a batch of values refers to.</summary>
    /// <param name="Attributes">The attributes, by id.</param>
    /// <param name="Options">Their options, by id.</param>
    private sealed record AttributeVocabulary(
        IReadOnlyDictionary<Guid, ProductAttribute> Attributes,
        IReadOnlyDictionary<Guid, AttributeOption> Options);
}
