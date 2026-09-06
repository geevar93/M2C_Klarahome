using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Reviews.Domain;

/// <summary>What a shopper asked to be told about (docs/03-database-design.md §4.15).</summary>
internal enum SubscriptionKind
{
    /// <summary>Tell me when it can be bought again.</summary>
    BackInStock = 0,

    /// <summary>Tell me when it costs less than it does now, or less than a figure I named.</summary>
    PriceDrop = 1,
}

/// <summary>Where a subscription stands.</summary>
/// <remarks>
/// <see cref="Notified"/> is terminal and that is the design. A back-in-stock alert fires once and
/// stops: a shopper who wanted to know when something returned has been told, and a subscription
/// that fired again on every restock would be a subscription to a mailing list nobody signed up for.
/// Wanting to be told again is a new subscription, which is one tap.
/// </remarks>
internal enum SubscriptionStatus
{
    /// <summary>Waiting for the thing to happen.</summary>
    Active = 0,

    /// <summary>It happened and the shopper was told. Terminal.</summary>
    Notified = 1,

    /// <summary>The shopper withdrew it. Terminal.</summary>
    Cancelled = 2,

    /// <summary>Nothing happened for long enough that the interest is assumed gone. Terminal.</summary>
    Expired = 3,
}

/// <summary>
/// A standing request to be told when something changes about a product
/// (docs/03-database-design.md §4.15).
/// </summary>
/// <remarks>
/// <para>
/// Keyed on the variant, because both questions are about a variant: the grey one being back in
/// stock says nothing about the beige one, and a price drop on the small says nothing about the
/// large. It carries the listing where the shopper was looking at a specific seller's offer, and
/// null where they were not — a back-in-stock alert on the product page is about the buy box, and
/// which seller wins it may change before the alert fires.
/// </para>
/// <para>
/// <see cref="TargetPrice"/> makes a price-drop subscription mean something precise. Where it is
/// set, the alert fires when the price reaches it; where it is not, <see cref="PriceAtSubscription"/>
/// is the benchmark and any drop below it fires. Recording the price at subscription time is what
/// stops the second case from being unanswerable — without it, "cheaper than what?" has no answer
/// once the price has moved twice.
/// </para>
/// <para>
/// Both alerts are Marketing rather than transactional in the notification model, and a recipient
/// who has opted out of Marketing gets a suppressed row. That is not an oversight: nobody ordered
/// anything, and treating an expression of interest in one product as consent to be messaged is
/// exactly what a preference centre exists to prevent.
/// </para>
/// </remarks>
internal sealed class StockSubscription : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private StockSubscription(Guid id, SubscriptionKind kind, Guid variantId, Guid productId)
        : base(id)
    {
        Kind = kind;
        VariantId = variantId;
        ProductId = productId;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private StockSubscription()
    {
    }

    /// <summary>What they asked to be told about.</summary>
    public SubscriptionKind Kind { get; private set; }

    /// <summary>The sellable thing.</summary>
    public Guid VariantId { get; private set; }

    /// <summary>Its product, for the message the shopper reads.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>The seller's offer they were looking at, or null for the buy box.</summary>
    public Guid? ListingId { get; private set; }

    /// <summary>The shopper, when they were signed in.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>Where to write, for a shopper who was not signed in.</summary>
    /// <remarks>
    /// The one piece of personal data this module collects from somebody with no account, and it is
    /// collected because the feature is worthless without it: a back-in-stock alert with nowhere to
    /// send it is a row nobody will ever read. It is a contact address and is treated as one —
    /// docs/07-security-compliance.md §4 — and the row is swept when the subscription expires.
    /// </remarks>
    public string? Email { get; private set; }

    /// <summary>Where the subscription stands.</summary>
    public SubscriptionStatus Status { get; private set; } = SubscriptionStatus.Active;

    /// <summary>The price the shopper is waiting for, for a price-drop alert.</summary>
    public decimal? TargetPrice { get; private set; }

    /// <summary>What it cost when they subscribed, which is the benchmark when no target was named.</summary>
    public decimal? PriceAtSubscription { get; private set; }

    /// <summary>When the alert fired, in UTC.</summary>
    public DateTimeOffset? NotifiedAt { get; private set; }

    /// <summary>What the price was when it fired, for the message and for a support question later.</summary>
    public decimal? NotifiedPrice { get; private set; }

    /// <summary>When it stops being watched, in UTC.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Whether it is still waiting.</summary>
    public bool IsActive => Status == SubscriptionStatus.Active;

    /// <summary>Records a standing request.</summary>
    /// <param name="kind">What to watch for.</param>
    /// <param name="variantId">The sellable thing.</param>
    /// <param name="productId">Its product.</param>
    /// <param name="listingId">The seller's offer, or null for the buy box.</param>
    /// <param name="customerId">The shopper, when signed in.</param>
    /// <param name="email">Where to write, when they are not.</param>
    /// <param name="targetPrice">The price they are waiting for, for a price-drop alert.</param>
    /// <param name="priceAtSubscription">What it costs today.</param>
    /// <param name="expiresAt">When to stop watching.</param>
    public static StockSubscription Record(
        SubscriptionKind kind,
        Guid variantId,
        Guid productId,
        Guid? listingId,
        Guid? customerId,
        string? email,
        decimal? targetPrice,
        decimal? priceAtSubscription,
        DateTimeOffset expiresAt)
        => new(UuidV7.New(), kind, Guard.NotEmpty(variantId), Guard.NotEmpty(productId))
        {
            ListingId = listingId,
            CustomerId = customerId,
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant(),
            TargetPrice = targetPrice,
            PriceAtSubscription = priceAtSubscription,
            ExpiresAt = expiresAt,
        };

    /// <summary>
    /// Whether the price now would satisfy this subscription.
    /// </summary>
    /// <remarks>
    /// Where a target was named, the test is against the target; where it was not, it is against the
    /// price when the shopper subscribed. Both are strict comparisons — a price returning to exactly
    /// what it was is not a drop, and firing on it would send an alert about nothing.
    /// </remarks>
    /// <param name="price">What it costs now.</param>
    public bool IsSatisfiedBy(decimal price)
    {
        if (Kind != SubscriptionKind.PriceDrop || !IsActive)
        {
            return false;
        }

        if (TargetPrice is { } target)
        {
            return price <= target;
        }

        return PriceAtSubscription is { } benchmark && price < benchmark;
    }

    /// <summary>Records that the shopper was told, and closes the subscription.</summary>
    /// <param name="at">When.</param>
    /// <param name="price">What the price was.</param>
    public void MarkNotified(DateTimeOffset at, decimal? price)
    {
        Status = SubscriptionStatus.Notified;
        NotifiedAt = at;
        NotifiedPrice = price;
    }

    /// <summary>Withdraws it at the shopper's request.</summary>
    public void Cancel() => Status = SubscriptionStatus.Cancelled;

    /// <summary>Closes it because nothing happened for long enough.</summary>
    public void Expire() => Status = SubscriptionStatus.Expired;
}
