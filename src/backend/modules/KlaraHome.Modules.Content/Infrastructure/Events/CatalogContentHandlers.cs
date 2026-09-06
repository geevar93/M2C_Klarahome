using KlaraHome.Contracts.Catalog;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Content.Infrastructure.Collections;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Content.Infrastructure.Events;

/// <summary>
/// Keeps rule-based collections in step with the catalogue (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// Three events, and they are the three that can change whether a product belongs in a rule: an offer
/// going live, its terms changing — which moves the price and the discount a rule may be about — and
/// its withdrawal. A rule about a category or a brand is unaffected by any of them, and re-evaluating
/// it for one product costs one in-memory comparison, so the handler does not try to be clever about
/// which rules to skip.
/// </para>
/// <para>
/// The events name a <em>listing</em> and a collection holds <em>products</em>, so each one is
/// translated through the catalogue's own contract rather than by a rule this module keeps. Two of
/// the three carry the variant but not the product; the projection carries both, and asking for it is
/// the same call that supplies the facts the rule is evaluated against.
/// </para>
/// <para>
/// Every method is idempotent, because delivery is at least once. They have to be: the work is
/// "make the membership match the rule", which is the same answer however many times it is asked.
/// There is no counter here and therefore no inbox row — unlike the search projection, which has one
/// precisely because it counts.
/// </para>
/// </remarks>
/// <param name="materializer">Adds and removes the rows a rule owns.</param>
/// <param name="catalogue">Translates a listing into the product a collection holds.</param>
/// <param name="logger">Reports what could not be resolved.</param>
internal sealed partial class CatalogContentHandlers(
    CollectionMaterializer materializer,
    IProductProjectionSource catalogue,
    ILogger<CatalogContentHandlers> logger)
    : IIntegrationEventHandler<ListingPublished>,
        IIntegrationEventHandler<ListingUpdated>,
        IIntegrationEventHandler<ListingDeactivated>
{
    /// <inheritdoc />
    /// <remarks>
    /// The one event that carries the product outright, so no translation is needed.
    /// </remarks>
    public Task HandleAsync(ListingPublished integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return materializer.ApplyProductsAsync([integrationEvent.ProductId], cancellationToken);
    }

    /// <inheritdoc />
    public Task HandleAsync(ListingUpdated integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return ApplyVariantAsync(integrationEvent.VariantId, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A withdrawal is handled the same way as a change, and deliberately: the last offer for a
    /// variant going away leaves the product with no buy box, the rule stops matching it, and the row
    /// is removed by the same code path that would have removed it for a price change. A separate
    /// "delete it" branch would be a second answer to the same question.
    /// </remarks>
    public Task HandleAsync(ListingDeactivated integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return ApplyVariantAsync(integrationEvent.VariantId, cancellationToken);
    }

    /// <summary>Finds the product behind a variant and re-evaluates every rule for it.</summary>
    private async Task ApplyVariantAsync(Guid variantId, CancellationToken cancellationToken)
    {
        var projections = await catalogue
            .FindByVariantsAsync([variantId], cancellationToken)
            .ConfigureAwait(false);

        var productId = projections.Select(row => row.ProductId).FirstOrDefault();

        if (productId == Guid.Empty)
        {
            // The variant has no live offer at all — every listing for it has gone. Nothing can tell
            // us which product it belonged to any more, so the rows for it are left for the periodic
            // sweep, which rebuilds each collection from the catalogue and therefore drops them.
            VariantUnresolved(logger, variantId);
            return;
        }

        await materializer.ApplyProductsAsync([productId], cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 8020,
        Level = LogLevel.Debug,
        Message = "Variant {VariantId} has no live offer; collection membership left to the next sweep")]
    private static partial void VariantUnresolved(ILogger logger, Guid variantId);
}
