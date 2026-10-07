using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Vendors.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Vendors.Infrastructure.Auditing;

/// <summary>Names sellers on the audit trail by their display name.</summary>
/// <param name="context">The Vendors data context.</param>
internal sealed class VendorAuditLabels(VendorsDbContext context) : IAuditLabelSource
{
    /// <inheritdoc />
    public string EntityType => "Vendor";

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

        return await context.Vendors
            .AsNoTracking()
            .Where(vendor => ids.Contains(vendor.Id))
            .ToDictionaryAsync(vendor => vendor.Id.ToString(), vendor => vendor.DisplayName, cancellationToken)
            .ConfigureAwait(false);
    }
}
