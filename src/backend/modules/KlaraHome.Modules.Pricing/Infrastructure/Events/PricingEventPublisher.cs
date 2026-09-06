using KlaraHome.Contracts.Pricing;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Pricing.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Pricing.Infrastructure.Events;

/// <summary>
/// Announces what happened to a price or a promotion (docs/02-domain-model.md §6).
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
/// before (ADR-003) — which matters particularly in this module: a price-drop alert published for a
/// price that was then rolled back would email every subscriber about a sale that never opened.
/// </para>
/// </remarks>
/// <param name="outbox">This module's outbox, keyed by its context.</param>
internal sealed class PricingEventPublisher(
    [FromKeyedServices(typeof(PricingDbContext))] IOutbox outbox)
{
    /// <summary>An offer's selling price moved.</summary>
    /// <param name="listingId">The offer.</param>
    /// <param name="vendorId">The seller behind it.</param>
    /// <param name="previousPrice">What it was, or null when it had no list price before.</param>
    /// <param name="newPrice">What it is now.</param>
    /// <param name="currencyCode">ISO 4217 code both amounts are in.</param>
    /// <param name="priceListId">The list that changed.</param>
    public void PriceChanged(
        Guid listingId,
        Guid vendorId,
        decimal? previousPrice,
        decimal newPrice,
        string currencyCode,
        Guid? priceListId)
        => outbox.Enqueue(new PriceChanged(listingId, vendorId, previousPrice, newPrice, currencyCode, priceListId));

    /// <summary>A promotion was used on an order.</summary>
    /// <param name="promotionId">The promotion.</param>
    /// <param name="code">Its code at the time.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="customerId">The shopper.</param>
    /// <param name="discountAmount">What it took off.</param>
    public void PromotionRedeemed(
        Guid promotionId,
        string? code,
        Guid orderId,
        Guid? customerId,
        decimal discountAmount)
        => outbox.Enqueue(new PromotionRedeemed(promotionId, code, orderId, customerId, discountAmount));
}
