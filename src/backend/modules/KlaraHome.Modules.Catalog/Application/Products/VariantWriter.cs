using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Catalog.Application.Products;

/// <summary>
/// The write half of a variant: the defining combination, the gallery, and which variant the PDP
/// opens on.
/// </summary>
/// <remarks>
/// Extracted for the same reason <see cref="ProductWriter"/> is — create, update and the bulk
/// importer all need it, and the combination rules are the part that is easy to get subtly wrong.
/// </remarks>
/// <param name="context">The Catalog data context.</param>
internal sealed class VariantWriter(CatalogDbContext context)
{
    /// <summary>
    /// Brings a variant's defining combination in line with the submitted axes, and recomputes the
    /// hash the uniqueness index reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every axis must be a <c>select</c> or <c>multiselect</c> attribute flagged as
    /// variant-defining, and every option must belong to the axis it was named against. Both are
    /// checked before the write, because the alternatives are a variant that cannot be rendered as
    /// a swatch and a combination hash that describes something nobody meant.
    /// </para>
    /// <para>
    /// The duplicate check is done here rather than left to the unique index: the index is the
    /// guarantee, but a caller deserves "another variant already has that combination" instead of a
    /// 500 with a constraint name in it.
    /// </para>
    /// </remarks>
    /// <param name="variant">The variant.</param>
    /// <param name="existing">Its current axes.</param>
    /// <param name="submitted">The axes it should end up with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> ApplyCombinationAsync(
        Variant variant,
        List<VariantAttributeValue> existing,
        IReadOnlyList<VariantOptionPayload> submitted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(submitted);

        var wanted = submitted
            .GroupBy(option => option.AttributeId)
            .Select(group => group.First())
            .ToList();

        if (wanted.Count > 0)
        {
            var attributeIds = wanted.ConvertAll(option => option.AttributeId);

            var attributes = await context.Attributes
                .AsNoTracking()
                .Where(attribute => attributeIds.Contains(attribute.Id))
                .ToDictionaryAsync(attribute => attribute.Id, cancellationToken)
                .ConfigureAwait(false);

            if (attributes.Count != attributeIds.Count)
            {
                return Result.Failure(CatalogErrors.NotFound("attribute"));
            }

            var notAnAxis = attributes.Values.FirstOrDefault(attribute => !attribute.IsVariantDefining);

            if (notAnAxis is not null)
            {
                return Result.Failure(CatalogErrors.NotAVariantAxis(notAnAxis.Code));
            }

            var optionIds = wanted.ConvertAll(option => option.OptionId);

            var options = await context.AttributeOptions
                .AsNoTracking()
                .Where(option => optionIds.Contains(option.Id))
                .ToDictionaryAsync(option => option.Id, cancellationToken)
                .ConfigureAwait(false);

            foreach (var payload in wanted)
            {
                if (!options.TryGetValue(payload.OptionId, out var option)
                    || option.AttributeId != payload.AttributeId)
                {
                    return Result.Failure(CatalogErrors.NotFound("attribute option"));
                }
            }
        }

        var hash = Variant.HashOf([.. wanted.Select(option => (option.AttributeId, option.OptionId))]);

        var clash = await context.Variants
            .AnyAsync(
                candidate => candidate.ProductId == variant.ProductId
                    && candidate.Id != variant.Id
                    && candidate.AttributeHash == hash,
                cancellationToken)
            .ConfigureAwait(false);

        if (clash)
        {
            return Result.Failure(CatalogErrors.DuplicateCombination);
        }

        var kept = new List<VariantAttributeValue>(wanted.Count);

        foreach (var payload in wanted)
        {
            var match = existing.Find(value =>
                value.AttributeId == payload.AttributeId && value.OptionId == payload.OptionId);

            if (match is not null)
            {
                kept.Add(match);
                continue;
            }

            var created = VariantAttributeValue.Create(variant.Id, payload.AttributeId, payload.OptionId);
            context.VariantAttributeValues.Add(created);
            kept.Add(created);
        }

        context.VariantAttributeValues.RemoveRange(existing.Where(value => !kept.Contains(value)));

        variant.SetCombination([.. wanted.Select(option => (option.AttributeId, option.OptionId))]);

        return Result.Success();
    }

    /// <summary>Brings a variant's own gallery in line with the submitted list.</summary>
    /// <param name="variant">The variant.</param>
    /// <param name="productId">Its product, denormalised onto each asset.</param>
    /// <param name="existing">Its current gallery.</param>
    /// <param name="submitted">The gallery it should end up with, or null to leave it alone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task ApplyMediaAsync(
        Variant variant,
        Guid productId,
        List<CatalogMediaAsset> existing,
        IReadOnlyList<MediaPayload>? submitted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(existing);

        if (submitted is null)
        {
            return Task.CompletedTask;
        }

        var kept = new List<CatalogMediaAsset>(submitted.Count);
        var byFile = existing.ToDictionary(asset => asset.FileId);

        foreach (var payload in submitted.DistinctBy(media => media.FileId))
        {
            if (byFile.TryGetValue(payload.FileId, out var asset))
            {
                asset.Describe(payload.AltText, payload.Position);
                kept.Add(asset);
                continue;
            }

            var created = CatalogMediaAsset.ForVariant(
                variant.Id,
                productId,
                payload.FileId,
                payload.Kind,
                payload.AltText,
                payload.Position);

            context.MediaAssets.Add(created);
            kept.Add(created);
        }

        context.MediaAssets.RemoveRange(existing.Where(asset => !kept.Contains(asset)));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Makes this the variant the PDP opens on, and takes the flag off whichever had it.
    /// </summary>
    /// <remarks>
    /// Done here rather than left to a partial unique index, because two defaults is not an error
    /// worth failing a request over — it is a flag to move. The read side takes the first anyway.
    /// </remarks>
    /// <param name="variant">The variant to make the default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task MakeDefaultAsync(Variant variant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(variant);

        var siblings = await context.Variants
            .Where(candidate =>
                candidate.ProductId == variant.ProductId && candidate.Id != variant.Id && candidate.IsDefault)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var sibling in siblings)
        {
            sibling.SetDefault(false);
        }

        variant.SetDefault(true);
    }
}
