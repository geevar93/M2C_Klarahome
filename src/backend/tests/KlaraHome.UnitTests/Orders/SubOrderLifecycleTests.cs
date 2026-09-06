using KlaraHome.Modules.Orders.Domain;

namespace KlaraHome.UnitTests.Orders;

/// <summary>
/// The sub-order transition table and the order-status derivation
/// (docs/02-domain-model.md §5.1 and §5.2).
/// </summary>
/// <remarks>
/// <para>
/// Tested while writing them under the build sprint's rule 1, which names a state machine's
/// transition table explicitly. It is the cheapest thing in this module to assert and the most
/// expensive to get wrong: every other module reacts to what these edges allow, and an edge that
/// silently exists is a parcel that can be dispatched before it was paid for.
/// </para>
/// <para>
/// The cases are tables rather than <c>[Theory]</c> rows, for the same reason the vendor lifecycle
/// tests use them: an internal enum cannot appear in a public method signature across the
/// <c>InternalsVisibleTo</c> boundary.
/// </para>
/// </remarks>
public sealed class SubOrderLifecycleTests
{
    [Fact]
    public void The_machine_has_every_edge_the_domain_model_draws()
    {
        (SubOrderStatus From, SubOrderStatus To)[] edges =
        [
            (SubOrderStatus.PendingPayment, SubOrderStatus.Confirmed),
            (SubOrderStatus.PendingPayment, SubOrderStatus.PaymentFailed),
            (SubOrderStatus.PaymentFailed, SubOrderStatus.PendingPayment),
            (SubOrderStatus.PaymentFailed, SubOrderStatus.Cancelled),
            (SubOrderStatus.Confirmed, SubOrderStatus.Processing),
            (SubOrderStatus.Confirmed, SubOrderStatus.Cancelled),
            (SubOrderStatus.Processing, SubOrderStatus.Packed),
            (SubOrderStatus.Processing, SubOrderStatus.Cancelled),
            (SubOrderStatus.Packed, SubOrderStatus.Shipped),
            (SubOrderStatus.Shipped, SubOrderStatus.OutForDelivery),
            (SubOrderStatus.OutForDelivery, SubOrderStatus.Delivered),
            (SubOrderStatus.OutForDelivery, SubOrderStatus.DeliveryFailed),
            (SubOrderStatus.DeliveryFailed, SubOrderStatus.OutForDelivery),
            (SubOrderStatus.DeliveryFailed, SubOrderStatus.RtoInitiated),
            (SubOrderStatus.RtoInitiated, SubOrderStatus.RtoDelivered),
            (SubOrderStatus.Delivered, SubOrderStatus.Completed),
            (SubOrderStatus.Delivered, SubOrderStatus.ReturnRequested),
            (SubOrderStatus.ReturnRequested, SubOrderStatus.ReturnInProgress),
            (SubOrderStatus.ReturnInProgress, SubOrderStatus.Returned),
        ];

        foreach (var (from, to) in edges)
        {
            Assert.True(
                SubOrderLifecycle.IsTransitionAllowed(from, to),
                $"The machine should allow {from} to {to}.");
        }
    }

    [Fact]
    public void The_machine_has_no_edge_the_domain_model_does_not_draw()
    {
        (SubOrderStatus From, SubOrderStatus To)[] absent =
        [
            (SubOrderStatus.PendingPayment, SubOrderStatus.Shipped),
            (SubOrderStatus.PendingPayment, SubOrderStatus.Delivered),
            (SubOrderStatus.PendingPayment, SubOrderStatus.Processing),
            (SubOrderStatus.Confirmed, SubOrderStatus.Delivered),
            (SubOrderStatus.Confirmed, SubOrderStatus.Shipped),
            (SubOrderStatus.Packed, SubOrderStatus.Delivered),
            (SubOrderStatus.Delivered, SubOrderStatus.Shipped),
            (SubOrderStatus.Delivered, SubOrderStatus.Cancelled),
            (SubOrderStatus.Completed, SubOrderStatus.Cancelled),
            (SubOrderStatus.Cancelled, SubOrderStatus.Confirmed),
            (SubOrderStatus.Returned, SubOrderStatus.Delivered),
        ];

        foreach (var (from, to) in absent)
        {
            Assert.False(
                SubOrderLifecycle.IsTransitionAllowed(from, to),
                $"The machine should not allow {from} to {to}.");
        }
    }

    [Fact]
    public void No_state_transitions_to_itself()
    {
        foreach (var status in Enum.GetValues<SubOrderStatus>())
        {
            Assert.False(SubOrderLifecycle.IsTransitionAllowed(status, status));
        }
    }

    [Fact]
    public void Nothing_leaves_a_terminal_state()
    {
        foreach (var terminal in SubOrderLifecycle.Terminal)
        {
            foreach (var to in Enum.GetValues<SubOrderStatus>())
            {
                Assert.False(
                    SubOrderLifecycle.IsTransitionAllowed(terminal, to),
                    $"{terminal} is terminal and should not reach {to}.");
            }
        }
    }

    /// <summary>
    /// docs/02-domain-model.md §5.1: "Cancellation is allowed up to <c>Packed</c> for the customer;
    /// Operations may cancel later with a reason."
    /// </summary>
    [Fact]
    public void The_customer_may_cancel_up_to_packed_and_no_further()
    {
        (SubOrderStatus From, bool Allowed)[] cases =
        [
            (SubOrderStatus.PendingPayment, true),
            (SubOrderStatus.PaymentFailed, true),
            (SubOrderStatus.Confirmed, true),
            (SubOrderStatus.Processing, true),
            (SubOrderStatus.Packed, true),
            (SubOrderStatus.Shipped, false),
            (SubOrderStatus.OutForDelivery, false),
            (SubOrderStatus.Delivered, false),
        ];

        foreach (var (from, allowed) in cases)
        {
            Assert.Equal(allowed, SubOrderLifecycle.IsCustomerCancellable(from));
        }
    }

    [Fact]
    public void Only_operations_may_cancel_a_dispatched_parcel()
    {
        Assert.True(SubOrderLifecycle.IsAllowedFor(
            SubOrderStatus.Shipped,
            SubOrderStatus.Cancelled,
            OrderActor.Platform));

        Assert.False(SubOrderLifecycle.IsAllowedFor(
            SubOrderStatus.Shipped,
            SubOrderStatus.Cancelled,
            OrderActor.Vendor));

        Assert.False(SubOrderLifecycle.IsAllowedFor(
            SubOrderStatus.Shipped,
            SubOrderStatus.Cancelled,
            OrderActor.Customer));
    }

    /// <summary>
    /// A seller must never be able to declare their own parcel delivered, or their own order paid
    /// for. Both are somebody else's word — the courier's and the gateway's — relayed by the
    /// platform.
    /// </summary>
    [Fact]
    public void A_seller_cannot_take_the_edges_that_belong_to_the_platform()
    {
        (SubOrderStatus From, SubOrderStatus To)[] platformOnly =
        [
            (SubOrderStatus.OutForDelivery, SubOrderStatus.Delivered),
            (SubOrderStatus.PendingPayment, SubOrderStatus.Confirmed),
            (SubOrderStatus.Delivered, SubOrderStatus.Completed),
        ];

        foreach (var (from, to) in platformOnly)
        {
            Assert.True(SubOrderLifecycle.IsTransitionAllowed(from, to));
            Assert.False(
                SubOrderLifecycle.IsAllowedFor(from, to, OrderActor.Vendor),
                $"A seller must not be able to move a sub-order from {from} to {to}.");
        }
    }

    /// <summary>
    /// A shopper must never be able to move their own order through fulfilment. Everything they may
    /// do is cancel it, retry a payment, or ask to return what arrived.
    /// </summary>
    [Fact]
    public void A_customer_can_only_cancel_retry_or_return()
    {
        foreach (var from in Enum.GetValues<SubOrderStatus>())
        {
            foreach (var to in SubOrderLifecycle.NextFrom(from, OrderActor.Customer))
            {
                Assert.True(
                    to is SubOrderStatus.Cancelled
                        or SubOrderStatus.PendingPayment
                        or SubOrderStatus.ReturnRequested,
                    $"A customer must not be able to move a sub-order from {from} to {to}.");
            }
        }
    }

    [Fact]
    public void Stock_is_committed_from_confirmation_onwards_and_not_before()
    {
        Assert.False(SubOrderLifecycle.HasCommittedStock(SubOrderStatus.PendingPayment));
        Assert.False(SubOrderLifecycle.HasCommittedStock(SubOrderStatus.PaymentFailed));

        Assert.True(SubOrderLifecycle.HasCommittedStock(SubOrderStatus.Confirmed));
        Assert.True(SubOrderLifecycle.HasCommittedStock(SubOrderStatus.Packed));
        Assert.True(SubOrderLifecycle.HasCommittedStock(SubOrderStatus.Delivered));
    }
}

/// <summary>The order's status is computed from its parts, never set (docs/02-domain-model.md §5.2).</summary>
public sealed class OrderStatusDerivationTests
{
    [Fact]
    public void Every_part_cancelled_makes_the_order_cancelled()
        => Assert.Equal(
            OrderStatus.Cancelled,
            OrderStatusRules.Derive([SubOrderStatus.Cancelled, SubOrderStatus.Cancelled]));

    [Fact]
    public void Every_part_finished_makes_the_order_completed()
        => Assert.Equal(
            OrderStatus.Completed,
            OrderStatusRules.Derive([SubOrderStatus.Completed, SubOrderStatus.Returned]));

    /// <summary>
    /// One seller cancelled and the other delivered is a completed order, not a cancelled one: the
    /// shopper received something, and filing it under "cancelled" would hide it from their history.
    /// </summary>
    [Fact]
    public void A_part_cancelled_alongside_a_part_completed_still_completes()
        => Assert.Equal(
            OrderStatus.Completed,
            OrderStatusRules.Derive([SubOrderStatus.Cancelled, SubOrderStatus.Completed]));

    [Fact]
    public void Any_part_awaiting_payment_holds_the_whole_order_there()
        => Assert.Equal(
            OrderStatus.PendingPayment,
            OrderStatusRules.Derive([SubOrderStatus.Shipped, SubOrderStatus.PendingPayment]));

    [Fact]
    public void A_failed_payment_also_holds_the_order_at_pending()
        => Assert.Equal(
            OrderStatus.PendingPayment,
            OrderStatusRules.Derive([SubOrderStatus.PaymentFailed]));

    [Fact]
    public void Anything_else_is_in_progress()
        => Assert.Equal(
            OrderStatus.InProgress,
            OrderStatusRules.Derive([SubOrderStatus.Confirmed, SubOrderStatus.Delivered]));
}
