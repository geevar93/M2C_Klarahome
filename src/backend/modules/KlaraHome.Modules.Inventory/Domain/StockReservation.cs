using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Inventory.Domain;

/// <summary>Where a hold on stock is in its short life.</summary>
internal enum ReservationStatus
{
    /// <summary>Live. The units are counted as reserved and cannot be sold to anybody else.</summary>
    Held = 0,

    /// <summary>The sale happened. The units left stock.</summary>
    Committed = 1,

    /// <summary>Given up deliberately — the cart changed, or the order was cancelled.</summary>
    Released = 2,

    /// <summary>Nobody settled it in time. The sweeper put the units back.</summary>
    Expired = 3,
}

/// <summary>
/// A time-limited hold on stock during checkout (docs/02-domain-model.md §2).
/// </summary>
/// <remarks>
/// <para>
/// Not a deduction. The units are still on hand and still on the seller's books; they are simply
/// not available to anybody else. That distinction is the reason there are two quantity columns
/// rather than one, and it is what lets an abandoned checkout cost nothing.
/// </para>
/// <para>
/// Every hold carries an expiry, because a hold that outlives the checkout that made it is stock
/// nobody can buy and nobody can find. Expiry is swept in the background and is itself a ledger
/// entry — "the units came back" is a movement, not a silent correction.
/// </para>
/// <para>
/// The <c>(referenceType, referenceId, lineReferenceId)</c> triple is unique while the hold is
/// live, which is what makes <c>HoldAsync</c> idempotent: a retried checkout finds its own hold
/// instead of taking the stock twice.
/// </para>
/// </remarks>
internal sealed class StockReservation : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private StockReservation(
        Guid id,
        Guid stockItemId,
        Guid listingId,
        int quantity,
        string referenceType,
        Guid referenceId,
        Guid lineReferenceId,
        DateTimeOffset expiresAt)
        : base(id)
    {
        StockItemId = Guard.NotEmpty(stockItemId);
        ListingId = Guard.NotEmpty(listingId);
        Quantity = Guard.Positive(quantity);
        ReferenceType = Guard.NotNullOrWhiteSpace(referenceType);
        ReferenceId = Guard.NotEmpty(referenceId);
        LineReferenceId = lineReferenceId;
        ExpiresAt = expiresAt;
        Status = ReservationStatus.Held;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private StockReservation() => ReferenceType = string.Empty;

    /// <summary>The stock row the units are held against.</summary>
    public Guid StockItemId { get; private set; }

    /// <summary>The offer, denormalised so a settle does not have to load the stock row to group by it.</summary>
    public Guid ListingId { get; private set; }

    /// <summary>How many units are held.</summary>
    public int Quantity { get; private set; }

    /// <summary>What is holding them: <c>cart</c> or <c>order</c>.</summary>
    public string ReferenceType { get; private set; }

    /// <summary>The cart or order.</summary>
    public Guid ReferenceId { get; private set; }

    /// <summary>The line within it, so two lines of one cart hold separately.</summary>
    public Guid LineReferenceId { get; private set; }

    /// <summary>Where the hold is in its life.</summary>
    public ReservationStatus Status { get; private set; }

    /// <summary>When it lapses if nobody settles it.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When it stopped being live, whichever way it went.</summary>
    public DateTimeOffset? SettledAt { get; private set; }

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

    /// <summary>Whether the units are still being held.</summary>
    public bool IsHeld => Status == ReservationStatus.Held;

    /// <summary>Places a hold.</summary>
    /// <param name="stockItemId">The stock row.</param>
    /// <param name="listingId">The offer.</param>
    /// <param name="quantity">How many units.</param>
    /// <param name="referenceType">What is holding them.</param>
    /// <param name="referenceId">The cart or order.</param>
    /// <param name="lineReferenceId">The line within it.</param>
    /// <param name="expiresAt">When it lapses.</param>
    public static StockReservation Hold(
        Guid stockItemId,
        Guid listingId,
        int quantity,
        string referenceType,
        Guid referenceId,
        Guid lineReferenceId,
        DateTimeOffset expiresAt)
        => new(
            UuidV7.New(),
            stockItemId,
            listingId,
            quantity,
            referenceType,
            referenceId,
            lineReferenceId,
            expiresAt);

    /// <summary>Settles the hold. Returns false if it was already settled.</summary>
    /// <remarks>
    /// The false return is what makes settling idempotent under at-least-once delivery: a
    /// redelivered cancellation finds the hold already released and moves nothing.
    /// </remarks>
    /// <param name="status">How it ended.</param>
    /// <param name="at">When.</param>
    public bool Settle(ReservationStatus status, DateTimeOffset at)
    {
        if (Status != ReservationStatus.Held || status == ReservationStatus.Held)
        {
            return false;
        }

        Status = status;
        SettledAt = at;
        return true;
    }

    /// <summary>Pushes the expiry out, for a checkout that is still in progress.</summary>
    /// <param name="expiresAt">The new expiry.</param>
    public void ExtendTo(DateTimeOffset expiresAt)
    {
        if (IsHeld && expiresAt > ExpiresAt)
        {
            ExpiresAt = expiresAt;
        }
    }
}
