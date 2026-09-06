namespace KlaraHome.Modules.Orders.Domain;

/// <summary>
/// The life of one seller's part of an order (docs/02-domain-model.md §5.1).
/// </summary>
/// <remarks>
/// <para>
/// The workhorse of the whole platform. The <em>order</em> has no life of its own — its status is
/// derived from these (see <see cref="OrderStatusRules"/>) — because in a marketplace two sellers
/// pack, dispatch and occasionally fail independently, and a single status on the order could only
/// ever describe one of them.
/// </para>
/// <para>
/// The numbers are stable and ordered by how far through fulfilment the state is, so
/// "has this been dispatched yet" is one comparison. They are persisted as strings, so a gap or a
/// reordering would be a display change and never a data migration.
/// </para>
/// </remarks>
internal enum SubOrderStatus
{
    /// <summary>Created and waiting for the shopper's money. Where every prepaid order starts.</summary>
    PendingPayment = 0,

    /// <summary>The gateway declined, timed out, or the shopper abandoned the payment page.</summary>
    PaymentFailed = 10,

    /// <summary>Paid, or accepted on cash on delivery. The stock behind it is committed here.</summary>
    Confirmed = 20,

    /// <summary>The seller has accepted it and is picking it.</summary>
    Processing = 30,

    /// <summary>Packed and waiting for a courier.</summary>
    Packed = 40,

    /// <summary>Handed to the courier, with an AWB against it.</summary>
    Shipped = 50,

    /// <summary>Out with the delivery agent.</summary>
    OutForDelivery = 60,

    /// <summary>An attempt failed — nobody home, refused, address unreachable. An NDR in the trade.</summary>
    DeliveryFailed = 65,

    /// <summary>Attempts are exhausted and the parcel is on its way back to the seller.</summary>
    RtoInitiated = 70,

    /// <summary>The returned-to-origin parcel reached the seller. The units go back on sale.</summary>
    RtoDelivered = 75,

    /// <summary>Delivered. The return window starts running from here.</summary>
    Delivered = 80,

    /// <summary>The shopper has asked to send it back.</summary>
    ReturnRequested = 85,

    /// <summary>The return has been approved and the parcel is moving.</summary>
    ReturnInProgress = 90,

    /// <summary>The return is complete. What happens to the money is the Returns module's.</summary>
    Returned = 95,

    /// <summary>Delivered, and the return window closed. Settlement becomes payable here.</summary>
    Completed = 100,

    /// <summary>Cancelled, in whole. A partial cancellation leaves the sub-order in its own state.</summary>
    Cancelled = 110,
}

/// <summary>
/// Who is asking for a transition. The permission half of the transition table.
/// </summary>
/// <remarks>
/// Flags rather than a single value, because most edges are open to more than one actor and the
/// table reads far better as <c>Vendor | Platform</c> than as three rows saying the same thing.
/// </remarks>
[Flags]
internal enum OrderActor
{
    /// <summary>Nobody. Used only as the empty answer for an edge that does not exist.</summary>
    None = 0,

    /// <summary>The shopper who placed the order, acting on the storefront.</summary>
    Customer = 1,

    /// <summary>The seller fulfilling this sub-order, acting in the vendor portal.</summary>
    Vendor = 2,

    /// <summary>Platform staff, acting in the admin with the transition permission.</summary>
    Platform = 4,

    /// <summary>
    /// The platform itself: a payment webhook, a courier tracking update, the completion sweeper.
    /// Never reachable from an HTTP endpoint — no caller can claim to be it.
    /// </summary>
    System = 8,
}

/// <summary>
/// The transition table, and the whole of it.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in this module moves a sub-order by ad-hoc code (docs/02-domain-model.md §5.1). One
/// table declares every edge and who may take it, so the machine can be read — and, at Step 29,
/// exhaustively tested — in one place rather than reconstructed from twenty handlers.
/// </para>
/// <para>
/// Three edges are worth stating out loud. <see cref="SubOrderStatus.PaymentFailed"/> goes back to
/// <see cref="SubOrderStatus.PendingPayment"/>, because a declined card is a retry rather than a
/// dead order. The customer may cancel up to and including <see cref="SubOrderStatus.Packed"/> and
/// no further, which is the rule in docs/02-domain-model.md §5.1 — after that the parcel is with a
/// courier and only Operations can stop it, with a reason. And
/// <see cref="SubOrderStatus.DeliveryFailed"/> returns to
/// <see cref="SubOrderStatus.OutForDelivery"/> as often as the courier will re-attempt, which is
/// why the NDR state is a loop and not a terminus.
/// </para>
/// </remarks>
internal static class SubOrderLifecycle
{
    /// <summary>The states from which nothing further can happen.</summary>
    public static readonly IReadOnlyList<SubOrderStatus> Terminal =
    [
        SubOrderStatus.Completed,
        SubOrderStatus.Cancelled,
        SubOrderStatus.Returned,
        SubOrderStatus.RtoDelivered,
    ];

    /// <summary>
    /// Who may move a sub-order from <paramref name="from"/> to <paramref name="to"/>, or
    /// <see cref="OrderActor.None"/> when the machine has no such edge.
    /// </summary>
    /// <param name="from">The current status.</param>
    /// <param name="to">The status being moved to.</param>
    public static OrderActor AllowedActors(SubOrderStatus from, SubOrderStatus to)
        => (from, to) switch
        {
            // Money in, or money refused. Both are the gateway's word, relayed by Payments; an
            // operator can force either when a webhook was lost and the money is demonstrably there.
            (SubOrderStatus.PendingPayment, SubOrderStatus.Confirmed) => OrderActor.System | OrderActor.Platform,
            (SubOrderStatus.PendingPayment, SubOrderStatus.PaymentFailed) => OrderActor.System | OrderActor.Platform,
            (SubOrderStatus.PaymentFailed, SubOrderStatus.PendingPayment) =>
                OrderActor.Customer | OrderActor.System | OrderActor.Platform,

            // Nothing has been committed yet, so anybody with an interest may walk away.
            (SubOrderStatus.PendingPayment, SubOrderStatus.Cancelled) =>
                OrderActor.Customer | OrderActor.Platform | OrderActor.System,
            (SubOrderStatus.PaymentFailed, SubOrderStatus.Cancelled) =>
                OrderActor.Customer | OrderActor.Platform | OrderActor.System,

            // Fulfilment. The seller drives it; Operations can drive it for them, which is what a
            // support call about a seller who has gone quiet actually needs.
            (SubOrderStatus.Confirmed, SubOrderStatus.Processing) => OrderActor.Vendor | OrderActor.Platform,
            (SubOrderStatus.Processing, SubOrderStatus.Packed) => OrderActor.Vendor | OrderActor.Platform,
            (SubOrderStatus.Packed, SubOrderStatus.Shipped) =>
                OrderActor.Vendor | OrderActor.Platform | OrderActor.System,

            // Cancellation before dispatch. The customer's right ends at Packed.
            (SubOrderStatus.Confirmed, SubOrderStatus.Cancelled) =>
                OrderActor.Customer | OrderActor.Vendor | OrderActor.Platform,
            (SubOrderStatus.Processing, SubOrderStatus.Cancelled) =>
                OrderActor.Customer | OrderActor.Vendor | OrderActor.Platform,
            (SubOrderStatus.Packed, SubOrderStatus.Cancelled) =>
                OrderActor.Customer | OrderActor.Vendor | OrderActor.Platform,

            // After dispatch only Operations may stop it, and only with a reason: the parcel is with
            // a third party and somebody has to answer for recalling it.
            (SubOrderStatus.Shipped, SubOrderStatus.Cancelled) => OrderActor.Platform,

            // In transit. The courier's word, relayed by Shipping from Step 16.
            (SubOrderStatus.Shipped, SubOrderStatus.OutForDelivery) => OrderActor.System | OrderActor.Platform,
            (SubOrderStatus.OutForDelivery, SubOrderStatus.Delivered) => OrderActor.System | OrderActor.Platform,
            (SubOrderStatus.OutForDelivery, SubOrderStatus.DeliveryFailed) => OrderActor.System | OrderActor.Platform,
            (SubOrderStatus.DeliveryFailed, SubOrderStatus.OutForDelivery) => OrderActor.System | OrderActor.Platform,
            (SubOrderStatus.DeliveryFailed, SubOrderStatus.RtoInitiated) => OrderActor.System | OrderActor.Platform,
            (SubOrderStatus.RtoInitiated, SubOrderStatus.RtoDelivered) => OrderActor.System | OrderActor.Platform,

            // A courier that delivers a parcel it had written off is commoner than it should be, and
            // refusing the correction would leave the shopper holding goods the platform says are
            // coming back.
            (SubOrderStatus.RtoInitiated, SubOrderStatus.Delivered) => OrderActor.System | OrderActor.Platform,

            // After delivery. Completion is the sweeper's; the return branch is the shopper's.
            (SubOrderStatus.Delivered, SubOrderStatus.Completed) => OrderActor.System | OrderActor.Platform,
            (SubOrderStatus.Delivered, SubOrderStatus.ReturnRequested) => OrderActor.Customer | OrderActor.Platform,
            (SubOrderStatus.ReturnRequested, SubOrderStatus.ReturnInProgress) =>
                OrderActor.Platform | OrderActor.Vendor | OrderActor.System,

            // A refused return puts the sub-order back where it was, so the window can still close
            // on it normally.
            (SubOrderStatus.ReturnRequested, SubOrderStatus.Delivered) => OrderActor.Platform | OrderActor.Vendor,
            (SubOrderStatus.ReturnInProgress, SubOrderStatus.Returned) => OrderActor.Platform | OrderActor.System,
            (SubOrderStatus.ReturnInProgress, SubOrderStatus.Delivered) => OrderActor.Platform,

            _ => OrderActor.None,
        };

    /// <summary>Whether the machine has an edge at all, regardless of who is asking.</summary>
    /// <param name="from">The current status.</param>
    /// <param name="to">The status being moved to.</param>
    public static bool IsTransitionAllowed(SubOrderStatus from, SubOrderStatus to)
        => AllowedActors(from, to) != OrderActor.None;

    /// <summary>Whether <paramref name="actor"/> may take the edge.</summary>
    /// <param name="from">The current status.</param>
    /// <param name="to">The status being moved to.</param>
    /// <param name="actor">Who is asking.</param>
    public static bool IsAllowedFor(SubOrderStatus from, SubOrderStatus to, OrderActor actor)
        => (AllowedActors(from, to) & actor) != 0;

    /// <summary>Every status reachable from one, for an admin UI that offers the next steps.</summary>
    /// <param name="from">The current status.</param>
    /// <param name="actor">Who is asking. Only edges open to them are returned.</param>
    public static IReadOnlyList<SubOrderStatus> NextFrom(SubOrderStatus from, OrderActor actor)
        => [.. Enum.GetValues<SubOrderStatus>().Where(to => IsAllowedFor(from, to, actor))];

    /// <summary>Whether nothing further can happen to a sub-order in this state.</summary>
    /// <param name="status">The status.</param>
    public static bool IsTerminal(SubOrderStatus status) => Terminal.Contains(status);

    /// <summary>
    /// Whether the stock behind a sub-order in this state has been committed out of supply.
    /// </summary>
    /// <remarks>
    /// The one question a cancellation has to answer before it decides what to do about stock: a
    /// sub-order cancelled before confirmation still holds a <em>reservation</em>, which is released;
    /// one cancelled after has had units taken out of stock, and only a restock puts them back
    /// (docs/02-domain-model.md §5.1).
    /// </remarks>
    /// <param name="status">The status.</param>
    public static bool HasCommittedStock(SubOrderStatus status)
        => status is not (SubOrderStatus.PendingPayment or SubOrderStatus.PaymentFailed);

    /// <summary>Whether a shopper may still cancel of their own accord from this state.</summary>
    /// <param name="status">The status.</param>
    public static bool IsCustomerCancellable(SubOrderStatus status)
        => IsAllowedFor(status, SubOrderStatus.Cancelled, OrderActor.Customer);
}

/// <summary>What an order as a whole looks like to the shopper (docs/02-domain-model.md §5.2).</summary>
internal enum OrderStatus
{
    /// <summary>At least one seller's part is still waiting for money.</summary>
    PendingPayment = 0,

    /// <summary>Somewhere between confirmed and delivered, per seller.</summary>
    InProgress = 10,

    /// <summary>Every seller's part reached a successful end.</summary>
    Completed = 20,

    /// <summary>Every seller's part was cancelled.</summary>
    Cancelled = 30,
}

/// <summary>
/// Derives the order's status from its sub-orders' (docs/02-domain-model.md §5.2).
/// </summary>
/// <remarks>
/// <para>
/// The parent status is computed and never set, which is what keeps a two-vendor order honest: one
/// seller shipping and the other refunding is not a state a single stored column could describe, and
/// the alternative — writing the order's status at every sub-order transition — is a denormalisation
/// that goes wrong the first time two sellers move at once.
/// </para>
/// <para>
/// The rule is docs/02-domain-model.md §5.2 verbatim, with one clarification the document leaves
/// implicit: a fully cancelled order and a fully completed one are both terminal, and an order whose
/// parts split between the two counts as completed. The shopper received something, and calling that
/// order "cancelled" would hide it from their own order history.
/// </para>
/// </remarks>
internal static class OrderStatusRules
{
    /// <summary>The successful ends of the sub-order machine.</summary>
    private static readonly SubOrderStatus[] SuccessfulEnds =
    [
        SubOrderStatus.Completed,
        SubOrderStatus.Returned,
        SubOrderStatus.RtoDelivered,
    ];

    /// <summary>Computes an order's status from the states of its parts.</summary>
    /// <param name="statuses">Every sub-order's status. Must not be empty.</param>
    public static OrderStatus Derive(IReadOnlyCollection<SubOrderStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);

        if (statuses.Count == 0)
        {
            // An order with no sub-orders violates the aggregate's invariant and cannot be created
            // through the domain. Answering PendingPayment rather than throwing keeps a read of a
            // corrupt row renderable, which is what a support screen needs it to be.
            return OrderStatus.PendingPayment;
        }

        if (statuses.All(status => status == SubOrderStatus.Cancelled))
        {
            return OrderStatus.Cancelled;
        }

        if (statuses.All(status => status == SubOrderStatus.Cancelled || SuccessfulEnds.Contains(status)))
        {
            return OrderStatus.Completed;
        }

        return statuses.Any(status => status is SubOrderStatus.PendingPayment or SubOrderStatus.PaymentFailed)
            ? OrderStatus.PendingPayment
            : OrderStatus.InProgress;
    }
}

/// <summary>Where the money for an order stands, mirrored from Payments and eventually consistent.</summary>
/// <remarks>
/// A mirror rather than a source: Payments owns what was captured and refunded, and this column
/// exists so an order list can be filtered on it without a join across a schema boundary. When the
/// two disagree, Payments is right.
/// </remarks>
internal enum OrderPaymentStatus
{
    /// <summary>Nothing has been collected. Where a prepaid order and every COD order start.</summary>
    Pending = 0,

    /// <summary>Money is authorised but not yet captured.</summary>
    Authorized = 1,

    /// <summary>Collected in full.</summary>
    Paid = 2,

    /// <summary>The gateway refused, or the shopper never completed the payment.</summary>
    Failed = 3,

    /// <summary>Some of it has gone back.</summary>
    PartiallyRefunded = 4,

    /// <summary>All of it has gone back.</summary>
    Refunded = 5,
}

/// <summary>How an order is being paid for.</summary>
internal enum OrderPaymentMethod
{
    /// <summary>Paid before dispatch, through the gateway.</summary>
    Prepaid = 0,

    /// <summary>Cash collected at the door.</summary>
    CashOnDelivery = 1,
}
