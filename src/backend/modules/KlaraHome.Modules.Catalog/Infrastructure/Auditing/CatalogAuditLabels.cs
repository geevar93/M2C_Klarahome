using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Catalog.Infrastructure.Auditing;

/// <summary>Names products on the audit trail by what they are called.</summary>
/// <param name="context">The Catalog data context.</param>
internal sealed class ProductAuditLabels(CatalogDbContext context) : IAuditLabelSource
{
    /// <inheritdoc />
    public string EntityType => "Product";

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> LabelsAsync(
        IReadOnlyCollection<string> entityIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityIds);

        var ids = AuditIds.Guids(entityIds);

        if (ids.Length == 0)
        {
            return new Dictionary<string, string>();
        }

        return await context.Products
            .AsNoTracking()
            .Where(product => ids.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id.ToString(), product => product.Name, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Names variants on the audit trail by their SKU, which is what a seller quotes.</summary>
/// <param name="context">The Catalog data context.</param>
internal sealed class VariantAuditLabels(CatalogDbContext context) : IAuditLabelSource
{
    /// <inheritdoc />
    public string EntityType => "Variant";

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> LabelsAsync(
        IReadOnlyCollection<string> entityIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityIds);

        var ids = AuditIds.Guids(entityIds);

        if (ids.Length == 0)
        {
            return new Dictionary<string, string>();
        }

        return await context.Variants
            .AsNoTracking()
            .Where(variant => ids.Contains(variant.Id))
            .ToDictionaryAsync(variant => variant.Id.ToString(), variant => variant.Sku, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Parses the string ids an audit entry stores back into the guids a table is keyed on.</summary>
internal static class AuditIds
{
    /// <summary>The distinct ids that are guids; anything else is not this module's entity.</summary>
    /// <param name="entityIds">The ids as the audit entries store them.</param>
    public static Guid[] Guids(IReadOnlyCollection<string> entityIds)
        => [.. entityIds
            .Select(id => Guid.TryParse(id, out var parsed) ? parsed : (Guid?)null)
            .OfType<Guid>()
            .Distinct()];
}
