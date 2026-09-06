using KlaraHome.Contracts.Catalog;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Catalog.Infrastructure.Events;

/// <summary>
/// Announces what happened to an offer (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// The outbox is resolved <b>keyed by this module's context</b>, and that is not decoration. The
/// unkeyed registration is first-wins and belongs to whichever module registered first; enqueuing
/// through it here would add the row to a different context's change tracker, this module's
/// <c>SaveChangesAsync</c> would not write it, and the event would be lost with no error anywhere.
/// </para>
/// <para>
/// Nothing is saved here. The event becomes real when the caller's transaction commits, and not
/// before — a handler that publishes and then throws publishes nothing, which is the entire point
/// of the pattern (ADR-003).
/// </para>
/// </remarks>
/// <param name="outbox">This module's outbox, keyed by its context.</param>
internal sealed class CatalogEventPublisher(
    [FromKeyedServices(typeof(CatalogDbContext))] IOutbox outbox)
{
    /// <summary>An offer went live.</summary>
    /// <param name="listing">The offer.</param>
    /// <param name="sku">Its variant's SKU, which the consumer needs and the listing does not hold.</param>
    public void Published(Listing listing, string sku)
    {
        ArgumentNullException.ThrowIfNull(listing);

        outbox.Enqueue(new ListingPublished(
            listing.Id,
            listing.VendorId!.Value,
            listing.VariantId,
            listing.ProductId,
            sku,
            listing.SellingPrice.Amount));
    }

    /// <summary>A live offer's commercial terms changed.</summary>
    /// <param name="listing">The offer.</param>
    public void Updated(Listing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        // Only for an offer that is already live. A draft being edited is not news, and an index
        // that reacted to every keystroke would be rebuilt for offers nobody can buy.
        if (!listing.IsLive)
        {
            return;
        }

        outbox.Enqueue(new ListingUpdated(
            listing.Id,
            listing.VendorId!.Value,
            listing.VariantId,
            listing.SellingPrice.Amount,
            listing.Mrp.Amount));
    }

    /// <summary>An offer left the storefront.</summary>
    /// <param name="listing">The offer.</param>
    /// <param name="reason">Why.</param>
    public void Deactivated(Listing listing, string? reason)
    {
        ArgumentNullException.ThrowIfNull(listing);

        outbox.Enqueue(new ListingDeactivated(
            listing.Id,
            listing.VendorId!.Value,
            listing.VariantId,
            reason));
    }
}
