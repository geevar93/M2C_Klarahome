using KlaraHome.Contracts.Pricing;
using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Carts.Domain;

/// <summary>How far through checkout the shopper has got.</summary>
/// <remarks>
/// The states are ordered and the order is the screen sequence, so "may this session be paid for"
/// is one comparison rather than four null checks spread across a handler.
/// </remarks>
internal enum CheckoutStatus
{
    /// <summary>Opened against a basket. Nothing chosen yet.</summary>
    Draft = 0,

    /// <summary>An address has been chosen, so the place of supply and serviceability are known.</summary>
    AddressSet = 1,

    /// <summary>A delivery service has been chosen for every seller in the basket.</summary>
    ShippingSet = 2,

    /// <summary>A payment method has been chosen and found eligible.</summary>
    PaymentSet = 3,

    /// <summary>A place-order request is in flight. Nothing else may touch it.</summary>
    Placing = 4,

    /// <summary>It became an order.</summary>
    Placed = 5,

    /// <summary>The shopper backed out.</summary>
    Abandoned = 6,

    /// <summary>It sat unfinished past its expiry.</summary>
    Expired = 7,
}

/// <summary>How the shopper is paying.</summary>
internal enum CheckoutPaymentMethod
{
    /// <summary>Paid before dispatch, through the gateway.</summary>
    Prepaid = 0,

    /// <summary>Cash collected at the door.</summary>
    CashOnDelivery = 1,
}

/// <summary>
/// An address frozen onto a checkout (docs/03-database-design.md §4.7). Stored as <c>jsonb</c>.
/// </summary>
/// <remarks>
/// A snapshot rather than a reference, and that is the point of it: an order is a record of what
/// was agreed, and an address the shopper edits between checkout and delivery must not silently
/// rewrite where the parcel was promised.
/// </remarks>
internal sealed class AddressSnapshot
{
    /// <summary>The address it was copied from, for support rather than for reading through.</summary>
    public Guid SourceAddressId { get; set; }

    /// <summary>Who the courier asks for at the door.</summary>
    public string RecipientName { get; set; } = string.Empty;

    /// <summary>The delivery contact number in E.164.</summary>
    public string Mobile { get; set; } = string.Empty;

    /// <summary>House or flat number and building.</summary>
    public string Line1 { get; set; } = string.Empty;

    /// <summary>Street, area or locality.</summary>
    public string? Line2 { get; set; }

    /// <summary>A nearby landmark.</summary>
    public string? Landmark { get; set; }

    /// <summary>City or town.</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>The <c>platform.states</c> row.</summary>
    public Guid StateId { get; set; }

    /// <summary>Six-digit PIN code.</summary>
    public string Pincode { get; set; } = string.Empty;

    /// <summary>A GSTIN this shipment is billed to, for a B2B invoice.</summary>
    public string? Gstin { get; set; }

    /// <summary>Whether it is a business address.</summary>
    public bool IsBusiness { get; set; }
}

/// <summary>
/// A shopper's run at paying for a basket (docs/03-database-design.md §4.7).
/// </summary>
/// <remarks>
/// <para>
/// It exists because a basket is not enough to place an order: an order needs an address, a
/// delivery choice per seller and a payment method, and those are decisions the shopper makes over
/// several requests. Holding them on the cart would mean an abandoned attempt leaving a half-made
/// decision on a basket the shopper comes back to next week.
/// </para>
/// <para>
/// The <see cref="QuoteSnapshot"/> is re-taken at review and again at placement. Orders copies it
/// rather than re-pricing, which is what makes the total on the confirmation the same number the
/// shopper agreed to.
/// </para>
/// </remarks>
internal sealed class CheckoutSession : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private readonly List<CheckoutShipment> _shipments = [];
    private readonly List<CheckoutPlacement> _placements = [];

    private CheckoutSession(Guid id, Guid cartId, Guid customerId, string currencyCode, DateTimeOffset expiresAt)
        : base(id)
    {
        CartId = cartId;
        CustomerId = customerId;
        CurrencyCode = currencyCode;
        Status = CheckoutStatus.Draft;
        PaymentMethod = CheckoutPaymentMethod.Prepaid;
        ExpiresAt = expiresAt;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private CheckoutSession() => CurrencyCode = Money.Inr;

    /// <summary>The basket being paid for.</summary>
    public Guid CartId { get; private set; }

    /// <summary>
    /// The shopper. Never null: checkout requires an account, because an order needs somebody to
    /// send it to and somebody to answer for it.
    /// </summary>
    public Guid CustomerId { get; private set; }

    /// <summary>How far through the shopper has got.</summary>
    public CheckoutStatus Status { get; private set; }

    /// <summary>ISO 4217 code every figure on it is in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>Where it is going.</summary>
    public AddressSnapshot? ShippingAddress { get; private set; }

    /// <summary>Who it is billed to.</summary>
    public AddressSnapshot? BillingAddress { get; private set; }

    /// <summary>
    /// The destination state, lifted out of the shipping snapshot into its own column because it
    /// decides CGST+SGST versus IGST and is therefore queried rather than merely displayed.
    /// </summary>
    public Guid? PlaceOfSupplyStateId { get; private set; }

    /// <summary>The GSTIN the shopper wants the invoice raised against, for a B2B purchase.</summary>
    public string? Gstin { get; private set; }

    /// <summary>How they are paying.</summary>
    public CheckoutPaymentMethod PaymentMethod { get; private set; }

    /// <summary>The agreed, itemised price. Serialised as <c>jsonb</c>.</summary>
    public QuoteResult? QuoteSnapshot { get; private set; }

    /// <summary>Delivery charged across the whole basket, inclusive of tax.</summary>
    public decimal ShippingTotal { get; private set; }

    /// <summary>What the shopper would pay, from the last snapshot taken.</summary>
    public decimal GrandTotal { get; private set; }

    /// <summary>When an unfinished attempt lapses.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When the order was created.</summary>
    public DateTimeOffset? PlacedAt { get; private set; }

    /// <summary>The order it became.</summary>
    public Guid? OrderId { get; private set; }

    /// <summary>The human-readable number of that order.</summary>
    public string? OrderNumber { get; private set; }

    /// <summary>One delivery choice per seller in the basket.</summary>
    public IReadOnlyList<CheckoutShipment> Shipments => _shipments;

    /// <summary>Every place-order attempt made against this session.</summary>
    public IReadOnlyList<CheckoutPlacement> Placements => _placements;

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

    /// <summary>Whether the shopper may still change anything.</summary>
    public bool IsOpen => Status
        is CheckoutStatus.Draft
        or CheckoutStatus.AddressSet
        or CheckoutStatus.ShippingSet
        or CheckoutStatus.PaymentSet;

    /// <summary>Opens a session against a basket.</summary>
    /// <param name="cartId">The basket.</param>
    /// <param name="customerId">The shopper.</param>
    /// <param name="currencyCode">ISO 4217 code the store trades in.</param>
    /// <param name="expiresAt">When an unfinished attempt lapses.</param>
    public static CheckoutSession Open(
        Guid cartId,
        Guid customerId,
        string currencyCode,
        DateTimeOffset expiresAt)
        => new(UuidV7.New(), Guard.NotEmpty(cartId), Guard.NotEmpty(customerId), currencyCode, expiresAt);

    /// <summary>
    /// Records the chosen addresses.
    /// </summary>
    /// <remarks>
    /// Changing the address after a delivery service was chosen clears the choice, and that is
    /// deliberate: a rate quoted to one PIN code is not a rate to another, and silently keeping it
    /// is how a shopper is charged Mumbai's shipping for a parcel to Shillong.
    /// </remarks>
    /// <param name="shipping">Where it goes.</param>
    /// <param name="billing">Who it is billed to.</param>
    /// <param name="gstin">The GSTIN to raise the invoice against.</param>
    public void SetAddresses(AddressSnapshot shipping, AddressSnapshot billing, string? gstin)
    {
        ArgumentNullException.ThrowIfNull(shipping);
        ArgumentNullException.ThrowIfNull(billing);

        var moved = ShippingAddress is null || ShippingAddress.Pincode != shipping.Pincode
                    || ShippingAddress.StateId != shipping.StateId;

        ShippingAddress = shipping;
        BillingAddress = billing;
        PlaceOfSupplyStateId = shipping.StateId;
        Gstin = string.IsNullOrWhiteSpace(gstin) ? null : gstin.Trim().ToUpperInvariant();

        if (moved)
        {
            _shipments.Clear();
            ShippingTotal = 0m;
        }

        Status = moved || Status < CheckoutStatus.AddressSet ? CheckoutStatus.AddressSet : Status;
    }

    /// <summary>Replaces the per-seller delivery choices wholesale.</summary>
    /// <param name="shipments">One per seller in the basket.</param>
    public void SetShipments(IEnumerable<CheckoutShipment> shipments)
    {
        ArgumentNullException.ThrowIfNull(shipments);

        _shipments.Clear();
        _shipments.AddRange(shipments);
        ShippingTotal = _shipments.Sum(shipment => shipment.Amount);
        Status = CheckoutStatus.ShippingSet;
    }

    /// <summary>Records how the shopper is paying.</summary>
    /// <param name="method">Prepaid or cash on delivery.</param>
    public void SetPaymentMethod(CheckoutPaymentMethod method)
    {
        PaymentMethod = method;
        Status = CheckoutStatus.PaymentSet;
    }

    /// <summary>Stores the price the shopper is being shown.</summary>
    /// <param name="quote">The itemised quote.</param>
    public void Snapshot(QuoteResult quote)
    {
        ArgumentNullException.ThrowIfNull(quote);

        QuoteSnapshot = quote;
        GrandTotal = quote.GrandTotal;
    }

    /// <summary>Takes the session out of the shopper's hands while an order is being created.</summary>
    public void BeginPlacing() => Status = CheckoutStatus.Placing;

    /// <summary>Hands the session back after a failed attempt, so the shopper can try again.</summary>
    /// <remarks>
    /// It returns to <see cref="CheckoutStatus.PaymentSet"/> rather than to Draft. A gateway that
    /// declined a card has not undone the address the shopper typed, and making them retype it is
    /// the surest way to lose the sale twice.
    /// </remarks>
    public void AbandonPlacing() => Status = CheckoutStatus.PaymentSet;

    /// <summary>Records the order this session became.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="orderNumber">Its human-readable number.</param>
    /// <param name="now">The current instant.</param>
    public void MarkPlaced(Guid orderId, string orderNumber, DateTimeOffset now)
    {
        Status = CheckoutStatus.Placed;
        OrderId = orderId;
        OrderNumber = orderNumber;
        PlacedAt = now;
    }

    /// <summary>The shopper backed out.</summary>
    public void MarkAbandoned() => Status = CheckoutStatus.Abandoned;

    /// <summary>It sat unfinished past its expiry.</summary>
    public bool MarkExpired()
    {
        if (!IsOpen)
        {
            return false;
        }

        Status = CheckoutStatus.Expired;
        return true;
    }

    /// <summary>Starts a place-order attempt and returns the record of it.</summary>
    /// <param name="idempotencyKey">The caller's key.</param>
    /// <param name="requestHash">A digest of the basket the key was used against.</param>
    /// <param name="now">The current instant.</param>
    public CheckoutPlacement BeginPlacement(string idempotencyKey, string requestHash, DateTimeOffset now)
    {
        var placement = CheckoutPlacement.Begin(Id, idempotencyKey, requestHash, now);
        _placements.Add(placement);
        return placement;
    }

    /// <summary>
    /// Drops an attempt that never became a row, because another request won the key.
    /// </summary>
    /// <remarks>
    /// The loser of the race on <c>(tenant_id, idempotency_key)</c> has to forget the placement it
    /// tried to make. Detaching it from the change tracker is not enough on its own: it is still on
    /// this collection, and the next save re-discovers it through the navigation and tries the
    /// insert again.
    /// </remarks>
    /// <param name="placement">The attempt to forget.</param>
    public void DiscardPlacement(CheckoutPlacement placement) => _placements.Remove(placement);
}

/// <summary>
/// What one seller's part of the basket will be shipped by (docs/03-database-design.md §4.7).
/// </summary>
/// <remarks>
/// One row per seller, which is what "per-vendor shipping and dispatch SLA" means in a marketplace:
/// two sellers ship separately, are quoted separately and promise separately. A single option on
/// the session cannot express that, and a basket that pretended otherwise would quote one delivery
/// date for two parcels.
/// </remarks>
internal sealed class CheckoutShipment : Entity<Guid>, ITenantScoped, IAuditable
{
    private CheckoutShipment(Guid id, Guid checkoutSessionId, Guid vendorId, string optionCode)
        : base(id)
    {
        CheckoutSessionId = checkoutSessionId;
        VendorId = vendorId;
        OptionCode = optionCode;
        ServiceName = optionCode;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private CheckoutShipment()
    {
        OptionCode = string.Empty;
        ServiceName = string.Empty;
    }

    /// <summary>The session it belongs to.</summary>
    public Guid CheckoutSessionId { get; private set; }

    /// <summary>The seller who will dispatch it.</summary>
    public Guid VendorId { get; private set; }

    /// <summary>
    /// The service the shopper chose. A code rather than an id, because a rate card is reissued and
    /// a saved checkout must not point at a row that has been replaced.
    /// </summary>
    public string OptionCode { get; private set; }

    /// <summary>What the shopper saw it called.</summary>
    public string ServiceName { get; private set; }

    /// <summary>The courier, when one has been decided.</summary>
    public string? Carrier { get; private set; }

    /// <summary>What it costs, inclusive of tax.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The tax inside that figure.</summary>
    public decimal TaxAmount { get; private set; }

    /// <summary>How long the seller has to hand the parcel over.</summary>
    public int DispatchSlaHours { get; private set; }

    /// <summary>Earliest delivery, in days from dispatch.</summary>
    public int PromisedMinDays { get; private set; }

    /// <summary>Latest delivery, in days from dispatch.</summary>
    public int PromisedMaxDays { get; private set; }

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

    /// <summary>Records a chosen service.</summary>
    /// <param name="checkoutSessionId">The session.</param>
    /// <param name="vendorId">The seller.</param>
    /// <param name="optionCode">The service code.</param>
    /// <param name="serviceName">What the shopper saw it called.</param>
    /// <param name="carrier">The courier, when decided.</param>
    /// <param name="amount">What it costs, inclusive of tax.</param>
    /// <param name="taxAmount">The tax inside that figure.</param>
    /// <param name="dispatchSlaHours">How long the seller has to dispatch.</param>
    /// <param name="promisedMinDays">Earliest delivery.</param>
    /// <param name="promisedMaxDays">Latest delivery.</param>
    public static CheckoutShipment Create(
        Guid checkoutSessionId,
        Guid vendorId,
        string optionCode,
        string serviceName,
        string? carrier,
        decimal amount,
        decimal taxAmount,
        int dispatchSlaHours,
        int promisedMinDays,
        int promisedMaxDays)
        => new(UuidV7.New(), checkoutSessionId, Guard.NotEmpty(vendorId), Guard.NotNullOrWhiteSpace(optionCode))
        {
            ServiceName = serviceName,
            Carrier = carrier,
            Amount = amount,
            TaxAmount = taxAmount,
            DispatchSlaHours = dispatchSlaHours,
            PromisedMinDays = promisedMinDays,
            PromisedMaxDays = promisedMaxDays,
        };
}

/// <summary>What happened to one place-order attempt.</summary>
internal enum PlacementStatus
{
    /// <summary>The attempt is running. A concurrent replay waits rather than starting a second one.</summary>
    InProgress = 0,

    /// <summary>An order was created. The stored response is replayed to any repeat of the key.</summary>
    Succeeded = 1,

    /// <summary>No order was created, and the shopper may try again.</summary>
    Failed = 2,
}

/// <summary>
/// One use of an idempotency key against a checkout (docs/03-database-design.md §4.7).
/// </summary>
/// <remarks>
/// <para>
/// This is what makes place-order idempotent, and it is a table rather than a cache because the
/// guarantee has to survive a restart: a shopper who double-taps <em>Pay</em> on a flaky connection
/// must get one order and two identical responses.
/// </para>
/// <para>
/// The unique index on the key is the enforcement — the second request loses the insert and reads
/// the winner's row — and <see cref="RequestHash"/> catches the other failure, a key replayed
/// against a different basket, which is answered with a conflict rather than with somebody else's
/// order.
/// </para>
/// </remarks>
internal sealed class CheckoutPlacement : Entity<Guid>, ITenantScoped, IAuditable
{
    private CheckoutPlacement(
        Guid id,
        Guid checkoutSessionId,
        string idempotencyKey,
        string requestHash,
        DateTimeOffset startedAt)
        : base(id)
    {
        CheckoutSessionId = checkoutSessionId;
        IdempotencyKey = idempotencyKey;
        RequestHash = requestHash;
        Status = PlacementStatus.InProgress;
        StartedAt = startedAt;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private CheckoutPlacement()
    {
        IdempotencyKey = string.Empty;
        RequestHash = string.Empty;
    }

    /// <summary>The session it was made against.</summary>
    public Guid CheckoutSessionId { get; private set; }

    /// <summary>The key the caller sent. Unique per tenant.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>A digest of the basket the key was used against.</summary>
    public string RequestHash { get; private set; }

    /// <summary>What happened.</summary>
    public PlacementStatus Status { get; private set; }

    /// <summary>The order that was created.</summary>
    public Guid? OrderId { get; private set; }

    /// <summary>Its human-readable number.</summary>
    public string? OrderNumber { get; private set; }

    /// <summary>The response body, replayed verbatim to a repeat of the key. Serialised as <c>jsonb</c>.</summary>
    public string? Response { get; private set; }

    /// <summary>The stable error code, when it failed.</summary>
    public string? FailureCode { get; private set; }

    /// <summary>When the attempt started.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>When it finished, either way.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

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

    /// <summary>Claims a key.</summary>
    /// <param name="checkoutSessionId">The session.</param>
    /// <param name="idempotencyKey">The caller's key.</param>
    /// <param name="requestHash">A digest of the basket it was used against.</param>
    /// <param name="startedAt">The current instant.</param>
    public static CheckoutPlacement Begin(
        Guid checkoutSessionId,
        string idempotencyKey,
        string requestHash,
        DateTimeOffset startedAt)
        => new(
            UuidV7.New(),
            checkoutSessionId,
            Guard.NotNullOrWhiteSpace(idempotencyKey),
            Guard.NotNullOrWhiteSpace(requestHash),
            startedAt);

    /// <summary>Records the order the attempt produced.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="orderNumber">Its number.</param>
    /// <param name="response">The response body to replay.</param>
    /// <param name="completedAt">The current instant.</param>
    public void Succeed(Guid orderId, string orderNumber, string response, DateTimeOffset completedAt)
    {
        Status = PlacementStatus.Succeeded;
        OrderId = orderId;
        OrderNumber = orderNumber;
        Response = response;
        CompletedAt = completedAt;
    }

    /// <summary>
    /// Records that the attempt produced nothing.
    /// </summary>
    /// <remarks>
    /// The row is kept rather than deleted, so support can see how many times a shopper tried and
    /// why each one failed. Keeping it is safe because <see cref="Restart"/> lets the same key be
    /// used again — an idempotency key guarantees "at most one order", and a failure created none.
    /// </remarks>
    /// <param name="failureCode">The stable error code.</param>
    /// <param name="completedAt">The current instant.</param>
    public void Fail(string failureCode, DateTimeOffset completedAt)
    {
        Status = PlacementStatus.Failed;
        FailureCode = failureCode;
        CompletedAt = completedAt;
    }

    /// <summary>
    /// Reopens a failed attempt so the same key may be tried again.
    /// </summary>
    /// <remarks>
    /// Without this, a declined card would leave the key permanently claimed and the shopper's next
    /// press of <em>Pay</em> — which browsers and mobile clients routinely send with the key they
    /// already have — would be answered with a conflict rather than with a payment.
    /// </remarks>
    /// <param name="startedAt">The current instant.</param>
    public void Restart(DateTimeOffset startedAt)
    {
        Status = PlacementStatus.InProgress;
        FailureCode = null;
        CompletedAt = null;
        StartedAt = startedAt;
    }
}
