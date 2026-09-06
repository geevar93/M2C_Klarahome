using KlaraHome.Modules.Catalog.Application.Taxonomy;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Catalog.Application.Products;

/// <summary>
/// The write half of a product: the payload-to-entity mapping and the attribute-value
/// reconciliation that create, update and the bulk importer all need.
/// </summary>
/// <remarks>
/// Extracted because there are three callers and the reconciliation is the fiddly part. Two copies
/// of "work out which values are new, which changed and which went" is two places for the rule
/// about typed columns to be got subtly wrong.
/// </remarks>
/// <param name="context">The Catalog data context.</param>
internal sealed class ProductWriter(CatalogDbContext context)
{
    /// <summary>Applies the marketing copy and taxonomy from a payload.</summary>
    /// <param name="product">The product.</param>
    /// <param name="name">Its title.</param>
    /// <param name="slug">Its URL segment.</param>
    /// <param name="categoryId">The category it browses under.</param>
    /// <param name="brandId">Its brand.</param>
    /// <param name="shortDescription">The one-line summary.</param>
    /// <param name="description">The long description.</param>
    /// <param name="specifications">The specification table.</param>
    /// <param name="seo">Crawler metadata.</param>
    public static void Describe(
        Product product,
        string name,
        string slug,
        Guid categoryId,
        Guid? brandId,
        string? shortDescription,
        string? description,
        IReadOnlyList<SpecificationPayload>? specifications,
        SeoPayload? seo)
    {
        ArgumentNullException.ThrowIfNull(product);

        product.Describe(
            name.Trim(),
            slug,
            categoryId,
            brandId,
            shortDescription,
            description,
            [
                .. (specifications ?? [])
                    .Where(spec => !string.IsNullOrWhiteSpace(spec.Label))
                    .Select(spec => new Specification(spec.Label.Trim(), spec.Value?.Trim() ?? string.Empty, spec.Group)),
            ],
            CategoryProjection.ToSeo(seo));
    }

    /// <summary>Applies the India compliance fields from a payload.</summary>
    /// <param name="product">The product.</param>
    /// <param name="hsnCode">The HSN code.</param>
    /// <param name="gstRate">The GST percentage.</param>
    /// <param name="countryOfOrigin">ISO 3166-1 alpha-2 origin.</param>
    /// <param name="manufacturer">Who made it.</param>
    /// <param name="packer">Who packed it.</param>
    /// <param name="importer">Who imported it.</param>
    public static void DeclareCompliance(
        Product product,
        string? hsnCode,
        decimal gstRate,
        string? countryOfOrigin,
        PartyPayload? manufacturer,
        PartyPayload? packer,
        PartyPayload? importer)
    {
        ArgumentNullException.ThrowIfNull(product);

        product.DeclareCompliance(
            hsnCode,
            gstRate,
            countryOfOrigin,
            ToParty(manufacturer),
            ToParty(packer),
            ToParty(importer));
    }

    /// <summary>
    /// Brings a product's attribute values in line with the submitted list.
    /// </summary>
    /// <remarks>
    /// Matched on (attribute, option) rather than on an id the caller does not have: the admin form
    /// posts what the product should say, not a list of row identities. A multiselect legitimately
    /// contributes several rows for one attribute, which is why the option is part of the key.
    /// </remarks>
    /// <param name="productId">The product.</param>
    /// <param name="existing">What is currently stored.</param>
    /// <param name="submitted">What it should say, or null to leave it alone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<List<ProductAttributeValue>>> ApplyAttributesAsync(
        Guid productId,
        List<ProductAttributeValue> existing,
        IReadOnlyList<AttributeValuePayload>? submitted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(existing);

        if (submitted is null)
        {
            return existing;
        }

        var attributeIds = submitted.Select(value => value.AttributeId).Distinct().ToList();

        var attributes = await context.Attributes
            .AsNoTracking()
            .Where(attribute => attributeIds.Contains(attribute.Id))
            .ToDictionaryAsync(attribute => attribute.Id, cancellationToken)
            .ConfigureAwait(false);

        if (attributes.Count != attributeIds.Count)
        {
            return CatalogErrors.NotFound("attribute");
        }

        // Every option named must belong to the attribute it was named against. Without this check
        // a caller can pin "colour" to a "size" option and produce a facet nobody can explain.
        var optionIds = submitted
            .Where(value => value.OptionId is not null)
            .Select(value => value.OptionId!.Value)
            .Distinct()
            .ToList();

        var options = optionIds.Count == 0
            ? []
            : await context.AttributeOptions
                .AsNoTracking()
                .Where(option => optionIds.Contains(option.Id))
                .ToDictionaryAsync(option => option.Id, cancellationToken)
                .ConfigureAwait(false);

        foreach (var payload in submitted.Where(value => value.OptionId is not null))
        {
            if (!options.TryGetValue(payload.OptionId!.Value, out var option)
                || option.AttributeId != payload.AttributeId)
            {
                return CatalogErrors.NotFound("attribute option");
            }
        }

        var kept = new List<ProductAttributeValue>(submitted.Count);

        foreach (var payload in submitted)
        {
            var dataType = attributes[payload.AttributeId].DataType;

            var match = existing.Find(value =>
                value.AttributeId == payload.AttributeId && value.ValueOptionId == payload.OptionId);

            if (match is not null)
            {
                match.Set(dataType, payload.Text, payload.OptionId);
                kept.Add(match);
                continue;
            }

            var created = ProductAttributeValue.Create(
                productId,
                payload.AttributeId,
                dataType,
                payload.Text,
                payload.OptionId);

            context.ProductAttributeValues.Add(created);
            kept.Add(created);
        }

        context.ProductAttributeValues.RemoveRange(existing.Where(value => !kept.Contains(value)));

        return kept;
    }

    private static PartyDetails ToParty(PartyPayload? payload)
        => new()
        {
            Name = payload?.Name?.Trim() ?? string.Empty,
            Address = payload?.Address?.Trim() ?? string.Empty,
            Contact = payload?.Contact?.Trim(),
        };
}
