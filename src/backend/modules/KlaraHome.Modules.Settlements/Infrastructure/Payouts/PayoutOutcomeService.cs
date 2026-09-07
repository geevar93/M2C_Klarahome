using KlaraHome.Contracts.Settlements;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Settlements.Infrastructure.Payouts;

/// <summary>
/// Applies a gateway's word about one transfer, as soon as it arrives rather than at the next sweep
/// (docs/08-integrations.md §1).
/// </summary>
/// <remarks>
/// <para>
/// The same three steps the reconciliation sweep takes for every in-flight item, taken for one:
/// find the item, ask the gateway what became of it, and let the workflow apply the answer. It is a
/// shortcut through the sweep's latency, not a second way of moving money — everything still goes
/// through <see cref="PayoutWorkflow.ApplyAsync"/>, so a webhook and a sweep write the same ledger
/// entries and raise the same events.
/// </para>
/// <para>
/// It re-fetches rather than believing the event. The webhook said something happened; the gateway
/// is asked what. That is the rule every payment webhook on this platform already follows, and it is
/// what keeps a signed but stale delivery from marking a reversed transfer as paid.
/// </para>
/// <para>
/// An unknown or already-settled transfer is a no-op that answers false. Delivery is at-least-once
/// and the sweep may well have got there first, so a replay must cost nothing.
/// </para>
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="registry">Finds the rail the batch was sent on.</param>
/// <param name="workflow">Applies the answer. The single place a payout item moves.</param>
/// <param name="logger">Reports what a delivery turned out to be about.</param>
internal sealed partial class PayoutOutcomeService(
    SettlementsDbContext context,
    PayoutProviderRegistry registry,
    PayoutWorkflow workflow,
    ILogger<PayoutOutcomeService> logger) : IPayoutOutcomes
{
    /// <inheritdoc />
    public async Task<bool> RefreshAsync(string providerPayoutId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerPayoutId))
        {
            return false;
        }

        var item = await context.PayoutItems
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.TenantId == context.TenantId
                             && candidate.ProviderPayoutId == providerPayoutId
                             && candidate.Status == PayoutItemStatus.Processing,
                cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            // Either the sweep got there first, or the transfer belongs to another deployment
            // sharing the gateway account. Neither is an error, and neither is worth a warning on a
            // path a gateway retries.
            return false;
        }

        var batch = await context.PayoutBatches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.TenantId == context.TenantId && candidate.Id == item.PayoutBatchId,
                cancellationToken)
            .ConfigureAwait(false);

        if (batch is null)
        {
            return false;
        }

        var answer = await registry
            .For(batch.Provider)
            .FetchAsync(providerPayoutId, cancellationToken)
            .ConfigureAwait(false);

        if (answer.IsFailure)
        {
            // The gateway could not be asked. Nothing is concluded from that: the transfer is
            // whatever it already was, and the sweep asks again in a quarter of an hour.
            GatewayUnavailable(logger, providerPayoutId);
            return false;
        }

        if (!answer.Value.IsProcessed && !answer.Value.IsFailed)
        {
            // The event arrived ahead of the gateway's own state, which happens. Leaving it in
            // flight is right; the sweep will catch it.
            return false;
        }

        await workflow.ApplyAsync(batch, item, answer.Value, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await workflow.SettleAsync(batch, cancellationToken).ConfigureAwait(false);

        TransferApplied(logger, providerPayoutId, batch.Reference, answer.Value.Status);
        return true;
    }

    [LoggerMessage(EventId = 1870, Level = LogLevel.Information,
        Message = "A transfer webhook moved {ProviderPayoutId} in batch {BatchReference} to {Status}.")]
    private static partial void TransferApplied(
        ILogger logger,
        string providerPayoutId,
        string batchReference,
        string status);

    [LoggerMessage(EventId = 1871, Level = LogLevel.Warning,
        Message = "A transfer webhook named {ProviderPayoutId}, but the gateway could not be asked what "
                  + "became of it. The reconciliation sweep will try again.")]
    private static partial void GatewayUnavailable(ILogger logger, string providerPayoutId);
}
