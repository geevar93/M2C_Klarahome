using KlaraHome.Contracts.Reviews;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Catalog.Infrastructure.Events;

/// <summary>
/// Keeps a product's stored rating in step with what shoppers have said
/// (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// The catalogue holds two rating columns on every product, and until Step 21 nothing filled them —
/// which is why every projection built on <c>IProductProjectionSource</c> has been answering null
/// since Step 19. This handler is what closes that: the Reviews module computes the aggregate, which
/// is the only module that can, and publishes it; the catalogue stores the two numbers it is handed.
/// </para>
/// <para>
/// Applying it is idempotent by construction, and that is a property of the event rather than of
/// this code. <c>ProductRatingChanged</c> carries the recomputed average and count rather than a
/// delta, so a redelivered message writes the same two values a second time. A handler that added a
/// delta would need an inbox row; this one needs nothing.
/// </para>
/// <para>
/// A product the catalogue no longer has is simply skipped. A review of something that has since
/// been deleted is not an error — the review outlives the product, and the storefront resolves it
/// through the same projection that no longer returns one.
/// </para>
/// </remarks>
/// <param name="context">The Catalog data context.</param>
internal sealed class ReviewRatingHandlers(CatalogDbContext context)
    : IIntegrationEventHandler<ProductRatingChanged>
{
    /// <inheritdoc />
    public async Task HandleAsync(ProductRatingChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var product = await context.Products
            .FirstOrDefaultAsync(row => row.Id == integrationEvent.ProductId, cancellationToken)
            .ConfigureAwait(false);

        if (product is null)
        {
            return;
        }

        product.ProjectRating(integrationEvent.RatingAverage, integrationEvent.RatingCount);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
