using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Orders.Infrastructure.Auditing;

/// <summary>Names a seller's part of an order by its number, which is what support quotes.</summary>
/// <param name="context">The Ordering data context.</param>
internal sealed class SubOrderAuditLabels(OrdersDbContext context) : IAuditLabelSource
{
    /// <inheritdoc />
    public string EntityType => "SubOrder";

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

        return await context.SubOrders
            .AsNoTracking()
            .Where(subOrder => ids.Contains(subOrder.Id))
            .ToDictionaryAsync(subOrder => subOrder.Id.ToString(), subOrder => subOrder.SubOrderNumber, cancellationToken)
            .ConfigureAwait(false);
    }
}
