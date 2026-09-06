namespace KlaraHome.Modules.Payments.Domain;

/// <summary>
/// Where one collection stands (docs/03-database-design.md §4.9).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the gateway's vocabulary rather than the order's. An order is <c>Confirmed</c>; a
/// payment is <c>Captured</c>, and the two are different facts that happen at different instants —
/// collapsing them is how a platform ends up unable to say whether it holds a customer's money.
/// </para>
/// <para>
/// The numbers are stable and ordered by how far through collection the state is, so "has this been
/// paid" is one comparison. They are persisted as strings, so a gap or a reordering is a display
/// change and never a data migration.
/// </para>
/// </remarks>
internal enum PaymentStatus
{
    /// <summary>Opened at the gateway and waiting for the shopper. Where every prepaid payment starts.</summary>
    Created = 0,

    /// <summary>The shopper's bank has held the money but it has not been taken.</summary>
    Authorized = 10,

    /// <summary>Taken. The one state that confirms an order.</summary>
    Captured = 20,

    /// <summary>Some of it has gone back.</summary>
    PartiallyRefunded = 30,

    /// <summary>All of it has gone back.</summary>
    Refunded = 40,

    /// <summary>The gateway refused, or the shopper never completed it. Retryable.</summary>
    Failed = 50,

    /// <summary>Abandoned before it was ever paid — the order timed out, or was cancelled.</summary>
    Cancelled = 60,
}

/// <summary>The rail a payment was actually taken on.</summary>
/// <remarks>
/// Written only from what the gateway reports. A shopper chooses <em>prepaid</em> at checkout;
/// which rail they then used is the gateway's fact, and <see cref="Unknown"/> is the honest answer
/// until it tells us.
/// </remarks>
internal enum PaymentMethod
{
    /// <summary>Not yet known. Every prepaid collection starts here.</summary>
    Unknown = 0,

    /// <summary>Unified Payments Interface — the majority rail in India.</summary>
    Upi = 1,

    /// <summary>Credit or debit card, collected entirely by the gateway.</summary>
    Card = 2,

    /// <summary>Net banking.</summary>
    NetBanking = 3,

    /// <summary>A prepaid wallet.</summary>
    Wallet = 4,

    /// <summary>Equated monthly instalments on a card or a lender's line.</summary>
    Emi = 5,

    /// <summary>Cash taken at the door. Never touches a gateway.</summary>
    Cod = 6,
}

/// <summary>Which route learned a fact about a payment.</summary>
/// <remarks>
/// Recorded on every attempt because it is what makes a lost webhook visible: an attempt whose
/// source is <see cref="Reconciliation"/> is one the gateway never delivered to us.
/// </remarks>
internal enum PaymentAttemptSource
{
    /// <summary>The browser's callback after the checkout widget closed. A UX signal only.</summary>
    Checkout = 0,

    /// <summary>A signed webhook from the gateway. The source of truth, after an API re-fetch.</summary>
    Webhook = 1,

    /// <summary>The reconciliation sweep, which re-fetches a payment the webhook never reported.</summary>
    Reconciliation = 2,

    /// <summary>An operator, repairing something by hand from the admin.</summary>
    Admin = 3,
}

/// <summary>Where a refund stands.</summary>
internal enum RefundStatus
{
    /// <summary>Raised, and waiting for a second signature because it is above the threshold.</summary>
    Requested = 0,

    /// <summary>Approved and about to be sent to the gateway.</summary>
    Approved = 10,

    /// <summary>The gateway has it and has not finished.</summary>
    Processing = 20,

    /// <summary>The gateway confirms the money has gone back. The only state that is money moved.</summary>
    Processed = 30,

    /// <summary>The gateway refused it.</summary>
    Failed = 40,

    /// <summary>A second signature was withheld. Nothing was sent.</summary>
    Rejected = 50,
}

/// <summary>How quickly a refund is sent, where the gateway offers a choice.</summary>
internal enum RefundSpeed
{
    /// <summary>The default. Settles on the gateway's normal cycle, at no extra fee.</summary>
    Normal = 0,

    /// <summary>Instant where the rail supports it, normal where it does not. Carries a fee.</summary>
    Optimum = 1,
}

/// <summary>Where a gateway event stands in this platform's processing of it.</summary>
internal enum GatewayEventStatus
{
    /// <summary>Stored, verified, and waiting for the processor.</summary>
    Pending = 0,

    /// <summary>Applied. A redelivery of it does nothing.</summary>
    Processed = 10,

    /// <summary>A type this platform does not subscribe to. Kept as evidence, never applied.</summary>
    Ignored = 20,

    /// <summary>Processing threw and there are attempts left.</summary>
    Failed = 30,

    /// <summary>Attempts are exhausted. It waits for a human, and is never dropped.</summary>
    DeadLettered = 40,
}

/// <summary>Where cash on delivery stands for one seller's parcel.</summary>
internal enum CodCollectionStatus
{
    /// <summary>The parcel is out and nobody has taken the money yet.</summary>
    Pending = 0,

    /// <summary>The courier took the cash at the door. It is theirs to hand over.</summary>
    Collected = 10,

    /// <summary>The courier has remitted it. The money is the platform's, and settleable.</summary>
    Remitted = 20,

    /// <summary>The parcel never got there — returned, refused, cancelled. Nothing is owed.</summary>
    Waived = 30,

    /// <summary>Collected and never remitted, past recovery. A loss, recorded rather than hidden.</summary>
    WrittenOff = 40,
}

/// <summary>Whether a settlement line matched what this platform recorded.</summary>
internal enum SettlementMatchStatus
{
    /// <summary>Not yet compared.</summary>
    Unmatched = 0,

    /// <summary>Found, and the amounts agree.</summary>
    Matched = 10,

    /// <summary>Found, and the amounts do not agree. Alerted, and deliberately not repaired.</summary>
    Mismatched = 20,
}

/// <summary>
/// The transition table for a collection, and the whole of it.
/// </summary>
/// <remarks>
/// <para>
/// Small, because a payment has far fewer states than an order — but written as a table for the
/// same reason. Every fact about a payment arrives from outside this system, at least once, in an
/// order nobody controls: a captured webhook can arrive before the authorised one, and a
/// reconciliation sweep can arrive at both facts independently. A machine that answers "is this move
/// legal" in one place is what stops a late authorisation event un-capturing a payment that has
/// already confirmed an order.
/// </para>
/// <para>
/// A transition to the state a payment is already in is allowed and does nothing. That is not
/// laxity — it is the idempotency the webhook contract requires, stated where it can be read.
/// </para>
/// </remarks>
internal static class PaymentLifecycle
{
    /// <summary>The states from which nothing further can happen.</summary>
    public static readonly IReadOnlyList<PaymentStatus> Terminal =
    [
        PaymentStatus.Refunded,
        PaymentStatus.Cancelled,
    ];

    /// <summary>Whether the machine has an edge from one state to another.</summary>
    /// <param name="from">The current status.</param>
    /// <param name="to">The status being moved to.</param>
    public static bool IsAllowed(PaymentStatus from, PaymentStatus to)
        => from == to
           || (from, to) switch
           {
               // The shopper is at the widget. Any of the three outcomes may arrive first.
               (PaymentStatus.Created, PaymentStatus.Authorized) => true,
               (PaymentStatus.Created, PaymentStatus.Captured) => true,
               (PaymentStatus.Created, PaymentStatus.Failed) => true,
               (PaymentStatus.Created, PaymentStatus.Cancelled) => true,

               // Held, then taken — or released, or the hold expired.
               (PaymentStatus.Authorized, PaymentStatus.Captured) => true,
               (PaymentStatus.Authorized, PaymentStatus.Failed) => true,
               (PaymentStatus.Authorized, PaymentStatus.Cancelled) => true,

               // Money in. From here it can only go back out.
               (PaymentStatus.Captured, PaymentStatus.PartiallyRefunded) => true,
               (PaymentStatus.Captured, PaymentStatus.Refunded) => true,
               (PaymentStatus.PartiallyRefunded, PaymentStatus.Refunded) => true,

               // A declined attempt is a retry, not a dead payment: the shopper goes back to the
               // widget on the same collection and the same gateway order.
               (PaymentStatus.Failed, PaymentStatus.Created) => true,
               (PaymentStatus.Failed, PaymentStatus.Authorized) => true,
               (PaymentStatus.Failed, PaymentStatus.Captured) => true,
               (PaymentStatus.Failed, PaymentStatus.Cancelled) => true,

               _ => false,
           };

    /// <summary>Whether money has actually been taken in this state.</summary>
    /// <param name="status">The status.</param>
    public static bool IsSettled(PaymentStatus status)
        => status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded;

    /// <summary>Whether a shopper may still be sent back to the widget for this collection.</summary>
    /// <param name="status">The status.</param>
    public static bool IsRetryable(PaymentStatus status)
        => status is PaymentStatus.Created or PaymentStatus.Failed;

    /// <summary>Whether nothing further can happen.</summary>
    /// <param name="status">The status.</param>
    public static bool IsTerminal(PaymentStatus status) => Terminal.Contains(status);

    /// <summary>
    /// What a collection's status becomes once <paramref name="refunded"/> has gone back out of
    /// <paramref name="captured"/>.
    /// </summary>
    /// <remarks>
    /// Derived rather than set, so a refund handler and a webhook handler cannot reach two different
    /// answers about the same two numbers. The comparison is greater-or-equal rather than equal
    /// because a gateway that refunds a fee alongside the principal can return marginally more than
    /// was captured, and a payment that is over-refunded is fully refunded, not partially.
    /// </remarks>
    /// <param name="captured">Everything taken.</param>
    /// <param name="refunded">Everything given back.</param>
    /// <param name="current">Where the payment is now, returned unchanged when nothing was captured.</param>
    public static PaymentStatus AfterRefund(decimal captured, decimal refunded, PaymentStatus current)
    {
        if (captured <= 0m || refunded <= 0m)
        {
            return current;
        }

        return refunded >= captured ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
    }
}
