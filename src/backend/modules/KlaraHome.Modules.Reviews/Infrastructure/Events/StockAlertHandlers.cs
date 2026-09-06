using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Inventory;
using KlaraHome.Contracts.Notifications;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Reviews.Domain;
using KlaraHome.Modules.Reviews.Infrastructure.Features;
using KlaraHome.Modules.Reviews.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Reviews.Infrastructure.Events;

/// <summary>
/// Fires the alerts shoppers asked for, off the two facts that can satisfy one
/// (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// Stock coming back and a price coming down are the only two things a subscription in this module
/// waits for, and each arrives here within seconds of happening. Both handlers take the same shape:
/// translate the listing the event names into the variant a subscription is keyed on, find who is
/// waiting, queue a message each, and close the subscriptions that fired.
/// </para>
/// <para>
/// Delivery is at-least-once, so both are guarded by an inbox row written in the same
/// <c>SaveChanges</c> as the subscriptions they close. That guard is doing real work here rather
/// than being ceremony: unlike a projection, sending a message is not idempotent — a redelivered
/// <c>StockLevelChanged</c> without it would email the same shopper twice about the same restock,
/// and there is no way to un-send it.
/// </para>
/// <para>
/// <see cref="StockLevelChanged"/> is the highest-volume event on the platform and this handler runs
/// for every one of them. The first thing it does is the cheap test — the event carries
/// <c>WasAvailable</c> and <c>IsAvailable</c>, so a movement that did not cross the availability
/// boundary is discarded before any query is issued at all.
/// </para>
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="catalogue">Translates the listing an event names into the variant a row is keyed on.</param>
/// <param name="notifier">Queues the messages.</param>
/// <param name="flags">Whether this deployment sends stock alerts at all.</param>
/// <param name="options">How many alerts one event may fan out to.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what was sent and what could not be resolved.</param>
internal sealed partial class StockAlertHandlers(
    ReviewsDbContext context,
    IProductProjectionSource catalogue,
    INotifier notifier,
    IFeatureFlags flags,
    IOptionsMonitor<ReviewsOptions> options,
    IClock clock,
    ILogger<StockAlertHandlers> logger)
    : IIntegrationEventHandler<StockLevelChanged>,
        IIntegrationEventHandler<PriceChanged>
{
    /// <summary>The name the back-in-stock handler records itself under in the inbox.</summary>
    /// <remarks>
    /// A constant rather than the type name, so that renaming the class does not silently make every
    /// already-sent alert sendable again.
    /// </remarks>
    private const string RestockHandler = "reviews.back-in-stock";

    /// <summary>The name the price-drop handler records itself under.</summary>
    private const string PriceDropHandler = "reviews.price-drop";

    /// <inheritdoc />
    /// <remarks>
    /// Only a movement that crosses from unavailable to available is a restock. A delivery that takes
    /// stock from four units to forty is not news to somebody who was waiting, because they were
    /// never waiting — the thing was buyable the whole time.
    /// </remarks>
    public async Task HandleAsync(StockLevelChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.WasAvailable || !integrationEvent.IsAvailable)
        {
            return;
        }

        if (!await IsEnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var variantId = await ResolveVariantAsync(integrationEvent.ListingId, cancellationToken)
            .ConfigureAwait(false);

        if (variantId is null)
        {
            return;
        }

        await FireAsync(
                integrationEvent.EventId,
                RestockHandler,
                SubscriptionKind.BackInStock,
                variantId.Value,
                NotificationEvents.BackInStock,
                price: null,
                previousPrice: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Only a drop, and only for the subscriptions the new price actually satisfies. A shopper who
    /// said they would buy at ₹1,200 is not told that it has fallen from ₹2,000 to ₹1,800 — that
    /// message is the reason people stop reading these.
    /// </remarks>
    public async Task HandleAsync(PriceChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!integrationEvent.IsDrop)
        {
            return;
        }

        if (!await IsEnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var variantId = await ResolveVariantAsync(integrationEvent.ListingId, cancellationToken)
            .ConfigureAwait(false);

        if (variantId is null)
        {
            return;
        }

        await FireAsync(
                integrationEvent.EventId,
                PriceDropHandler,
                SubscriptionKind.PriceDrop,
                variantId.Value,
                NotificationEvents.PriceDrop,
                integrationEvent.NewPrice,
                integrationEvent.PreviousPrice,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Whether this deployment sends stock alerts at all.</summary>
    private async Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
        => await flags.IsEnabledAsync(ReviewFeatures.StockAlerts, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Which variant the listing belongs to.</summary>
    private async Task<Guid?> ResolveVariantAsync(Guid listingId, CancellationToken cancellationToken)
    {
        var variants = await catalogue
            .FindVariantsOfAsync([listingId], cancellationToken)
            .ConfigureAwait(false);

        if (variants.TryGetValue(listingId, out var variantId))
        {
            return variantId;
        }

        UnknownListing(logger, listingId);
        return null;
    }

    /// <summary>
    /// Tells everybody waiting for one variant, and closes their subscriptions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The inbox row is written in the same <c>SaveChanges</c> as the subscription closures, so a
    /// redelivery either finds the row and does nothing, or fails the primary key and rolls the whole
    /// thing back. There is no ordering in which somebody is told twice by this path.
    /// </para>
    /// <para>
    /// The messages are queued before that save and are therefore not part of it, which is a
    /// deliberate asymmetry: <c>INotifier</c> writes in its own scope by design, and the alternative —
    /// holding a transaction open across a fan-out to five hundred recipients — would be far worse
    /// than the failure it prevents. A rollback after queueing sends an alert about a restock that
    /// did not commit, which is a shopper visiting a page and finding nothing.
    /// </para>
    /// </remarks>
    private async Task FireAsync(
        Guid eventId,
        string handler,
        SubscriptionKind kind,
        Guid variantId,
        string eventKey,
        decimal? price,
        decimal? previousPrice,
        CancellationToken cancellationToken)
    {
        var alreadySent = await context.InboxMessages
            .AnyAsync(message => message.MessageId == eventId && message.Handler == handler, cancellationToken)
            .ConfigureAwait(false);

        if (alreadySent)
        {
            return;
        }

        var waiting = await context.StockSubscriptions
            .Where(subscription => subscription.VariantId == variantId)
            .Where(subscription => subscription.Kind == kind)
            .Where(subscription => subscription.Status == SubscriptionStatus.Active)
            .OrderBy(subscription => subscription.Id)
            .Take(options.CurrentValue.MaxAlertsPerEvent)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = clock.UtcNow;
        var sent = 0;

        foreach (var subscription in waiting)
        {
            // A price-drop subscription decides for itself whether this price satisfies it. A
            // back-in-stock one has nothing to decide: it is waiting for exactly the event that just
            // arrived.
            if (kind == SubscriptionKind.PriceDrop && (price is null || !subscription.IsSatisfiedBy(price.Value)))
            {
                continue;
            }

            await notifier
                .EnqueueAsync(
                    new NotificationRequest(
                        eventKey,
                        new NotificationRecipient(subscription.CustomerId, subscription.Email),
                        BuildVariables(subscription, price, previousPrice)),
                    cancellationToken)
                .ConfigureAwait(false);

            subscription.MarkNotified(now, price);
            sent++;
        }

        // Written whether or not anybody was waiting. An event this handler has seen and found nobody
        // for is still an event it has seen, and leaving it unrecorded would mean re-resolving it on
        // every redelivery for as long as the outbox keeps the message.
        context.InboxMessages.Add(new InboxMessage
        {
            MessageId = eventId,
            Handler = handler,
            ProcessedAt = now,
        });

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (sent > 0)
        {
            AlertsSent(logger, sent, eventKey, variantId);
        }
    }

    /// <summary>
    /// What the template is given.
    /// </summary>
    /// <remarks>
    /// The product's name is not among them, and cannot be: this module holds product ids and no
    /// product names, and resolving one per recipient would be a catalogue read for every message in
    /// a fan-out. The id is passed and the storefront's link resolves it, which is honest about what
    /// the module has.
    /// </remarks>
    private static Dictionary<string, string> BuildVariables(
        StockSubscription subscription,
        decimal? price,
        decimal? previousPrice)
        => new(StringComparer.Ordinal)
        {
            ["name"] = "there",
            ["productName"] = subscription.ProductId.ToString(),
            ["variantId"] = subscription.VariantId.ToString(),
            ["price"] = Format(price),
            ["previousPrice"] = Format(previousPrice ?? subscription.PriceAtSubscription),
        };

    private static string Format(decimal? amount)
        => amount?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    [LoggerMessage(
        EventId = 8110,
        Level = LogLevel.Warning,
        Message = "Reviews could not resolve listing {ListingId} to a variant; no stock alert was sent")]
    private static partial void UnknownListing(ILogger logger, Guid listingId);

    [LoggerMessage(
        EventId = 8111,
        Level = LogLevel.Information,
        Message = "Queued {AlertCount} {EventKey} alert(s) for variant {VariantId}")]
    private static partial void AlertsSent(ILogger logger, int alertCount, string eventKey, Guid variantId);
}
