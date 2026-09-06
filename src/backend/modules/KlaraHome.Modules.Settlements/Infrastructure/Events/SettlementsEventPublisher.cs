using KlaraHome.Contracts.Settlements;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Settlements.Infrastructure.Events;

/// <summary>
/// Announces what happened to a seller's money (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// Everything goes through the keyed outbox bound to this module's context, so an event is written in
/// the same transaction as the fact it describes. A consumer reacting to a completed payout is
/// reacting to a payout that certainly completed — which is what lets Notifications tell a seller
/// their money has arrived without asking this module to confirm.
/// </para>
/// <para>
/// Three events and no more. A cycle closing is the moment a figure becomes a promise, and a payout
/// succeeding or failing is the moment it becomes money or becomes somebody's work. Everything else
/// this module does is a ledger row, and a ledger row is not news.
/// </para>
/// </remarks>
/// <param name="outbox">The outbox bound to the Settlements context.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class SettlementsEventPublisher(
    [FromKeyedServices(typeof(SettlementsDbContext))] IOutbox outbox,
    IClock clock)
{
    /// <summary>Announces that a period was totalled and fixed.</summary>
    /// <param name="cycle">The cycle.</param>
    public void CycleClosed(SettlementCycle cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        outbox.Enqueue(new SettlementCycleClosed(
            cycle.Id,
            cycle.VendorId ?? Guid.Empty,
            cycle.PeriodStart,
            cycle.PeriodEnd,
            cycle.GrossSales,
            cycle.TotalCommission,
            cycle.TotalFees,
            cycle.TotalRefunds,
            cycle.Tcs,
            cycle.Tds,
            cycle.OpeningBalance,
            cycle.NetPayable,
            cycle.CurrencyCode,
            cycle.ClosedAt ?? clock.UtcNow));
    }

    /// <summary>Announces that money reached a seller.</summary>
    /// <param name="batch">The run it was paid in.</param>
    /// <param name="item">The transfer.</param>
    public void PayoutCompleted(PayoutBatch batch, PayoutItem item)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(item);

        outbox.Enqueue(new PayoutCompleted(
            item.Id,
            batch.Id,
            batch.Reference,
            item.VendorId ?? Guid.Empty,
            item.SettlementCycleId,
            item.Amount,
            item.CurrencyCode,
            item.ProviderPayoutId,
            item.Utr,
            item.SettledAt ?? clock.UtcNow));
    }

    /// <summary>Announces that it did not.</summary>
    /// <param name="batch">The run.</param>
    /// <param name="item">The transfer that failed.</param>
    public void PayoutFailed(PayoutBatch batch, PayoutItem item)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(item);

        outbox.Enqueue(new PayoutFailed(
            item.Id,
            batch.Id,
            batch.Reference,
            item.VendorId ?? Guid.Empty,
            item.SettlementCycleId,
            item.Amount,
            item.CurrencyCode,
            item.FailureReason,
            item.SettledAt ?? clock.UtcNow));
    }
}
