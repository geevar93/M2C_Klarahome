using KlaraHome.Contracts.Shipping;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using KlaraHome.Modules.Payments.Infrastructure.Processing;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Payments.Infrastructure.Events;

/// <summary>
/// Lets go of the refund for an order cancelled after dispatch, once its parcel is back.
/// </summary>
/// <remarks>
/// <para>
/// A cancellation made after the courier collected the parcel raises its refund held
/// (<see cref="Refund.IsHeldForReturn"/>), because until the goods come back the shopper could end
/// up with both. The parcel reaching the seller again — <c>RtoDelivered</c> in Shipping's vocabulary
/// — is what releases it: approved and sent under the approval threshold, left for its second
/// signature over it.
/// </para>
/// <para>
/// Every other scan is ignored, and so is a return that belongs to no held refund — the ordinary
/// return to origin of an order nobody cancelled has no refund here to release. Redelivery finds the
/// refund already released and does nothing.
/// </para>
/// </remarks>
/// <param name="context">The Payments data context.</param>
/// <param name="refunds">Sends the ones that are approved on release.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what was released.</param>
internal sealed partial class ShippingLifecycleHandlers(
    PaymentsDbContext context,
    RefundDispatcher refunds,
    IClock clock,
    ILogger<ShippingLifecycleHandlers> logger) : IIntegrationEventHandler<ShipmentTrackingUpdated>
{
    /// <summary>The status Shipping reports when a returning parcel reaches the seller.</summary>
    private const string BackWithSeller = "RtoDelivered";

    /// <inheritdoc />
    public async Task HandleAsync(ShipmentTrackingUpdated integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!string.Equals(integrationEvent.Status, BackWithSeller, StringComparison.Ordinal))
        {
            return;
        }

        var payments = await context.Payments
            .IgnoreQueryFilters()
            .Include(payment => payment.Refunds)
            .Where(payment => payment.Refunds.Any(refund => refund.SubOrderId == integrationEvent.SubOrderId
                                                            && refund.IsHeldForReturn))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (payments.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;

        foreach (var payment in payments)
        {
            var held = payment.Refunds
                .Where(refund => refund.SubOrderId == integrationEvent.SubOrderId && refund.IsHeldForReturn)
                .ToList();

            foreach (var refund in held)
            {
                if (!refund.ReleaseAfterReturn(now))
                {
                    continue;
                }

                // Sends only what the release approved; one over the threshold stays in the queue.
                await refunds.SendAsync(payment, refund, cancellationToken).ConfigureAwait(false);

                Released(logger, refund.Amount, integrationEvent.Awb, refund.Status);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 1615, Level = LogLevel.Information,
        Message = "Released the held refund of {Amount}: parcel {Awb} is back with the seller. It is {Status}.")]
    private static partial void Released(ILogger logger, decimal amount, string awb, RefundStatus status);
}
