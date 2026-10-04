using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Pricing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Pricing.Infrastructure.Auditing;

/// <summary>Names promotions on the audit trail by what the merchandiser called them.</summary>
/// <param name="context">The Pricing data context.</param>
internal sealed class PromotionAuditLabels(PricingDbContext context) : IAuditLabelSource
{
    /// <inheritdoc />
    public string EntityType => "Promotion";

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> LabelsAsync(
        IReadOnlyCollection<string> entityIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityIds);

        var ids = entityIds
            .Select(id => Guid.TryParse(id, out var parsed) ? parsed : (Guid?)null)
            .OfType<Guid>()
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<string, string>();
        }

        return await context.Promotions
            .AsNoTracking()
            .Where(promotion => ids.Contains(promotion.Id))
            .ToDictionaryAsync(promotion => promotion.Id.ToString(), promotion => promotion.Name, cancellationToken)
            .ConfigureAwait(false);
    }
}
