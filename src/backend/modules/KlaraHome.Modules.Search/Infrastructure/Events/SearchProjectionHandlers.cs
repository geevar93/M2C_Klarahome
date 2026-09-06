using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Inventory;
using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Contracts.Reviews;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Search.Domain;
using KlaraHome.Modules.Search.Infrastructure.Persistence;
using KlaraHome.Modules.Search.Infrastructure.Projection;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Search.Infrastructure.Events;

/// <summary>
/// Keeps the index in step with the seven facts that can change what a shopper should see
/// (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// This is what makes the projection a projection rather than a stale copy. An offer going live, its
/// terms changing, its withdrawal, its price moving, its stock moving, its being bought and — since
/// Step 21 — its being reviewed are between them everything that alters a search result, and each of
/// them arrives here within seconds of happening.
/// </para>
/// <para>
/// Four of the seven take the expensive path and re-resolve the variant from the catalogue, because
/// they can change <em>which offer wins the buy box</em> — a price is one of the criteria the rule
/// ranks on, and a withdrawal removes a candidate. Stock deliberately does not: the buy-box rule
/// treats availability as unknown rather than as a criterion, so a stock movement changes one row's
/// two availability columns and nothing else. That is the difference between the highest-volume
/// event in the platform costing one <c>UPDATE</c> and costing a five-table read.
/// </para>
/// <para>
/// Delivery is at-least-once, so every method here is idempotent. Six of them are naturally so —
/// rebuilding a row from its sources twice produces the same row. The seventh is not: units sold is a
/// counter, and adding to it twice would permanently overstate how popular a product is. That one
/// writes an inbox row in the same transaction, which is what the inbox table exists for
/// (docs/03-database-design.md §4.1).
/// </para>
/// </remarks>
/// <param name="context">The Search data context.</param>
/// <param name="writer">The single place an index row is written.</param>
/// <param name="catalogue">Translates the listing an event names into the variant a row is keyed on.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what could not be resolved.</param>
internal sealed partial class SearchProjectionHandlers(
    SearchDbContext context,
    SearchProjectionWriter writer,
    IProductProjectionSource catalogue,
    IClock clock,
    ILogger<SearchProjectionHandlers> logger)
    : IIntegrationEventHandler<ListingPublished>,
        IIntegrationEventHandler<ListingUpdated>,
        IIntegrationEventHandler<ListingDeactivated>,
        IIntegrationEventHandler<PriceChanged>,
        IIntegrationEventHandler<StockLevelChanged>,
        IIntegrationEventHandler<SubOrderConfirmed>,
        IIntegrationEventHandler<ProductRatingChanged>
{
    /// <summary>
    /// The name this handler records itself under in the inbox.
    /// </summary>
    /// <remarks>
    /// A constant rather than the type name, so that renaming the class does not silently make every
    /// already-counted sale countable again.
    /// </remarks>
    private const string PopularityHandler = "search.popularity";

    /// <inheritdoc />
    public Task HandleAsync(ListingPublished integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // The event carries the variant, so no translation is needed. It is the only one of the six
        // that does.
        return writer.RefreshVariantsAsync([integrationEvent.VariantId], cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The whole variant, not just this offer. A price or a dispatch promise changing is one of the
    /// things the buy-box rule ranks on, so the seller a shopper sees may now be a different one —
    /// and updating this listing's row in place would leave the index showing the old winner at the
    /// new winner's price.
    /// </remarks>
    public Task HandleAsync(ListingUpdated integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return writer.RefreshVariantsAsync([integrationEvent.VariantId], cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A withdrawal removes a candidate, so the variant is re-resolved rather than removed. Where
    /// another seller was offering it, the row simply changes hands; where nobody was, the writer
    /// retires it and the sales history it has accumulated survives the offer coming back.
    /// </remarks>
    public Task HandleAsync(ListingDeactivated integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return writer.RefreshVariantsAsync([integrationEvent.VariantId], cancellationToken);
    }

    /// <inheritdoc />
    public async Task HandleAsync(PriceChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var variants = await catalogue
            .FindVariantsOfAsync([integrationEvent.ListingId], cancellationToken)
            .ConfigureAwait(false);

        if (!variants.TryGetValue(integrationEvent.ListingId, out var variantId))
        {
            UnknownListing(logger, integrationEvent.ListingId);
            return;
        }

        await writer.RefreshVariantsAsync([variantId], cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The cheap path, and the reason it is cheap is that stock is not a buy-box criterion. The event
    /// carries both numbers the index holds, so the row is found by the listing it already names and
    /// two columns are written. A listing that is not the current winner matches no row, which is
    /// correct: what is on a losing seller's shelf changes nothing a shopper can see.
    /// </remarks>
    public async Task HandleAsync(StockLevelChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var documents = await context.Documents
            .Where(document => document.ListingId == integrationEvent.ListingId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (documents.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;

        foreach (var document in documents)
        {
            document.RecordAvailability(
                integrationEvent.QuantityAvailable,
                integrationEvent.IsAvailable,
                now);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Confirmation rather than placement, and rather than delivery. A placed order that is never
    /// paid for is not evidence of anything, and waiting for delivery would mean the ranking lagged
    /// demand by a week — which is exactly wrong for the sale week a merchandiser cares most about.
    /// </para>
    /// <para>
    /// The inbox row is written in the same <c>SaveChanges</c> as the counter, so a redelivery either
    /// finds the row and does nothing, or fails the primary key and rolls the whole thing back. There
    /// is no ordering in which a sale is counted twice.
    /// </para>
    /// </remarks>
    public async Task HandleAsync(SubOrderConfirmed integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var counted = await context.InboxMessages
            .AnyAsync(
                message => message.MessageId == integrationEvent.EventId
                           && message.Handler == PopularityHandler,
                cancellationToken)
            .ConfigureAwait(false);

        if (counted)
        {
            return;
        }

        var quantities = new Dictionary<Guid, int>();

        foreach (var line in integrationEvent.Lines)
        {
            quantities[line.ListingId] = quantities.GetValueOrDefault(line.ListingId) + line.Quantity;
        }

        var variants = await catalogue
            .FindVariantsOfAsync([.. quantities.Keys], cancellationToken)
            .ConfigureAwait(false);

        var byVariant = new Dictionary<Guid, int>();

        foreach (var (listingId, quantity) in quantities)
        {
            if (variants.TryGetValue(listingId, out var variantId))
            {
                byVariant[variantId] = byVariant.GetValueOrDefault(variantId) + quantity;
            }
        }

        var documents = await context.Documents
            .Where(document => byVariant.Keys.Contains(document.VariantId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var document in documents)
        {
            document.RecordSale(byVariant[document.VariantId]);
        }

        // Written whether or not any row was found. A sale of something that is not in the index is
        // still a sale this handler has seen, and leaving it unrecorded would mean re-resolving it on
        // every redelivery for as long as the outbox keeps the message.
        context.InboxMessages.Add(new InboxMessage
        {
            MessageId = integrationEvent.EventId,
            Handler = PopularityHandler,
            ProcessedAt = clock.UtcNow,
        });

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Added at Step 21, and the seventh fact. A rating is a column in the index and a criterion the
    /// storefront sorts on, so an average that moved and an index that did not would put a
    /// four-and-a-half-star product below a three-star one for as long as it took somebody to notice.
    /// </para>
    /// <para>
    /// The event names a product and the index is keyed on the variant, so the catalogue translates —
    /// the same asymmetry the price handler deals with, in the other direction. Every variant of the
    /// product is refreshed, because a rating is a fact about the product and every one of its
    /// variants renders it.
    /// </para>
    /// <para>
    /// Idempotent without an inbox row, like five of the other six: the event carries the recomputed
    /// aggregate rather than a delta, and rebuilding a row from its sources twice produces the same
    /// row.
    /// </para>
    /// </remarks>
    public async Task HandleAsync(ProductRatingChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var projections = await catalogue
            .FindByProductsAsync([integrationEvent.ProductId], cancellationToken)
            .ConfigureAwait(false);

        var variantIds = projections.Select(projection => projection.VariantId).Distinct().ToArray();

        if (variantIds.Length == 0)
        {
            return;
        }

        await writer.RefreshVariantsAsync(variantIds, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 7930,
        Level = LogLevel.Warning,
        Message = "Search could not resolve listing {ListingId} to a variant; its index row was not refreshed")]
    private static partial void UnknownListing(ILogger logger, Guid listingId);
}
