using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Carts.Domain;

/// <summary>Where a basket is in its short life.</summary>
internal enum CartStatus
{
    /// <summary>The shopper is still using it. At most one of these exists per shopper.</summary>
    Active = 0,

    /// <summary>It became an order. Kept, because conversion analysis reads it.</summary>
    Converted = 1,

    /// <summary>It was left long enough to be worth chasing. Kept, because a campaign reads it.</summary>
    Abandoned = 2,

    /// <summary>It is past the retention window and is no longer of interest.</summary>
    Expired = 3,
}

/// <summary>
/// A shopper's basket (docs/03-database-design.md §4.7).
/// </summary>
/// <remarks>
/// <para>
/// A cart is either a signed-in shopper's or a browser's, and never neither:
/// <see cref="CustomerId"/> and <see cref="AnonymousTokenHash"/> are not both null, and
/// <see cref="AttachTo"/> — merge on login — is what turns the second kind into the first.
/// </para>
/// <para>
/// The token is held <b>hashed</b>. It is a bearer capability: anyone holding it can read and edit
/// the basket, so it is treated the way a refresh token is (docs/03-database-design.md §4.2) — the
/// value lives in the shopper's cookie, the row holds a digest of it, and a dump of this table
/// hands an attacker nobody's cart.
/// </para>
/// <para>
/// A cart holds <em>no stock</em>. Reservations are Inventory's, they are taken when an order is
/// placed and not when a line is added, and that is deliberate: holding stock at add-to-cart makes
/// every browsing shopper a denial of service against every buying one.
/// </para>
/// </remarks>
internal sealed class Cart : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private readonly List<CartLine> _lines = [];

    private Cart(
        Guid id,
        Guid? customerId,
        string? anonymousTokenHash,
        string currencyCode,
        DateTimeOffset now,
        TimeSpan lifetime)
        : base(id)
    {
        CustomerId = customerId;
        AnonymousTokenHash = anonymousTokenHash;
        CurrencyCode = currencyCode;
        Status = CartStatus.Active;
        LastActivityAt = now;
        ExpiresAt = now + lifetime;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Cart() => CurrencyCode = Money.Inr;

    /// <summary>The signed-in shopper, or null while the basket belongs only to a browser.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>A digest of the cookie token, or null once the basket has an owner.</summary>
    public string? AnonymousTokenHash { get; private set; }

    /// <summary>Where the basket is in its life.</summary>
    public CartStatus Status { get; private set; }

    /// <summary>ISO 4217 code every figure on it is in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>The coupon the shopper typed, kept so it survives a page reload.</summary>
    public string? CouponCode { get; private set; }

    /// <summary>
    /// How many lines are in it, excluding those saved for later. A derived cache maintained in the
    /// same transaction as the lines: the header badge is read on every page of the storefront, and
    /// counting a child table for it would be the most frequent query on the site.
    /// </summary>
    public int LineCount { get; private set; }

    /// <summary>When the shopper last touched it. Drives both expiry and abandonment.</summary>
    public DateTimeOffset LastActivityAt { get; private set; }

    /// <summary>When it lapses if nobody comes back.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When it became an order.</summary>
    public DateTimeOffset? ConvertedAt { get; private set; }

    /// <summary>The order it became.</summary>
    public Guid? ConvertedOrderId { get; private set; }

    /// <summary>When it was written off as abandoned.</summary>
    public DateTimeOffset? AbandonedAt { get; private set; }

    /// <summary>How many reminders have been sent about it.</summary>
    public int ReminderCount { get; private set; }

    /// <summary>When the last reminder went out.</summary>
    public DateTimeOffset? LastReminderAt { get; private set; }

    /// <summary>The lines, saved-for-later ones included.</summary>
    public IReadOnlyList<CartLine> Lines => _lines;

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

    /// <summary>Whether the basket may still be edited.</summary>
    public bool IsOpen => Status == CartStatus.Active;

    /// <summary>Opens a basket for a signed-in shopper.</summary>
    /// <param name="customerId">The shopper.</param>
    /// <param name="currencyCode">ISO 4217 code the store trades in.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="lifetime">How long an untouched basket lives.</param>
    public static Cart ForCustomer(Guid customerId, string currencyCode, DateTimeOffset now, TimeSpan lifetime)
        => new(UuidV7.New(), Guard.NotEmpty(customerId), null, currencyCode, now, lifetime);

    /// <summary>Opens a basket for a browser that has not signed in.</summary>
    /// <param name="tokenHash">A digest of the cookie token. Never the token itself.</param>
    /// <param name="currencyCode">ISO 4217 code the store trades in.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="lifetime">How long an untouched basket lives.</param>
    public static Cart ForGuest(string tokenHash, string currencyCode, DateTimeOffset now, TimeSpan lifetime)
        => new(UuidV7.New(), null, Guard.NotNullOrWhiteSpace(tokenHash), currencyCode, now, lifetime);

    /// <summary>
    /// Adds units of an offer, or increases the line that is already there. Returns the line.
    /// </summary>
    /// <remarks>
    /// Adding an offer that is already in the basket increases the existing line rather than
    /// creating a second one — the unique index says the same thing, and doing it here means a
    /// shopper who clicks "add" twice sees a quantity of two rather than a conflict.
    /// </remarks>
    /// <param name="listingId">The offer.</param>
    /// <param name="vendorId">Its seller, denormalised so grouping never costs a contract call.</param>
    /// <param name="quantity">How many units to add.</param>
    /// <param name="unitPrice">What it costs now, so a later price change can be pointed out.</param>
    /// <param name="maxQuantity">The most units this line may hold.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="lifetime">How long an untouched basket lives.</param>
    public CartLine Add(
        Guid listingId,
        Guid vendorId,
        int quantity,
        decimal unitPrice,
        int maxQuantity,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        var line = _lines.Find(candidate => candidate.ListingId == listingId);

        if (line is null)
        {
            line = CartLine.Create(Id, listingId, vendorId, Math.Min(quantity, maxQuantity), unitPrice, now);
            _lines.Add(line);
        }
        else
        {
            line.SetQuantity(Math.Min(line.Quantity + quantity, maxQuantity));
            line.Reprice(unitPrice, now);

            // Adding something that was set aside is the shopper changing their mind about it.
            line.SetSavedForLater(false);
        }

        Touch(now, lifetime);
        return line;
    }

    /// <summary>Finds a line by its own id.</summary>
    /// <param name="lineId">The line.</param>
    public CartLine? FindLine(Guid lineId) => _lines.Find(line => line.Id == lineId);

    /// <summary>Removes a line.</summary>
    /// <param name="line">The line to remove.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="lifetime">How long an untouched basket lives.</param>
    public void Remove(CartLine line, DateTimeOffset now, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(line);

        _lines.Remove(line);
        Touch(now, lifetime);
    }

    /// <summary>Empties the basket, coupon included.</summary>
    /// <param name="now">The current instant.</param>
    /// <param name="lifetime">How long an untouched basket lives.</param>
    public void Clear(DateTimeOffset now, TimeSpan lifetime)
    {
        _lines.Clear();
        CouponCode = null;
        Touch(now, lifetime);
    }

    /// <summary>
    /// Records the coupon the shopper typed, or clears it.
    /// </summary>
    /// <remarks>
    /// Storing it does not mean it is valid. Whether a code applies is the quote engine's answer,
    /// and it is re-asked on every render because a code that worked this morning may have run out
    /// of uses since.
    /// </remarks>
    /// <param name="code">The code, or null to remove it.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="lifetime">How long an untouched basket lives.</param>
    public void SetCoupon(string? code, DateTimeOffset now, TimeSpan lifetime)
    {
        CouponCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
        Touch(now, lifetime);
    }

    /// <summary>
    /// Gives an anonymous basket an owner, on login.
    /// </summary>
    /// <remarks>
    /// The token is dropped at the same time. A cart still reachable by the cookie after it has
    /// been claimed would be a signed-in shopper's basket editable by whoever else has that
    /// browser.
    /// </remarks>
    /// <param name="customerId">The shopper who just signed in.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="lifetime">How long an untouched basket lives.</param>
    public void AttachTo(Guid customerId, DateTimeOffset now, TimeSpan lifetime)
    {
        CustomerId = Guard.NotEmpty(customerId);
        AnonymousTokenHash = null;
        Touch(now, lifetime);
    }

    /// <summary>
    /// Takes every line of another basket into this one.
    /// </summary>
    /// <remarks>
    /// Quantities are summed and then clamped, and the coupon follows only if this basket has none:
    /// overwriting a code the shopper typed while signed in with one they typed as a guest would
    /// silently change what they are paying.
    /// </remarks>
    /// <param name="other">The basket being folded in.</param>
    /// <param name="maxQuantityPerLine">The most units one line may hold.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="lifetime">How long an untouched basket lives.</param>
    public void MergeFrom(Cart other, int maxQuantityPerLine, DateTimeOffset now, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(other);

        foreach (var source in other.Lines)
        {
            var line = _lines.Find(candidate => candidate.ListingId == source.ListingId);

            if (line is null)
            {
                _lines.Add(CartLine.Create(
                    Id,
                    source.ListingId,
                    source.VendorId,
                    Math.Min(source.Quantity, maxQuantityPerLine),
                    source.UnitPriceAtAdd,
                    now));
            }
            else
            {
                line.SetQuantity(Math.Min(line.Quantity + source.Quantity, maxQuantityPerLine));
            }
        }

        CouponCode ??= other.CouponCode;

        Touch(now, lifetime);
    }

    /// <summary>Marks the basket as having become an order.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="now">The current instant.</param>
    public void MarkConverted(Guid orderId, DateTimeOffset now)
    {
        Status = CartStatus.Converted;
        ConvertedOrderId = orderId;
        ConvertedAt = now;
    }

    /// <summary>
    /// Marks the basket abandoned. Returns false if it already was, which is what keeps a reminder
    /// campaign to one message per basket rather than one per sweep.
    /// </summary>
    /// <param name="now">The current instant.</param>
    public bool MarkAbandoned(DateTimeOffset now)
    {
        if (Status != CartStatus.Active)
        {
            return false;
        }

        Status = CartStatus.Abandoned;
        AbandonedAt = now;
        return true;
    }

    /// <summary>Retires the basket for good. Returns false if there was nothing to retire.</summary>
    /// <param name="now">The current instant.</param>
    public bool MarkExpired(DateTimeOffset now)
    {
        if (Status is CartStatus.Converted or CartStatus.Expired)
        {
            return false;
        }

        Status = CartStatus.Expired;
        ExpiresAt = now;
        return true;
    }

    /// <summary>Records that a reminder was sent.</summary>
    /// <param name="now">The current instant.</param>
    public void RecordReminder(DateTimeOffset now)
    {
        ReminderCount++;
        LastReminderAt = now;
    }

    /// <summary>
    /// Restamps the activity clock, pushes the expiry out, and recounts the badge.
    /// </summary>
    /// <remarks>
    /// Called from every mutation rather than left to the handlers. A basket whose expiry is
    /// refreshed on some edits and not others expires while the shopper is using it, and which
    /// edits were forgotten is not a question anybody can answer from a bug report.
    /// </remarks>
    /// <param name="now">The current instant.</param>
    /// <param name="lifetime">How long an untouched basket lives.</param>
    public void Touch(DateTimeOffset now, TimeSpan lifetime)
    {
        LastActivityAt = now;
        ExpiresAt = now + lifetime;
        LineCount = _lines.Count(line => !line.SavedForLater);
    }
}

/// <summary>
/// One offer in a basket (docs/03-database-design.md §4.7).
/// </summary>
/// <remarks>
/// <see cref="UnitPriceAtAdd"/> is <b>not</b> a price the shopper is owed. The quote engine is the
/// only thing on this platform permitted to price anything; this figure exists so the cart can say
/// "the price of this changed since you added it", which is a disclosure that has to be made before
/// payment and is unanswerable without something to compare against.
/// </remarks>
internal sealed class CartLine : Entity<Guid>, ITenantScoped, IAuditable
{
    private CartLine(
        Guid id,
        Guid cartId,
        Guid listingId,
        Guid vendorId,
        int quantity,
        decimal unitPriceAtAdd,
        DateTimeOffset now)
        : base(id)
    {
        CartId = cartId;
        ListingId = listingId;
        VendorId = vendorId;
        Quantity = quantity;
        UnitPriceAtAdd = unitPriceAtAdd;
        PricedAt = now;
        AddedAt = now;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private CartLine()
    {
    }

    /// <summary>The basket it belongs to.</summary>
    public Guid CartId { get; private set; }

    /// <summary>The offer. A plain id: no foreign key crosses a schema.</summary>
    public Guid ListingId { get; private set; }

    /// <summary>
    /// The seller, denormalised from the offer when the line was added. Grouping, per-seller
    /// shipping and the sub-order split all key on it, and re-asking Catalog on every render would
    /// be a contract call per line on the hottest path in the basket.
    /// </summary>
    public Guid VendorId { get; private set; }

    /// <summary>How many units.</summary>
    public int Quantity { get; private set; }

    /// <summary>Whether the shopper set it aside rather than removing it.</summary>
    public bool SavedForLater { get; private set; }

    /// <summary>What one unit cost the last time the shopper was shown a price for it.</summary>
    public decimal UnitPriceAtAdd { get; private set; }

    /// <summary>When that price was stamped.</summary>
    public DateTimeOffset PricedAt { get; private set; }

    /// <summary>When the line first appeared.</summary>
    public DateTimeOffset AddedAt { get; private set; }

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

    /// <summary>Creates a line. Called by <see cref="Cart.Add"/> and by a merge, and nowhere else.</summary>
    /// <param name="cartId">The basket.</param>
    /// <param name="listingId">The offer.</param>
    /// <param name="vendorId">Its seller.</param>
    /// <param name="quantity">How many units.</param>
    /// <param name="unitPriceAtAdd">What it cost when it went in.</param>
    /// <param name="now">The current instant.</param>
    public static CartLine Create(
        Guid cartId,
        Guid listingId,
        Guid vendorId,
        int quantity,
        decimal unitPriceAtAdd,
        DateTimeOffset now)
        => new(UuidV7.New(), cartId, Guard.NotEmpty(listingId), vendorId, Math.Max(quantity, 1), unitPriceAtAdd, now);

    /// <summary>Sets the quantity, never below one.</summary>
    /// <param name="quantity">How many units.</param>
    public void SetQuantity(int quantity) => Quantity = Math.Max(quantity, 1);

    /// <summary>Sets the line aside, or brings it back.</summary>
    /// <param name="savedForLater">Whether it is set aside.</param>
    public void SetSavedForLater(bool savedForLater) => SavedForLater = savedForLater;

    /// <summary>
    /// Restamps the price the shopper has been shown, once the change has been disclosed to them.
    /// </summary>
    /// <param name="unitPrice">The price now.</param>
    /// <param name="now">The current instant.</param>
    public void Reprice(decimal unitPrice, DateTimeOffset now)
    {
        UnitPriceAtAdd = unitPrice;
        PricedAt = now;
    }
}
