using KlaraHome.Contracts.Vendors;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Catalog.Infrastructure.Events;

/// <summary>
/// Keeps a seller's offers in step with whether that seller may trade
/// (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// "A Listing cannot be Active if its Vendor is not Active" (§4.1) is an invariant nothing else can
/// maintain: the Vendors module owns the seller's status and this module owns the offers, and no
/// query may cross that boundary. The event is the join.
/// </para>
/// <para>
/// Delivery is at-least-once, so every method here is idempotent — a redelivered suspension finds
/// no live offers left and does nothing. Suspension and offboarding are handled identically, and
/// deliberately: both mean "stop selling now", and the difference between them is the Vendors
/// module's to record, not this one's to model twice.
/// </para>
/// <para>
/// Reactivation deliberately does <b>not</b> put the offers back. The seller paused them or the
/// platform did; which of those it was is not recorded on the listing, and guessing would republish
/// prices a suspended seller never got to review. They are theirs to bring back.
/// </para>
/// </remarks>
/// <param name="context">The Catalog data context.</param>
/// <param name="publisher">Announces each withdrawal downstream.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports how many offers were affected.</param>
internal sealed partial class VendorLifecycleHandlers(
    CatalogDbContext context,
    CatalogEventPublisher publisher,
    IClock clock,
    ILogger<VendorLifecycleHandlers> logger)
    : IIntegrationEventHandler<VendorActivated>,
        IIntegrationEventHandler<VendorSuspended>,
        IIntegrationEventHandler<VendorOffboarded>
{
    /// <inheritdoc />
    public Task HandleAsync(VendorActivated integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        VendorActivatedNoted(logger, integrationEvent.VendorId);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task HandleAsync(VendorSuspended integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return WithdrawAsync(
            integrationEvent.VendorId,
            integrationEvent.Reason ?? "The seller was suspended.",
            cancellationToken);
    }

    /// <inheritdoc />
    public Task HandleAsync(VendorOffboarded integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return WithdrawAsync(
            integrationEvent.VendorId,
            integrationEvent.Reason ?? "The seller left the marketplace.",
            cancellationToken);
    }

    private async Task WithdrawAsync(Guid vendorId, string reason, CancellationToken cancellationToken)
    {
        var listings = await context.Listings
            .Where(listing => listing.VendorId == vendorId && listing.Status == ListingStatus.Active)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (listings.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;

        foreach (var listing in listings)
        {
            if (listing.TransitionTo(ListingStatus.Inactive, now, reason))
            {
                publisher.Deactivated(listing, reason);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ListingsWithdrawn(logger, listings.Count, vendorId, reason);
    }

    [LoggerMessage(
        EventId = 6100,
        Level = LogLevel.Information,
        Message = "Seller {VendorId} began trading; their existing offers are theirs to republish")]
    private static partial void VendorActivatedNoted(ILogger logger, Guid vendorId);

    [LoggerMessage(
        EventId = 6101,
        Level = LogLevel.Warning,
        Message = "Withdrew {Count} offers for seller {VendorId}: {Reason}")]
    private static partial void ListingsWithdrawn(ILogger logger, int count, Guid vendorId, string reason);
}
