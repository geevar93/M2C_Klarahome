using KlaraHome.Contracts.Pricing;
using KlaraHome.Modules.Pricing.Domain;
using KlaraHome.Modules.Pricing.Infrastructure.Events;
using KlaraHome.Modules.Pricing.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Pricing.Infrastructure.Promotions;

/// <summary>
/// Commits and reverses promotion use (docs/03-database-design.md §4.6).
/// </summary>
/// <remarks>
/// <para>
/// The usage counter is claimed with a <b>conditional update</b> — one statement that increments
/// only while the count is still below the limit — and that single statement is what makes a
/// single-use coupon single-use. Reading the count, deciding, and then writing it back would let
/// two orders placed in the same second both see the last use available; the database decides
/// instead, and exactly one of them wins.
/// </para>
/// <para>
/// The whole of a redemption is one transaction: the claim, the row, and the event. Claiming a use
/// and then failing to record it would spend a coupon nobody can prove was used, and the counter
/// would drift permanently away from the rows that are supposed to explain it.
/// </para>
/// <para>
/// That transaction is opened through <c>KlaraHomeDbContext.ExecuteInTransactionAsync</c>
/// rather than by hand. Retry-on-failure is enabled for every context in this platform, and EF
/// refuses a hand-rolled <c>BeginTransactionAsync</c> under a retrying strategy — the strategy has
/// to wrap the transaction and not the other way round. Both methods below therefore read their own
/// inputs inside the operation and reset what they answer at the top of it, because the operation
/// may run more than once.
/// </para>
/// </remarks>
/// <param name="context">The Pricing data context.</param>
/// <param name="events">Announces the redemption once the transaction commits.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class PromotionLedger(
    PricingDbContext context,
    PricingEventPublisher events,
    IClock clock) : IPromotionLedger
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Guid>> RedeemAsync(
        Guid orderId,
        Guid? customerId,
        IReadOnlyList<PromotionRedemptionRequest> redemptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(redemptions);

        if (redemptions.Count == 0)
        {
            return [];
        }

        var wanted = redemptions.Select(redemption => redemption.PromotionId).Distinct().ToList();
        var refused = new List<Guid>();

        await context.ExecuteInTransactionAsync(
            async (_, cancellation) =>
            {
                refused.Clear();

                // Already redeemed against this order? Then this is a replay of an at-least-once
                // event and the right answer is to do nothing at all rather than claim a second use.
                var already = await context.PromotionRedemptions
                    .AsNoTracking()
                    .Where(redemption => redemption.OrderId == orderId && wanted.Contains(redemption.PromotionId))
                    .Select(redemption => redemption.PromotionId)
                    .ToListAsync(cancellation)
                    .ConfigureAwait(false);

                var outstanding = redemptions
                    .Where(redemption => !already.Contains(redemption.PromotionId))
                    .ToList();

                if (outstanding.Count == 0)
                {
                    return;
                }

                var promotions = await context.Promotions
                    .AsNoTracking()
                    .Where(promotion => outstanding.Select(request => request.PromotionId).Contains(promotion.Id))
                    .Select(promotion => new { promotion.Id, promotion.Code })
                    .ToListAsync(cancellation)
                    .ConfigureAwait(false);

                var codes = promotions.ToDictionary(promotion => promotion.Id, promotion => promotion.Code);
                var now = clock.UtcNow;

                foreach (var redemption in outstanding)
                {
                    if (!codes.TryGetValue(redemption.PromotionId, out var code))
                    {
                        // The promotion was deleted between quoting and placing. Nothing to claim,
                        // and nothing the order can do about it, so it is reported rather than
                        // thrown.
                        refused.Add(redemption.PromotionId);
                        continue;
                    }

                    var claimed = await context.Promotions
                        .Where(promotion => promotion.Id == redemption.PromotionId
                                            && (promotion.UsageLimitTotal == null
                                                || promotion.UsageCount < promotion.UsageLimitTotal))
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(
                                promotion => promotion.UsageCount,
                                promotion => promotion.UsageCount + 1),
                            cancellation)
                        .ConfigureAwait(false);

                    if (claimed == 0)
                    {
                        // Somebody else took the last use between the quote and this call. The order
                        // has to be told, because the total it was quoted is no longer the total.
                        refused.Add(redemption.PromotionId);
                        continue;
                    }

                    context.PromotionRedemptions.Add(PromotionRedemption.Create(
                        redemption.PromotionId,
                        code,
                        customerId,
                        orderId,
                        Math.Max(0m, redemption.DiscountAmount),
                        now));

                    events.PromotionRedeemed(
                        redemption.PromotionId,
                        code,
                        orderId,
                        customerId,
                        redemption.DiscountAmount);
                }

                await context.SaveChangesAsync(cancellation).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return refused;
    }

    /// <inheritdoc />
    public async ValueTask<int> ReverseAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var reversed = 0;

        await context.ExecuteInTransactionAsync(
            async (_, cancellation) =>
            {
                reversed = 0;

                var rows = await context.PromotionRedemptions
                    .Where(redemption => redemption.OrderId == orderId
                                         && redemption.Status == RedemptionStatus.Redeemed)
                    .ToListAsync(cancellation)
                    .ConfigureAwait(false);

                if (rows.Count == 0)
                {
                    return;
                }

                var now = clock.UtcNow;

                foreach (var row in rows)
                {
                    if (!row.Reverse(now))
                    {
                        continue;
                    }

                    // Floored at zero. A counter that has been reset by hand must not be driven
                    // negative by a reversal, and a negative usage count would make every subsequent
                    // limit check wrong in the shopper's favour.
                    await context.Promotions
                        .Where(promotion => promotion.Id == row.PromotionId && promotion.UsageCount > 0)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(
                                promotion => promotion.UsageCount,
                                promotion => promotion.UsageCount - 1),
                            cancellation)
                        .ConfigureAwait(false);

                    reversed++;
                }

                await context.SaveChangesAsync(cancellation).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return reversed;
    }
}
