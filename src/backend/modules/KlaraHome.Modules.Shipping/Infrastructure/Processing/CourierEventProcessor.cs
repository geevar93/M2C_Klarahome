using KlaraHome.Modules.Shipping.Application;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.Modules.Shipping.Infrastructure.Fulfilment;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Shipping.Infrastructure.Processing;

/// <summary>
/// Turns a stored courier webhook into movement.
/// </summary>
/// <remarks>
/// <para>
/// The other half of the webhook contract in docs/04-api-specification.md §5. The endpoint verifies,
/// stores and answers <c>200</c> in milliseconds; this applies what was stored, with a retry budget
/// and a dead-letter queue behind it — so an aggregator is never kept waiting while an order moves,
/// a shopper is notified and cash is settled.
/// </para>
/// <para>
/// An event whose signature never verified is never processed. The worker's claim query excludes
/// them and this method refuses them again, because a control that lives in one place is a control a
/// future query can forget about.
/// </para>
/// <para>
/// An event about a parcel this platform has no record of is <b>ignored, not failed</b>. Retrying
/// will not make an unknown air waybill start existing, and a courier that is misrouting webhooks
/// would otherwise fill the dead-letter queue with somebody else's parcels. It is logged, because a
/// stream of them means the aggregator account is shared or the webhook URL is wrong.
/// </para>
/// </remarks>
/// <param name="context">The Shipping data context.</param>
/// <param name="providers">The adapters, for parsing a payload the way its sender writes them.</param>
/// <param name="workflow">The single place a parcel moves.</param>
/// <param name="logger">Reports what could not be matched.</param>
internal sealed partial class CourierEventProcessor(
    ShippingDbContext context,
    ShippingProviderRegistry providers,
    ShipmentWorkflow workflow,
    ILogger<CourierEventProcessor> logger)
{
    /// <summary>
    /// Applies one stored event.
    /// </summary>
    /// <remarks>
    /// Success here means "this event needs no further attempt", which includes the cases where
    /// nothing happened: an unknown parcel, a payload carrying no scans, a scan already recorded.
    /// Failure is reserved for what a retry might fix.
    /// </remarks>
    /// <param name="stored">The event, loaded for update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> ProcessAsync(CourierEvent stored, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stored);

        if (!stored.SignatureValid)
        {
            return Result.Failure(ShippingErrors.EventNotReplayable);
        }

        var provider = providers.For(stored.Provider);
        var envelope = provider.ReadWebhook(stored.Payload);

        if (envelope.IsFailure)
        {
            // Stored, unreadable, and not worth retrying: the bytes will not change. It is a success
            // so the event settles out of the queue, and the payload stays as evidence.
            Unreadable(logger, stored.ProviderEventId, envelope.Error.Message);
            return Result.Success();
        }

        var read = envelope.Value;
        var shipment = await FindAsync(read, stored, cancellationToken).ConfigureAwait(false);

        if (shipment is null)
        {
            UnknownParcel(logger, read.Awb ?? "<none>", stored.Provider);
            return Result.Success();
        }

        foreach (var scan in read.Scans.OrderBy(scan => scan.OccurredAt))
        {
            var applied = await workflow.ApplyScanAsync(shipment, scan, cancellationToken).ConfigureAwait(false);

            if (applied.IsFailure)
            {
                return applied;
            }
        }

        // The courier's own figures, where the payload carried them. They arrive on a delivery
        // notification more often than in an invoice, and this is the cheapest place to catch them.
        stored.MarkProcessed(shipment.Id, stored.ReceivedAt);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// The parcel an event is about.
    /// </summary>
    /// <remarks>
    /// By air waybill first, because that is what every courier payload carries and what the unique
    /// index is on. Our own consignment id is tried second, for the aggregators that echo back the
    /// reference we sent — it is the more reliable of the two when it is present, and it is present
    /// far less often.
    /// </remarks>
    private async Task<Shipment?> FindAsync(
        CourierWebhookEnvelope read,
        CourierEvent stored,
        CancellationToken cancellationToken)
    {
        var awb = read.Awb ?? stored.Awb;

        // The query filters are bypassed on purpose. A webhook has no caller and no vendor scope, and
        // a filtered read would find nothing at all — which looks exactly like a parcel this platform
        // does not have.
        if (!string.IsNullOrWhiteSpace(awb))
        {
            var found = await context.Shipments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(shipment => shipment.Awb == awb, cancellationToken)
                .ConfigureAwait(false);

            if (found is not null)
            {
                return found;
            }
        }

        return read.ShipmentId is { } id
            ? await context.Shipments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(shipment => shipment.Id == id, cancellationToken)
                .ConfigureAwait(false)
            : null;
    }

    [LoggerMessage(EventId = 1750, Level = LogLevel.Warning,
        Message = "Courier event {ProviderEventId} could not be read and will not be retried: {Detail}")]
    private static partial void Unreadable(ILogger logger, string providerEventId, string detail);

    [LoggerMessage(EventId = 1751, Level = LogLevel.Warning,
        Message = "A {Provider} webhook named air waybill {Awb}, which this platform has no record of. "
                  + "It was stored and ignored.")]
    private static partial void UnknownParcel(ILogger logger, string awb, string provider);
}
