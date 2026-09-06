using KlaraHome.Contracts.Reviews;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Vendors.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Vendors.Infrastructure.Events;

/// <summary>
/// Keeps a seller's stored rating in step with what shoppers have said about their sales
/// (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// A seller's rating is the average over reviews of <em>their</em> sales, not over the products they
/// happen to list — the same product from two sellers can arrive well packed or badly, on time or
/// late, and that difference is exactly what the number is for. Reviews computes it, because a review
/// is written against an order line and only that module knows which seller sold it.
/// </para>
/// <para>
/// Like the catalogue's, this handler stores two numbers it was handed rather than adjusting a
/// running total, so a redelivered message is harmless without an inbox row.
/// </para>
/// <para>
/// The number matters beyond a badge on a storefront: it is a criterion the buy-box rule may rank
/// on, so a seller who ships well can win an offer they would otherwise lose on price alone.
/// </para>
/// </remarks>
/// <param name="context">The Vendors data context.</param>
internal sealed class VendorRatingHandlers(VendorsDbContext context)
    : IIntegrationEventHandler<VendorRatingChanged>
{
    /// <inheritdoc />
    public async Task HandleAsync(VendorRatingChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var vendor = await context.Vendors
            .FirstOrDefaultAsync(row => row.Id == integrationEvent.VendorId, cancellationToken)
            .ConfigureAwait(false);

        if (vendor is null)
        {
            return;
        }

        vendor.RecordRating(integrationEvent.RatingAverage);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
