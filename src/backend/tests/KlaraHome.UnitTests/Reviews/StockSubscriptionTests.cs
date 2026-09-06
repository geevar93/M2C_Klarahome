using KlaraHome.Modules.Reviews.Domain;

namespace KlaraHome.UnitTests.Reviews;

/// <summary>
/// When a price-drop alert should actually fire.
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1. It is a rule about inequalities, which
/// is the classic place for an off-by-one, and both failure modes are unrecoverable: an alert that
/// does not fire is a sale nobody made, and one that fires wrongly is an unsolicited email a shopper
/// cannot un-receive.
/// </remarks>
public sealed class StockSubscriptionTests
{
    private static readonly DateTimeOffset Expiry = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A named target fires when the price reaches it, not only when it goes below.</summary>
    /// <remarks>
    /// A shopper who said "tell me at ₹1,200" means at ₹1,200. Requiring strictly less would leave
    /// the alert silent on the exact price they asked for, which is the one price they will notice.
    /// </remarks>
    [Fact]
    public void A_named_target_fires_at_the_price_that_was_named()
    {
        var subscription = PriceDrop(target: 1_200m, priceAtSubscription: 2_000m);

        Assert.True(subscription.IsSatisfiedBy(1_200m));
        Assert.True(subscription.IsSatisfiedBy(1_199m));
        Assert.False(subscription.IsSatisfiedBy(1_201m));
    }

    /// <summary>With no target, the benchmark is what it cost when they subscribed.</summary>
    /// <remarks>
    /// Recording the price at subscription is what makes this answerable at all: without it,
    /// "cheaper" has no meaning once the price has moved twice.
    /// </remarks>
    [Fact]
    public void With_no_target_any_drop_below_the_subscription_price_fires()
    {
        var subscription = PriceDrop(target: null, priceAtSubscription: 2_000m);

        Assert.True(subscription.IsSatisfiedBy(1_999.99m));
        Assert.False(subscription.IsSatisfiedBy(2_000m));
        Assert.False(subscription.IsSatisfiedBy(2_500m));
    }

    /// <summary>A price returning to what it was is not a drop.</summary>
    /// <remarks>
    /// The one case a strict comparison exists for. A price that fell and came back would otherwise
    /// fire an alert saying it is now what it always was.
    /// </remarks>
    [Fact]
    public void A_price_returning_to_where_it_started_does_not_fire()
    {
        var subscription = PriceDrop(target: null, priceAtSubscription: 1_500m);

        Assert.False(subscription.IsSatisfiedBy(1_500m));
    }

    /// <summary>A back-in-stock alert is never satisfied by a price.</summary>
    /// <remarks>
    /// The two kinds share a table and a handler, so the kind is checked rather than assumed. A
    /// back-in-stock row answering true to a price movement would send the wrong message about the
    /// wrong event.
    /// </remarks>
    [Fact]
    public void A_back_in_stock_alert_ignores_prices()
    {
        var subscription = StockSubscription.Record(
            SubscriptionKind.BackInStock,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            listingId: null,
            Guid.CreateVersion7(),
            email: null,
            targetPrice: null,
            priceAtSubscription: null,
            Expiry);

        Assert.False(subscription.IsSatisfiedBy(1m));
    }

    /// <summary>An alert that has already fired does not fire again.</summary>
    /// <remarks>
    /// <see cref="SubscriptionStatus.Notified"/> is terminal by design: somebody who wanted to know
    /// has been told, and a subscription that fired on every subsequent movement would be a mailing
    /// list nobody signed up for.
    /// </remarks>
    [Fact]
    public void A_notified_alert_is_finished()
    {
        var subscription = PriceDrop(target: 1_200m, priceAtSubscription: 2_000m);

        subscription.MarkNotified(new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero), 1_100m);

        Assert.False(subscription.IsActive);
        Assert.False(subscription.IsSatisfiedBy(900m));
    }

    private static StockSubscription PriceDrop(decimal? target, decimal? priceAtSubscription)
        => StockSubscription.Record(
            SubscriptionKind.PriceDrop,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            listingId: null,
            Guid.CreateVersion7(),
            email: null,
            target,
            priceAtSubscription,
            Expiry);
}
