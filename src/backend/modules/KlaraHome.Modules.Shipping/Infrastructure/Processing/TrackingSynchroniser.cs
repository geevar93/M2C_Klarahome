using KlaraHome.Modules.Shipping.Application;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.Modules.Shipping.Infrastructure.Fulfilment;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Shipping.Infrastructure.Processing;

/// <summary>
/// Asks a courier directly what happened to a parcel, and applies the answer.
/// </summary>
/// <remarks>
/// <para>
/// The recovery path for a webhook that was never delivered (docs/08-integrations.md §2), and the
/// only way this platform ever learns about a parcel whose notifications are being dropped. It is
/// the exact counterpart of the payment reconciliation sweep, and it exists for the same reason:
/// a webhook is a courtesy, not a guarantee.
/// </para>
/// <para>
/// It goes through the same workflow a webhook does, so a scan discovered by polling moves the order,
/// writes the timeline and settles cash identically. That is what makes the two paths interchangeable
/// rather than merely similar — and the tracking event's unique id is what stops a scan learned both
/// ways from being recorded twice.
/// </para>
/// <para>
/// It also captures what the courier billed. Charged weight and freight cost usually arrive on the
/// tracking record days before they appear on an invoice, and this is the cheapest place to catch
/// them: the weight dispute this platform will one day have is argued from the difference between
/// that figure and what the packer weighed.
/// </para>
/// </remarks>
/// <param name="context">The Shipping data context.</param>
/// <param name="providers">The adapters, each answering for the parcels it booked.</param>
/// <param name="workflow">The single place a parcel moves.</param>
/// <param name="options">Supplies how long a silence is too long.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what the courier refused to say.</param>
internal sealed partial class TrackingSynchroniser(
    ShippingDbContext context,
    ShippingProviderRegistry providers,
    ShipmentWorkflow workflow,
    IOptions<ShippingOptions> options,
    IClock clock,
    ILogger<TrackingSynchroniser> logger)
{
    /// <summary>
    /// Re-reads one parcel from its courier and applies every scan they report.
    /// </summary>
    /// <param name="shipment">The consignment, loaded for update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> SyncAsync(Shipment shipment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        if (!shipment.IsBooked)
        {
            return Result.Failure(ShippingErrors.NotBooked);
        }

        var provider = providers.For(shipment.Provider);

        var tracked = await provider.TrackAsync(shipment.Awb!, cancellationToken).ConfigureAwait(false);

        if (tracked.IsFailure)
        {
            // Recorded as heard-from anyway, so a hand-booked parcel — which has nobody to ask — is
            // not re-queued every half hour for the rest of its life.
            shipment.MarkTracked(clock.UtcNow);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            TrackFailed(logger, shipment.Awb!, tracked.Error.Message);

            return tracked;
        }

        var courier = tracked.Value;

        shipment.RecordCharges(courier.ChargedWeightGrams, courier.FreightCost);

        foreach (var scan in courier.Scans.OrderBy(scan => scan.OccurredAt))
        {
            var applied = await workflow.ApplyScanAsync(shipment, scan, cancellationToken).ConfigureAwait(false);

            if (applied.IsFailure)
            {
                return applied;
            }
        }

        shipment.MarkTracked(clock.UtcNow);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// The parcels nobody has heard about for too long.
    /// </summary>
    /// <remarks>
    /// Booked, unfinished, and quiet — a draft has no courier to ask and a delivered parcel has
    /// nothing left to say. Ordered oldest-heard-from first, so an interrupted pass resumes with the
    /// parcels that have been silent longest.
    /// </remarks>
    /// <param name="batchSize">How many to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<Guid>> StaleAsync(int batchSize, CancellationToken cancellationToken)
    {
        var silence = TimeSpan.FromHours(options.Value.TrackingSilenceHours);
        var cutoff = clock.UtcNow - silence;

        return await context.Shipments
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(shipment => shipment.Awb != null
                               && shipment.Status != ShipmentStatus.Draft
                               && shipment.Status != ShipmentStatus.Delivered
                               && shipment.Status != ShipmentStatus.RtoDelivered
                               && shipment.Status != ShipmentStatus.Cancelled
                               && (shipment.LastTrackedAt == null || shipment.LastTrackedAt <= cutoff))
            .OrderBy(shipment => shipment.LastTrackedAt)
            .Select(shipment => shipment.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 1760, Level = LogLevel.Information,
        Message = "The courier could not be asked about parcel {Awb}: {Detail}")]
    private static partial void TrackFailed(ILogger logger, string awb, string detail);
}
