using KlaraHome.Modules.Payments.Domain;

namespace KlaraHome.UnitTests.Payments;

/// <summary>
/// The rules that decide what a payment does when facts about it arrive twice, late, or in the
/// wrong order.
/// </summary>
/// <remarks>
/// Tested while writing them under the build sprint's rule 1. Every fact about a payment comes from
/// outside this system, at least once, in an order nobody controls — and the failure modes are all
/// silent: a stale <c>failed</c> event un-paying a paid order, a redelivered capture adding up, or a
/// refund total that walks past what was captured. None of those throws; they just leave the wrong
/// number in a column somebody eventually reconciles against a bank statement.
/// </remarks>
public sealed class PaymentLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_transition_to_the_state_it_is_already_in_is_allowed()
    {
        // The idempotency the webhook contract requires, stated in the machine rather than assumed
        // by every caller.
        foreach (var status in Enum.GetValues<PaymentStatus>())
        {
            Assert.True(PaymentLifecycle.IsAllowed(status, status));
        }
    }

    /// <summary>The edges a gateway actually produces, and the ones it never can.</summary>
    /// <remarks>
    /// Written as one fact rather than a theory per pair because the enum is internal to the module
    /// and an <c>InlineData</c> of it would force the test class open. The pairs are the point, not
    /// the reporting granularity.
    /// </remarks>
    [Fact]
    public void The_machine_has_the_edges_a_gateway_actually_produces()
    {
        AssertEdge(PaymentStatus.Created, PaymentStatus.Authorized, allowed: true);
        AssertEdge(PaymentStatus.Created, PaymentStatus.Captured, allowed: true);
        AssertEdge(PaymentStatus.Created, PaymentStatus.Failed, allowed: true);
        AssertEdge(PaymentStatus.Authorized, PaymentStatus.Captured, allowed: true);
        AssertEdge(PaymentStatus.Captured, PaymentStatus.PartiallyRefunded, allowed: true);
        AssertEdge(PaymentStatus.Captured, PaymentStatus.Refunded, allowed: true);

        // A declined attempt is a retry, not a dead payment.
        AssertEdge(PaymentStatus.Failed, PaymentStatus.Created, allowed: true);
        AssertEdge(PaymentStatus.Failed, PaymentStatus.Captured, allowed: true);
    }

    /// <summary>Once money is in, the machine has no edge back out of it except a refund.</summary>
    [Fact]
    public void The_machine_has_no_edge_back_out_of_money()
    {
        AssertEdge(PaymentStatus.Captured, PaymentStatus.Failed, allowed: false);
        AssertEdge(PaymentStatus.Captured, PaymentStatus.Created, allowed: false);
        AssertEdge(PaymentStatus.Refunded, PaymentStatus.Captured, allowed: false);
        AssertEdge(PaymentStatus.Cancelled, PaymentStatus.Captured, allowed: false);
        AssertEdge(PaymentStatus.PartiallyRefunded, PaymentStatus.Captured, allowed: false);
    }

    [Fact]
    public void Refunding_less_than_was_captured_leaves_it_partially_refunded()
        => Assert.Equal(
            PaymentStatus.PartiallyRefunded,
            PaymentLifecycle.AfterRefund(captured: 1000m, refunded: 400m, PaymentStatus.Captured));

    [Fact]
    public void Refunding_everything_leaves_it_refunded()
        => Assert.Equal(
            PaymentStatus.Refunded,
            PaymentLifecycle.AfterRefund(captured: 1000m, refunded: 1000m, PaymentStatus.Captured));

    /// <summary>
    /// Over-refunded is fully refunded, not partially.
    /// </summary>
    /// <remarks>
    /// A gateway that refunds its own fee alongside the principal returns marginally more than was
    /// captured. An equality comparison would leave that payment <c>PartiallyRefunded</c> for ever,
    /// and it would sit on a reconciliation report as an open item nobody could close.
    /// </remarks>
    [Fact]
    public void Refunding_more_than_was_captured_is_still_fully_refunded()
        => Assert.Equal(
            PaymentStatus.Refunded,
            PaymentLifecycle.AfterRefund(captured: 1000m, refunded: 1002.50m, PaymentStatus.Captured));

    [Fact]
    public void Nothing_captured_leaves_the_status_alone()
        => Assert.Equal(
            PaymentStatus.Created,
            PaymentLifecycle.AfterRefund(captured: 0m, refunded: 0m, PaymentStatus.Created));

    [Fact]
    public void Capturing_records_the_reference_the_rail_and_the_amount()
    {
        var payment = Open(amount: 2499m);

        Assert.True(payment.Capture("pay_1", PaymentMethod.Upi, 2499m, Now));

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal("pay_1", payment.ProviderPaymentId);
        Assert.Equal(PaymentMethod.Upi, payment.Method);
        Assert.Equal(2499m, payment.AmountCaptured);
        Assert.Equal(Now, payment.CapturedAt);
    }

    /// <summary>
    /// A redelivered capture leaves one captured payment, not two amounts added together.
    /// </summary>
    [Fact]
    public void Capturing_twice_does_not_double_the_captured_amount()
    {
        var payment = Open(amount: 2499m);

        Assert.True(payment.Capture("pay_1", PaymentMethod.Upi, 2499m, Now));
        Assert.False(payment.Capture("pay_1", PaymentMethod.Upi, 2499m, Now.AddMinutes(1)));

        Assert.Equal(2499m, payment.AmountCaptured);
        Assert.Equal(Now, payment.CapturedAt);
    }

    /// <summary>
    /// A late <c>failed</c> event about an earlier attempt must not un-pay a paid order.
    /// </summary>
    [Fact]
    public void A_failure_arriving_after_a_capture_is_ignored()
    {
        var payment = Open(amount: 500m);
        payment.Capture("pay_1", PaymentMethod.Card, 500m, Now);

        Assert.False(payment.Fail("BAD_REQUEST_ERROR", "Declined.", Now.AddMinutes(2)));

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Null(payment.FailureCode);
    }

    /// <summary>
    /// An authorisation arriving after a capture still teaches the payment its rail and reference.
    /// </summary>
    [Fact]
    public void An_authorisation_arriving_after_a_capture_does_not_move_it_back()
    {
        var payment = Open(amount: 500m);
        payment.Capture("pay_1", PaymentMethod.Unknown, 500m, Now);

        Assert.False(payment.Authorize("pay_1", PaymentMethod.NetBanking, Now.AddMinutes(-1)));

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(PaymentMethod.NetBanking, payment.Method);
    }

    /// <summary>
    /// A second, different payment id is not written over the first.
    /// </summary>
    /// <remarks>
    /// Two payment ids against one collection means the shopper paid twice. Overwriting the
    /// reference would destroy the evidence of it; the attempts table keeps both.
    /// </remarks>
    [Fact]
    public void The_gateway_reference_is_written_once()
    {
        var payment = Open(amount: 500m);
        payment.Capture("pay_1", PaymentMethod.Upi, 500m, Now);
        payment.Capture("pay_2", PaymentMethod.Upi, 500m, Now.AddMinutes(1));

        Assert.Equal("pay_1", payment.ProviderPaymentId);
    }

    [Fact]
    public void A_declined_payment_can_be_reopened_and_then_captured()
    {
        var payment = Open(amount: 500m);

        Assert.True(payment.Fail("GATEWAY_ERROR", "Declined by the bank.", Now));
        Assert.Equal(PaymentStatus.Failed, payment.Status);

        Assert.True(payment.Reopen());
        Assert.Equal(PaymentStatus.Created, payment.Status);
        Assert.Null(payment.FailureReason);

        Assert.True(payment.Capture("pay_2", PaymentMethod.Card, 500m, Now.AddMinutes(5)));
        Assert.Equal(PaymentStatus.Captured, payment.Status);
    }

    /// <summary>
    /// The refunded total is summed from the refunds, never incremented.
    /// </summary>
    /// <remarks>
    /// An increment applied twice by a redelivered <c>refund.processed</c> would show the shopper's
    /// money going back twice on paper. Summing gives the same answer however many times it is
    /// called, which is the only shape that survives at-least-once delivery.
    /// </remarks>
    [Fact]
    public void The_refunded_total_is_summed_rather_than_incremented()
    {
        var payment = Open(amount: 1000m);
        payment.Capture("pay_1", PaymentMethod.Upi, 1000m, Now);

        var first = AddRefund(payment, 300m, "first");
        first.MarkProcessed("rfnd_1", Now);

        payment.RederiveRefunds();
        payment.RederiveRefunds();

        Assert.Equal(300m, payment.AmountRefunded);
        Assert.Equal(700m, payment.AmountRefundable);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
    }

    [Fact]
    public void Only_completed_refunds_count_towards_the_total()
    {
        var payment = Open(amount: 1000m);
        payment.Capture("pay_1", PaymentMethod.Upi, 1000m, Now);

        AddRefund(payment, 300m, "pending");
        var done = AddRefund(payment, 200m, "done");
        done.MarkProcessed("rfnd_2", Now);

        payment.RederiveRefunds();

        Assert.Equal(200m, payment.AmountRefunded);
    }

    /// <summary>
    /// A refund a gateway has already confirmed cannot be reversed by a stale failure event.
    /// </summary>
    [Fact]
    public void A_processed_refund_is_not_failed_by_a_later_event()
    {
        var payment = Open(amount: 1000m);
        payment.Capture("pay_1", PaymentMethod.Upi, 1000m, Now);

        var refund = AddRefund(payment, 1000m, "whole");
        refund.MarkProcessed("rfnd_1", Now);

        Assert.False(refund.MarkFailed("Refund failed."));
        Assert.Equal(RefundStatus.Processed, refund.Status);
    }

    /// <summary>
    /// Below the threshold a refund is approved on creation; above it, it waits.
    /// </summary>
    /// <remarks>
    /// The maker-checker rule of docs/07-security-compliance.md §4, and the boundary matters: "at or
    /// below" is approved, and an off-by-one here silently sends every refund at exactly the
    /// threshold without a second signature.
    /// </remarks>
    [Theory]
    [InlineData(4999, 5000, false, "Approved")]
    [InlineData(5000, 5000, false, "Approved")]
    [InlineData(5001, 5000, true, "Requested")]
    public void The_approval_threshold_decides_whether_a_refund_waits(
        decimal amount,
        decimal threshold,
        bool requiresApproval,
        string expected)
    {
        var refund = Refund.Raise(
            Guid.NewGuid(),
            Guid.NewGuid(),
            amount,
            "INR",
            "A cancellation.",
            $"key:{amount}",
            initiatedBy: Guid.NewGuid(),
            threshold,
            Now);

        Assert.Equal(requiresApproval, refund.RequiresApproval);
        Assert.Equal(expected, refund.Status.ToString());
    }

    /// <summary>
    /// A refund approved on creation records no approver, because nobody was needed.
    /// </summary>
    /// <remarks>
    /// Writing the initiator into <c>ApprovedBy</c> would make "who was the second person" answer
    /// with a name that never looked at it — and it would collide with the check constraint that
    /// refuses a self-approval.
    /// </remarks>
    [Fact]
    public void A_refund_under_the_threshold_names_no_approver()
    {
        var initiator = Guid.NewGuid();

        var refund = Refund.Raise(
            Guid.NewGuid(),
            Guid.NewGuid(),
            100m,
            "INR",
            "Small.",
            "key:small",
            initiator,
            approvalThreshold: 5000m,
            Now);

        Assert.Null(refund.ApprovedBy);
        Assert.Equal(Now, refund.ApprovedAt);
    }

    [Fact]
    public void A_refund_that_needs_approval_can_only_be_approved_once()
    {
        var refund = Refund.Raise(
            Guid.NewGuid(),
            Guid.NewGuid(),
            9000m,
            "INR",
            "Large.",
            "key:large",
            initiatedBy: Guid.NewGuid(),
            approvalThreshold: 5000m,
            Now);

        var approver = Guid.NewGuid();

        Assert.True(refund.Approve(approver, Now));
        Assert.False(refund.Approve(Guid.NewGuid(), Now));
        Assert.Equal(approver, refund.ApprovedBy);
    }

    private static void AssertEdge(PaymentStatus from, PaymentStatus to, bool allowed)
        => Assert.Equal(allowed, PaymentLifecycle.IsAllowed(from, to));

    private static Payment Open(decimal amount)
        => Payment.Open(
            Guid.NewGuid(),
            "KH-2609-000001",
            Guid.NewGuid(),
            "razorpay",
            amount,
            "INR",
            $"place:{Guid.NewGuid()}",
            Now);

    private static Refund AddRefund(Payment payment, decimal amount, string key)
    {
        var refund = Refund.Raise(
            payment.Id,
            payment.OrderId,
            amount,
            payment.CurrencyCode,
            "Because.",
            key,
            initiatedBy: null,
            approvalThreshold: decimal.MaxValue,
            Now);

        payment.Add(refund);

        return refund;
    }
}
