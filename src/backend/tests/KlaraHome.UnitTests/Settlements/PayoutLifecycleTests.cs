using KlaraHome.Modules.Settlements.Domain;

namespace KlaraHome.UnitTests.Settlements;

/// <summary>
/// The transition table a payout batch moves by, and the maker–checker control on top of it.
/// </summary>
/// <remarks>
/// Tested while writing them under the build sprint's rule 1. The table is data an admin screen draws
/// its buttons from, so an edge that exists for the wrong actor is a button somebody presses; and the
/// self-approval rule is the one control in this platform whose failure sends money to whoever
/// decided to send it. Both are pure and neither needs a database.
/// </remarks>
public sealed class PayoutLifecycleTests
{
    /// <summary>Only a checker signs a batch off, and a maker never does.</summary>
    /// <remarks>
    /// The permission half of the control. It is separate from the identity half tested below,
    /// because the two catch different mistakes: this catches a role that was granted too much, and
    /// that catches one person holding both halves.
    /// </remarks>
    [Fact]
    public void Only_a_checker_may_approve()
    {
        Assert.True(PayoutLifecycle.IsAllowed(
            PayoutBatchStatus.Draft, PayoutBatchStatus.Approved, PayoutActor.Checker));

        Assert.False(PayoutLifecycle.IsAllowed(
            PayoutBatchStatus.Draft, PayoutBatchStatus.Approved, PayoutActor.Maker));

        // The edge exists — it is the actor that is wrong, which is what lets a handler answer 403
        // rather than 409.
        Assert.True(PayoutLifecycle.Exists(PayoutBatchStatus.Draft, PayoutBatchStatus.Approved));
    }

    /// <summary>An outcome is the gateway's fact, never an operator's opinion.</summary>
    /// <remarks>
    /// No human actor may declare a batch complete. One who could would be able to mark a seller paid
    /// who was not, and the cycle would be discharged by money that never moved.
    /// </remarks>
    [Theory]
    [InlineData(PayoutBatchStatus.Completed)]
    [InlineData(PayoutBatchStatus.PartiallyFailed)]
    [InlineData(PayoutBatchStatus.Failed)]
    internal void Only_the_system_may_declare_an_outcome(PayoutBatchStatus outcome)
    {
        Assert.True(PayoutLifecycle.IsAllowed(PayoutBatchStatus.Processing, outcome, PayoutActor.System));
        Assert.False(PayoutLifecycle.IsAllowed(PayoutBatchStatus.Processing, outcome, PayoutActor.Staff));
    }

    /// <summary>Nothing leaves a batch that has stopped moving.</summary>
    /// <remarks>
    /// A failed transfer is retried by putting the cycle into a <em>new</em> batch once the reason is
    /// fixed, which leaves the failed one standing as the record that it was tried and why it did not
    /// work. Re-opening it would give one row two outcomes.
    /// </remarks>
    [Theory]
    [InlineData(PayoutBatchStatus.Completed)]
    [InlineData(PayoutBatchStatus.PartiallyFailed)]
    [InlineData(PayoutBatchStatus.Failed)]
    [InlineData(PayoutBatchStatus.Cancelled)]
    internal void A_terminal_batch_has_nowhere_to_go(PayoutBatchStatus status)
    {
        Assert.True(PayoutLifecycle.IsTerminal(status));
        Assert.Empty(PayoutLifecycle.NextFor(status, PayoutActor.Anyone));
    }

    /// <summary>A batch's outcome is derived from its items, never decided.</summary>
    /// <remarks>
    /// A batch of nothing but skipped items counts as failed. No money left, and calling that a
    /// completion would mark cycles paid that were not.
    /// </remarks>
    [Theory]
    [InlineData(3, 0, PayoutBatchStatus.Completed)]
    [InlineData(2, 1, PayoutBatchStatus.PartiallyFailed)]
    [InlineData(0, 3, PayoutBatchStatus.Failed)]
    [InlineData(0, 0, PayoutBatchStatus.Failed)]
    internal void The_outcome_follows_from_the_items(int completed, int failed, PayoutBatchStatus expected)
        => Assert.Equal(expected, PayoutLifecycle.Outcome(completed, failed));

    /// <summary>The aggregate refuses an approval by the person who raised the batch.</summary>
    /// <remarks>
    /// The identity half of maker–checker, and the reason it is enforced here as well as in the
    /// handler and in a database constraint: granting both permissions to one finance role is a
    /// reasonable thing for an operator to do, and this is what still requires two people afterwards.
    /// </remarks>
    [Fact]
    public void Nobody_approves_their_own_batch()
    {
        var maker = Guid.CreateVersion7();
        var batch = PayoutBatch.Draft("PAY-2609-000001", "INR", DateTimeOffset.UtcNow, maker);

        Assert.Throws<InvalidOperationException>(() => batch.Approve(maker, DateTimeOffset.UtcNow));

        batch.Approve(Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        Assert.Equal(PayoutBatchStatus.Approved, batch.Status);
    }

    /// <summary>A batch settles only once nothing in it is still in flight.</summary>
    /// <remarks>
    /// A batch that reported itself complete with a transfer still pending would mark a cycle paid
    /// that had not been. The skipped item counts towards the outcome without ever having been sent,
    /// which is why this ends partially failed rather than complete.
    /// </remarks>
    [Fact]
    public void A_batch_settles_only_when_every_item_has_stopped()
    {
        var now = DateTimeOffset.UtcNow;
        var batch = PayoutBatch.Draft("PAY-2609-000002", "INR", now, Guid.CreateVersion7());

        var paid = PayoutItem.For(batch.Id, Guid.CreateVersion7(), Guid.CreateVersion7(), 500m, "INR");
        var skipped = PayoutItem.For(batch.Id, Guid.CreateVersion7(), Guid.CreateVersion7(), 250m, "INR");

        batch.Add(paid);
        batch.Add(skipped);

        Assert.Equal(750m, batch.TotalAmount);
        Assert.Equal(2, batch.VendorCount);

        batch.Approve(Guid.CreateVersion7(), now);
        batch.BeginProcessing("route", now);

        // One still in flight: nothing settles.
        paid.Sent("acc_1", "4321", "trf_1", "pending", now);
        Assert.False(batch.TrySettle(now));

        skipped.Skip("The seller has no verified bank account.", now);
        Assert.False(batch.TrySettle(now));

        paid.Complete("UTR123", "processed", now);
        Assert.True(batch.TrySettle(now));

        Assert.Equal(PayoutBatchStatus.PartiallyFailed, batch.Status);

        // What actually left the account, as opposed to what was signed off.
        Assert.Equal(500m, batch.SettledAmount);

        // The headline figure does not move once the batch has been approved, and that is
        // deliberate: it is the number somebody put their name against, and a document whose total
        // changed after it was signed would be a document nobody could audit. An item skipped at
        // build time never counts, because the skip happens before it is added.
        Assert.Equal(750m, batch.TotalAmount);
    }
}
