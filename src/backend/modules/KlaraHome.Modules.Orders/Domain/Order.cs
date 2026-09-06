using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Orders.Domain;

/// <summary>
/// An address frozen onto an order (docs/03-database-design.md §4.8). Stored as <c>jsonb</c>.
/// </summary>
/// <remarks>
/// A snapshot rather than a reference, and that is the whole point of it: an order is a record of
/// what was agreed, and an address the shopper edits next month must not silently rewrite where a
/// parcel was promised or which state the GST was charged against.
/// </remarks>
internal sealed class OrderAddressSnapshot
{
    /// <summary>Who the courier asks for at the door.</summary>
    public string RecipientName { get; set; } = string.Empty;

    /// <summary>The delivery contact number in E.164.</summary>
    public string Mobile { get; set; } = string.Empty;

    /// <summary>House or flat number and building.</summary>
    public string Line1 { get; set; } = string.Empty;

    /// <summary>Street, area or locality.</summary>
    public string? Line2 { get; set; }

    /// <summary>A nearby landmark. Not decoration in India — couriers navigate by it.</summary>
    public string? Landmark { get; set; }

    /// <summary>City or town.</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>The <c>platform.states</c> row.</summary>
    public Guid StateId { get; set; }

    /// <summary>The state's name at the time of the order, so a reprint needs no lookup.</summary>
    public string? StateName { get; set; }

    /// <summary>The GST state code the place of supply was decided from.</summary>
    public string? StateCode { get; set; }

    /// <summary>Six-digit PIN code.</summary>
    public string Pincode { get; set; } = string.Empty;

    /// <summary>The GSTIN this shipment is billed to, for a B2B invoice.</summary>
    public string? Gstin { get; set; }
}

/// <summary>
/// The shopper, as they were when they ordered (docs/03-database-design.md §4.8). Stored as
/// <c>jsonb</c>.
/// </summary>
/// <remarks>
/// Copied for the same reason the address is. A support conversation about an order from March has
/// to show the name and number the order actually carried, and an account whose email has since
/// changed must not rewrite an invoice that was already issued.
/// </remarks>
internal sealed class OrderCustomerSnapshot
{
    /// <summary>The name on the invoice.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Their email address at the time, or null.</summary>
    public string? Email { get; set; }

    /// <summary>Their mobile number in E.164 at the time, or null.</summary>
    public string? Mobile { get; set; }

    /// <summary>The GSTIN the invoice was raised against, for a B2B purchase.</summary>
    public string? Gstin { get; set; }
}

/// <summary>
/// A sale, as the platform records it (docs/02-domain-model.md §4.4).
/// </summary>
/// <remarks>
/// <para>
/// The root, and deliberately a thin one. It holds who bought, where it goes, what the whole thing
/// came to and how it is being paid for; everything that is <em>fulfilled</em> — lines, statuses,
/// invoices, timelines — hangs off a <see cref="SubOrder"/>, one per seller. That split is not a
/// modelling preference, it is what a marketplace is: two sellers dispatch separately, invoice
/// separately under their own GSTINs, and are settled separately.
/// </para>
/// <para>
/// Its totals are a snapshot of the agreed quote, and they never move. A cancellation does not
/// rewrite them — it records cancelled quantities on the lines, and what is still owed is derived.
/// An order whose totals changed after the fact would be an order nobody could reconcile against the
/// confirmation the shopper was sent.
/// </para>
/// <para>
/// Its <see cref="Status"/> is likewise derived rather than set (docs/02-domain-model.md §5.2). It
/// is stored so an order list can be filtered on it, and it is recomputed from the sub-orders on
/// every write, so the stored value can never disagree with the parts.
/// </para>
/// </remarks>
internal sealed class Order : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private readonly List<SubOrder> _subOrders = [];
    private readonly List<OrderEvent> _events = [];

    private Order(Guid id, string orderNumber, Guid customerId, Guid cartId, string currencyCode)
        : base(id)
    {
        OrderNumber = orderNumber;
        CustomerId = customerId;
        CartId = cartId;
        CurrencyCode = currencyCode;
        Status = OrderStatus.PendingPayment;
        PaymentStatus = OrderPaymentStatus.Pending;
        CustomerSnapshot = new OrderCustomerSnapshot();
        ShippingAddress = new OrderAddressSnapshot();
        BillingAddress = new OrderAddressSnapshot();
        Channel = OrderChannels.Web;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Order()
    {
        OrderNumber = string.Empty;
        CurrencyCode = Money.Inr;
        CustomerSnapshot = new OrderCustomerSnapshot();
        ShippingAddress = new OrderAddressSnapshot();
        BillingAddress = new OrderAddressSnapshot();
        Channel = OrderChannels.Web;
    }

    /// <summary>The human-readable number a shopper quotes to support. Unique per tenant.</summary>
    public string OrderNumber { get; private set; }

    /// <summary>The shopper.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>
    /// The basket it came from.
    /// </summary>
    /// <remarks>
    /// Kept because it is the <em>stock-reservation reference</em>: the units were held against the
    /// cart before the order existed, and this module settles those holds when the order is confirmed
    /// or falls through. Without it the holds could only be found by expiring.
    /// </remarks>
    public Guid CartId { get; private set; }

    /// <summary>The checkout session that produced it, for tracing and for support.</summary>
    public Guid CheckoutSessionId { get; private set; }

    /// <summary>The shopper as they were at placement.</summary>
    public OrderCustomerSnapshot CustomerSnapshot { get; private set; }

    /// <summary>Where it goes.</summary>
    public OrderAddressSnapshot ShippingAddress { get; private set; }

    /// <summary>Who it is billed to.</summary>
    public OrderAddressSnapshot BillingAddress { get; private set; }

    /// <summary>
    /// The destination state, lifted out of the shipping snapshot into its own column because it
    /// decides CGST+SGST versus IGST and is therefore queried and reported on, not merely displayed.
    /// </summary>
    public Guid PlaceOfSupplyStateId { get; private set; }

    /// <summary>Derived from the sub-orders on every write; never set by a caller.</summary>
    public OrderStatus Status { get; private set; }

    /// <summary>How the shopper is paying.</summary>
    public OrderPaymentMethod PaymentMethod { get; private set; }

    /// <summary>Where the money stands. Mirrored from Payments and eventually consistent.</summary>
    public OrderPaymentStatus PaymentStatus { get; private set; }

    /// <summary>The lines' gross, before any discount.</summary>
    public decimal ItemsTotal { get; private set; }

    /// <summary>Every discount, line-level and order-level.</summary>
    public decimal DiscountTotal { get; private set; }

    /// <summary>Delivery charged across the order, inclusive of its tax.</summary>
    public decimal ShippingTotal { get; private set; }

    /// <summary>CGST + SGST + IGST + cess across the order.</summary>
    public decimal TaxTotal { get; private set; }

    /// <summary>The cash-on-delivery handling fee, inclusive of its tax. Zero when prepaid.</summary>
    public decimal CodFee { get; private set; }

    /// <summary>Store credit redeemed against it at placement.</summary>
    public decimal WalletApplied { get; private set; }

    /// <summary>What was added or taken off to land the total on a whole rupee.</summary>
    public decimal RoundingAdjustment { get; private set; }

    /// <summary>What the shopper agreed to pay.</summary>
    public decimal GrandTotal { get; private set; }

    /// <summary>The grand total less store credit: what the gateway was asked for.</summary>
    public decimal AmountPayable { get; private set; }

    /// <summary>ISO 4217 code every figure on it is in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>The coupon that was applied, if any.</summary>
    public string? CouponCode { get; private set; }

    /// <summary>Where the order came from: <c>web</c>, <c>app</c>, <c>admin</c>.</summary>
    public string Channel { get; private set; }

    /// <summary>When it was placed.</summary>
    public DateTimeOffset PlacedAt { get; private set; }

    /// <summary>When every part of it reached a successful end.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>When every part of it was cancelled.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Why, when the whole order was cancelled.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>The operator's running note on the order. Never shown to the shopper.</summary>
    public string? Notes { get; private set; }

    /// <summary>One per seller. An order always has at least one.</summary>
    public IReadOnlyList<SubOrder> SubOrders => _subOrders;

    /// <summary>The append-only timeline, order-level entries included.</summary>
    public IReadOnlyList<OrderEvent> Events => _events;

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

    /// <summary>What is still owed once cancelled units are taken off. Derived, never stored.</summary>
    /// <remarks>
    /// The counterpart of the frozen totals: the agreed figures stay as they were, and what a
    /// cancellation changes is this. Payments and Returns decide what to give back; this is the
    /// figure they reconcile against.
    /// </remarks>
    public decimal NetTotal => GrandTotal - CancelledTotal;

    /// <summary>What has come off the bill through cancellation, across every seller.</summary>
    public decimal CancelledTotal => _subOrders.Sum(subOrder => subOrder.CancelledTotal);

    /// <summary>Opens an order against an agreed checkout.</summary>
    /// <param name="orderNumber">The allocated number.</param>
    /// <param name="customerId">The shopper.</param>
    /// <param name="cartId">The basket, which is also the stock-reservation reference.</param>
    /// <param name="checkoutSessionId">The session it came from.</param>
    /// <param name="currencyCode">ISO 4217 code every figure is in.</param>
    public static Order Place(
        string orderNumber,
        Guid customerId,
        Guid cartId,
        Guid checkoutSessionId,
        string currencyCode)
        => new(
            UuidV7.New(),
            Guard.NotNullOrWhiteSpace(orderNumber),
            Guard.NotEmpty(customerId),
            Guard.NotEmpty(cartId),
            currencyCode)
        {
            CheckoutSessionId = checkoutSessionId,
        };

    /// <summary>Freezes who bought it and where it goes.</summary>
    /// <param name="customer">The shopper as they were.</param>
    /// <param name="shipping">Where it goes.</param>
    /// <param name="billing">Who it is billed to.</param>
    public void Capture(
        OrderCustomerSnapshot customer,
        OrderAddressSnapshot shipping,
        OrderAddressSnapshot billing)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(shipping);
        ArgumentNullException.ThrowIfNull(billing);

        CustomerSnapshot = customer;
        ShippingAddress = shipping;
        BillingAddress = billing;
        PlaceOfSupplyStateId = shipping.StateId;
    }

    /// <summary>Freezes the agreed price and how it is being paid.</summary>
    /// <param name="method">Prepaid or cash on delivery.</param>
    /// <param name="itemsTotal">The lines' gross.</param>
    /// <param name="discountTotal">Every discount.</param>
    /// <param name="shippingTotal">Delivery, inclusive of its tax.</param>
    /// <param name="taxTotal">Every tax figure.</param>
    /// <param name="codFee">The cash-on-delivery handling fee.</param>
    /// <param name="walletApplied">Store credit redeemed.</param>
    /// <param name="roundingAdjustment">What was rounded off.</param>
    /// <param name="grandTotal">What the shopper agreed to pay.</param>
    /// <param name="amountPayable">What a gateway is asked for.</param>
    /// <param name="couponCode">The code applied, if any.</param>
    /// <param name="channel">Where the order came from.</param>
    /// <param name="placedAt">When it was placed.</param>
    public void Price(
        OrderPaymentMethod method,
        decimal itemsTotal,
        decimal discountTotal,
        decimal shippingTotal,
        decimal taxTotal,
        decimal codFee,
        decimal walletApplied,
        decimal roundingAdjustment,
        decimal grandTotal,
        decimal amountPayable,
        string? couponCode,
        string channel,
        DateTimeOffset placedAt)
    {
        PaymentMethod = method;
        ItemsTotal = itemsTotal;
        DiscountTotal = discountTotal;
        ShippingTotal = shippingTotal;
        TaxTotal = taxTotal;
        CodFee = codFee;
        WalletApplied = walletApplied;
        RoundingAdjustment = roundingAdjustment;
        GrandTotal = grandTotal;
        AmountPayable = amountPayable;
        CouponCode = string.IsNullOrWhiteSpace(couponCode) ? null : couponCode.Trim().ToUpperInvariant();
        Channel = Guard.NotNullOrWhiteSpace(channel);
        PlacedAt = placedAt;
    }

    /// <summary>Attaches one seller's part.</summary>
    /// <param name="subOrder">The sub-order.</param>
    public void AddSubOrder(SubOrder subOrder)
    {
        ArgumentNullException.ThrowIfNull(subOrder);
        _subOrders.Add(subOrder);
    }

    /// <summary>Appends a timeline entry.</summary>
    /// <param name="entry">What happened.</param>
    public void Record(OrderEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _events.Add(entry);
    }

    /// <summary>Records where the money stands, as Payments reports it.</summary>
    /// <param name="status">The new payment status.</param>
    public void SetPaymentStatus(OrderPaymentStatus status) => PaymentStatus = status;

    /// <summary>Replaces the operator's note.</summary>
    /// <param name="notes">The note, or null to clear it.</param>
    public void SetNotes(string? notes)
        => Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    /// <summary>
    /// Recomputes the order's status from its parts, and stamps the terminal instants.
    /// </summary>
    /// <remarks>
    /// Called after every sub-order transition rather than by the transition itself, so there is one
    /// place the derivation happens and no path that can forget it. The instants are set once: an
    /// order that completes does not complete again, and a cancelled order that is somehow revived
    /// keeps the record of when it was cancelled.
    /// </remarks>
    /// <param name="at">The current instant.</param>
    /// <returns>Whether the derived status changed.</returns>
    public bool Rederive(DateTimeOffset at)
    {
        var derived = OrderStatusRules.Derive([.. _subOrders.Select(subOrder => subOrder.Status)]);

        if (derived == Status)
        {
            return false;
        }

        Status = derived;

        switch (derived)
        {
            case OrderStatus.Completed:
                CompletedAt ??= at;
                break;
            case OrderStatus.Cancelled:
                CancelledAt ??= at;
                CancellationReason ??= _subOrders
                    .Select(subOrder => subOrder.CancellationReason)
                    .FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason));
                break;
            case OrderStatus.PendingPayment:
            case OrderStatus.InProgress:
            default:
                break;
        }

        return true;
    }
}

/// <summary>The <c>channel</c> values an order may carry, so callers cannot spell them differently.</summary>
internal static class OrderChannels
{
    /// <summary>The storefront in a browser.</summary>
    public const string Web = "web";

    /// <summary>A native or wrapped mobile app.</summary>
    public const string App = "app";

    /// <summary>Placed by staff on a shopper's behalf.</summary>
    public const string Admin = "admin";

    /// <summary>Every channel this platform understands.</summary>
    public static readonly IReadOnlyList<string> All = [Web, App, Admin];

    /// <summary>Whether a value is one of them, case-insensitively.</summary>
    /// <param name="channel">The candidate.</param>
    public static bool Contains(string? channel)
        => channel is not null && All.Any(known => string.Equals(known, channel, StringComparison.OrdinalIgnoreCase));
}
