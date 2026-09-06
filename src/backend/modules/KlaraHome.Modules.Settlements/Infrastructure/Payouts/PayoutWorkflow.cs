using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Settlements.Application;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure.Accounting;
using KlaraHome.Modules.Settlements.Infrastructure.Events;
using KlaraHome.Modules.Settlements.Infrastructure.Numbering;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Primitives;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Settlements.Infrastructure.Payouts;

/// <summary>
/// The one place a payout batch is built, signed off and sent.
/// </summary>
/// <remarks>
/// <para>
/// Every route into a batch comes through here — an operator's button, the scheduler's unattended
/// run, the reconciliation sweep's answer from the gateway — so none of them can move a batch in a
/// way the others would not.
/// </para>
/// <para>
/// <b>Sending is resumable and never atomic.</b> A batch of four hundred transfers is four hundred
/// HTTP calls, and a request that tried to make them all would time out somewhere in the middle with
/// no record of where. Each item is saved as soon as the gateway answers about it, so a process that
/// dies halfway leaves four hundred items in a known state and the next pass picks up the ones still
/// pending. That is the opposite of the usual rule about transactions, and it is right here: the
/// money has already moved, and the only sin is failing to write down that it did.
/// </para>
/// <para>
/// <b>A completed transfer posts a ledger entry and marks the cycle paid; a failed one does neither.</b>
/// The cycle stays payable and can go into a new batch once the reason is fixed. Nothing is retried
/// inside the batch: two attempts on one row would leave one row with two outcomes and no way to say
/// which bank reference belonged to which.
/// </para>
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="registry">Picks the rail.</param>
/// <param name="poster">Posts the ledger entry a completed transfer produces.</param>
/// <param name="numbering">Allocates the batch reference.</param>
/// <param name="vendors">Reads where a seller's money goes.</param>
/// <param name="settings">Supplies the payout minimum and the approval threshold.</param>
/// <param name="options">Supplies how many transfers one pass sends.</param>
/// <param name="events">Announces what reached a seller and what did not.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what was built, sent and refused.</param>
internal sealed partial class PayoutWorkflow(
    SettlementsDbContext context,
    PayoutProviderRegistry registry,
    SettlementPoster poster,
    PayoutNumbering numbering,
    IVendorPayouts vendors,
    IStoreSettings settings,
    IOptionsMonitor<PayoutOptions> options,
    SettlementsEventPublisher events,
    IClock clock,
    ILogger<PayoutWorkflow> logger)
{
    /// <summary>
    /// Builds a draft batch from closed cycles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A cycle goes in when it is closed, is not already in a batch, and is worth at least the store's
    /// payout minimum. A seller who cannot be paid — suspended, or with no account at the gateway —
    /// still gets a line, skipped, with the reason on it: "why was this seller not paid" is a question
    /// that must have its answer in the batch rather than in somebody's memory.
    /// </para>
    /// <para>
    /// A cycle whose net is zero or negative is left out entirely. There is nothing to send, and a
    /// negative balance is carried into the next period as an opening balance rather than becoming a
    /// demand on the seller.
    /// </para>
    /// </remarks>
    /// <param name="cycleIds">The cycles to pay. Empty means every closed, unbatched, payable one.</param>
    /// <param name="requestedBy">Who is building it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<PayoutBatch>> BuildAsync(
        IReadOnlyCollection<Guid> cycleIds,
        Guid? requestedBy,
        CancellationToken cancellationToken)
    {
        var policy = await settings.GetAsync<SettlementSettings>(cancellationToken).ConfigureAwait(false);

        var query = context.Cycles
            .IgnoreQueryFilters()
            .Where(cycle => cycle.TenantId == context.TenantId
                            && cycle.Status == SettlementCycleStatus.Closed
                            && cycle.PayoutBatchId == null
                            && cycle.NetPayable >= policy.MinimumPayoutAmount
                            && cycle.NetPayable > 0m);

        if (cycleIds.Count > 0)
        {
            var wanted = cycleIds.Distinct().ToArray();
            query = query.Where(cycle => wanted.Contains(cycle.Id));
        }

        var cycles = await query
            .OrderBy(cycle => cycle.PeriodStart)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (cycles.Count == 0)
        {
            return Result.Failure<PayoutBatch>(SettlementsErrors.NothingToPay);
        }

        var reference = await numbering.NextReferenceAsync(clock.UtcNow, cancellationToken).ConfigureAwait(false);
        var batch = PayoutBatch.Draft(reference, Money.Inr, clock.UtcNow, requestedBy);

        context.PayoutBatches.Add(batch);

        var profiles = await vendors
            .FindManyAsync(
                [.. cycles.Select(cycle => cycle.VendorId ?? Guid.Empty).Distinct()],
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var cycle in cycles)
        {
            var vendorId = cycle.VendorId ?? Guid.Empty;
            var item = PayoutItem.For(batch.Id, vendorId, cycle.Id, cycle.NetPayable, cycle.CurrencyCode);
            var profile = profiles.GetValueOrDefault(vendorId);

            item.Payee(profile?.Code, profile?.LegalName);

            if (profile is null || !profile.IsPayable)
            {
                item.Skip(profile?.NotPayableReason ?? "The seller could not be found.", clock.UtcNow);
            }

            batch.Add(item);
            cycle.Batch(batch.Id);
        }

        BatchBuilt(logger, batch.Reference, batch.VendorCount, batch.TotalAmount);

        return Result.Success(batch);
    }

    /// <summary>
    /// Signs a batch off.
    /// </summary>
    /// <remarks>
    /// Three checks and they are deliberately in this order: whether the caller may take the edge at
    /// all, whether the aggregate allows it from where the batch stands, and whether the approver is
    /// the person who raised it. The last is repeated by the aggregate and by a database constraint;
    /// it is checked here as well so an operator gets a sentence rather than a 500.
    /// </remarks>
    /// <param name="batch">The batch.</param>
    /// <param name="actor">Who is asking.</param>
    /// <param name="approverId">The user signing it off.</param>
    public Result Approve(PayoutBatch batch, PayoutActor actor, Guid? approverId)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (!PayoutLifecycle.IsAllowed(batch.Status, PayoutBatchStatus.Approved, actor))
        {
            return PayoutLifecycle.Exists(batch.Status, PayoutBatchStatus.Approved)
                ? Result.Failure(SettlementsErrors.NotPermitted("approve"))
                : Result.Failure(SettlementsErrors.BatchNotIn(batch.Status.ToString(), "approved"));
        }

        if (approverId is not { } approver)
        {
            return Result.Failure(SettlementsErrors.NotPermitted("approve"));
        }

        if (batch.RequestedBy == approver)
        {
            return Result.Failure(SettlementsErrors.SelfApproval);
        }

        batch.Approve(approver, clock.UtcNow);

        BatchApproved(logger, batch.Reference, approver, batch.TotalAmount);

        return Result.Success();
    }

    /// <summary>
    /// Hands an approved batch to the gateway, one transfer at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every item is saved as soon as the gateway answers about it. See the class remarks: the money
    /// has already moved by the time we learn about it, and the only failure that matters here is
    /// failing to write that down.
    /// </para>
    /// <para>
    /// The pass is bounded and resumable. It sends at most a configured number of transfers and
    /// leaves the rest pending; calling it again continues from where it stopped, and the batch
    /// settles itself once nothing is in flight.
    /// </para>
    /// </remarks>
    /// <param name="batch">The batch.</param>
    /// <param name="actor">Who is asking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> ProcessAsync(
        PayoutBatch batch,
        PayoutActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Status == PayoutBatchStatus.Approved)
        {
            if (!PayoutLifecycle.IsAllowed(batch.Status, PayoutBatchStatus.Processing, actor))
            {
                return Result.Failure(SettlementsErrors.NotPermitted("send"));
            }

            var provider = registry.Default;

            if (!provider.IsConfigured)
            {
                return Result.Failure(SettlementsErrors.ProviderUnavailable);
            }

            batch.BeginProcessing(provider.Name, clock.UtcNow);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (batch.Status != PayoutBatchStatus.Processing)
        {
            return Result.Failure(SettlementsErrors.BatchNotIn(batch.Status.ToString(), "sent"));
        }

        var rail = registry.For(batch.Provider);
        var pending = batch.Items
            .Where(item => item.Status == PayoutItemStatus.Pending)
            .Take(options.CurrentValue.SendBatchSize)
            .ToArray();

        var destinations = await vendors
            .FindManyAsync([.. pending.Select(item => item.VendorId ?? Guid.Empty).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        foreach (var item in pending)
        {
            await SendAsync(batch, item, rail, destinations, cancellationToken).ConfigureAwait(false);
        }

        await SettleAsync(batch, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// Applies what the gateway says about one transfer.
    /// </summary>
    /// <remarks>
    /// The single place a provider's answer becomes a fact, whether it arrived as the response to a
    /// send or as the answer to a reconciliation sweep's question. A completion posts the ledger
    /// entry, marks the cycle paid and announces it; a failure records the reason and leaves the cycle
    /// payable. Both are idempotent — the ledger entry is keyed on the payout item, and an item that
    /// has already settled is not settled again.
    /// </remarks>
    /// <param name="batch">The batch the transfer belongs to.</param>
    /// <param name="item">The transfer.</param>
    /// <param name="answer">What the gateway said.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ApplyAsync(
        PayoutBatch batch,
        PayoutItem item,
        ProviderPayout answer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(answer);

        if (item.IsSettled)
        {
            return;
        }

        if (answer.IsProcessed)
        {
            item.Complete(answer.Utr, answer.Status, answer.OccurredAt ?? clock.UtcNow);

            await poster
                .PostPayoutAsync(item, item.SettledAt ?? clock.UtcNow, cancellationToken)
                .ConfigureAwait(false);

            await MarkCyclePaidAsync(batch, item, cancellationToken).ConfigureAwait(false);

            events.PayoutCompleted(batch, item);
            TransferCompleted(logger, batch.Reference, item.VendorId ?? Guid.Empty, item.Amount);

            return;
        }

        if (answer.IsFailed)
        {
            item.Fail(answer.Error, answer.Status, clock.UtcNow);
            await ReleaseCycleAsync(item, cancellationToken).ConfigureAwait(false);

            events.PayoutFailed(batch, item);
            TransferFailed(logger, batch.Reference, item.VendorId ?? Guid.Empty, answer.Error);

            return;
        }

        // Still moving. The gateway has it, we have its id, and the reconciliation sweep will ask
        // again — which is the whole reason a payout has a status of its own rather than a boolean.
        item.Sent(
            item.DestinationAccountId,
            item.DestinationLast4,
            answer.ProviderPayoutId,
            answer.Status,
            item.SentAt ?? clock.UtcNow);
    }

    /// <summary>
    /// Settles the batch if nothing is in flight, and releases the cycles of anything that failed.
    /// </summary>
    /// <param name="batch">The batch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SettleAsync(PayoutBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.TrySettle(clock.UtcNow))
        {
            BatchSettled(logger, batch.Reference, batch.Status, batch.SettledAmount);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends one transfer and records whatever comes back.</summary>
    private async Task SendAsync(
        PayoutBatch batch,
        PayoutItem item,
        IPayoutProvider rail,
        IReadOnlyDictionary<Guid, VendorPayoutProfile> destinations,
        CancellationToken cancellationToken)
    {
        var vendorId = item.VendorId ?? Guid.Empty;
        var profile = destinations.GetValueOrDefault(vendorId);

        if (profile is null || !profile.IsPayable || string.IsNullOrWhiteSpace(profile.GatewayAccountId))
        {
            // Not a failure of the transfer: nothing was attempted. The distinction is what tells an
            // operator to fix the seller's record rather than to chase a bank.
            item.Skip(
                profile?.NotPayableReason
                ?? "The seller has no payout account at the gateway.",
                clock.UtcNow);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        item.Sent(
            profile.GatewayAccountId,
            profile.BankAccountLast4,
            providerPayoutId: null,
            providerStatus: null,
            clock.UtcNow);

        var answer = await rail
            .SendAsync(
                new PayoutRequest(
                    item.Id,
                    batch.Reference,
                    vendorId,
                    profile.GatewayAccountId,
                    item.Amount,
                    item.CurrencyCode,
                    options.CurrentValue.Narration),
                cancellationToken)
            .ConfigureAwait(false);

        if (answer.IsFailure)
        {
            item.Fail(answer.Error.Message, providerStatus: null, clock.UtcNow);
            await ReleaseCycleAsync(item, cancellationToken).ConfigureAwait(false);
            events.PayoutFailed(batch, item);
            TransferFailed(logger, batch.Reference, vendorId, answer.Error.Message);
        }
        else
        {
            await ApplyAsync(batch, item, answer.Value, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Marks the cycle a completed transfer discharged.</summary>
    private async Task MarkCyclePaidAsync(
        PayoutBatch batch,
        PayoutItem item,
        CancellationToken cancellationToken)
    {
        if (item.SettlementCycleId is not { } cycleId)
        {
            return;
        }

        var cycle = await context.Cycles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.TenantId == context.TenantId && candidate.Id == cycleId,
                cancellationToken)
            .ConfigureAwait(false);

        if (cycle is { Status: SettlementCycleStatus.Closed })
        {
            cycle.MarkPaid(batch.Id, item.SettledAt ?? clock.UtcNow);
        }
    }

    /// <summary>
    /// Frees a failed transfer's cycle to be paid again.
    /// </summary>
    /// <remarks>
    /// The cycle stays closed and its figures stay frozen; only its link to this batch is cleared, so
    /// the next run picks it up once the reason for the failure is fixed. Re-opening it would
    /// recompute a period the seller has already been sent a statement for.
    /// </remarks>
    private async Task ReleaseCycleAsync(PayoutItem item, CancellationToken cancellationToken)
    {
        if (item.SettlementCycleId is not { } cycleId)
        {
            return;
        }

        var cycle = await context.Cycles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.TenantId == context.TenantId && candidate.Id == cycleId,
                cancellationToken)
            .ConfigureAwait(false);

        if (cycle is { Status: SettlementCycleStatus.Closed })
        {
            cycle.Batch(null);
        }
    }

    [LoggerMessage(EventId = 1830, Level = LogLevel.Information,
        Message = "Built payout batch {BatchReference}: {VendorCount} seller(s), {TotalAmount}.")]
    private static partial void BatchBuilt(
        ILogger logger,
        string batchReference,
        int vendorCount,
        decimal totalAmount);

    [LoggerMessage(EventId = 1831, Level = LogLevel.Information,
        Message = "Payout batch {BatchReference} approved by {ApproverId} for {TotalAmount}.")]
    private static partial void BatchApproved(
        ILogger logger,
        string batchReference,
        Guid approverId,
        decimal totalAmount);

    [LoggerMessage(EventId = 1832, Level = LogLevel.Information,
        Message = "Paid {Amount} to vendor {VendorId} in batch {BatchReference}.")]
    private static partial void TransferCompleted(
        ILogger logger,
        string batchReference,
        Guid vendorId,
        decimal amount);

    [LoggerMessage(EventId = 1833, Level = LogLevel.Warning,
        Message = "Could not pay vendor {VendorId} in batch {BatchReference}: {FailureReason}")]
    private static partial void TransferFailed(
        ILogger logger,
        string batchReference,
        Guid vendorId,
        string? failureReason);

    [LoggerMessage(EventId = 1834, Level = LogLevel.Information,
        Message = "Payout batch {BatchReference} finished as {BatchStatus}; {SettledAmount} left the account.")]
    private static partial void BatchSettled(
        ILogger logger,
        string batchReference,
        PayoutBatchStatus batchStatus,
        decimal settledAmount);
}
