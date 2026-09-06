using KlaraHome.Contracts.Carts;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Carts.Domain;
using KlaraHome.Modules.Carts.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Carts.Infrastructure.Events;

/// <summary>
/// Announces what happened to a basket (docs/02-domain-model.md §6).
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
/// before (ADR-003) — so a reminder is never sent about a basket that was, in the end, not
/// abandoned.
/// </para>
/// </remarks>
/// <param name="outbox">This module's outbox, keyed by its context.</param>
internal sealed class CartsEventPublisher(
    [FromKeyedServices(typeof(CartsDbContext))] IOutbox outbox)
{
    /// <summary>A basket was left long enough to be worth chasing.</summary>
    /// <param name="cart">The basket, already marked abandoned.</param>
    /// <param name="estimatedValue">What it was worth at the last price the cart saw.</param>
    public void Abandoned(Cart cart, decimal estimatedValue)
    {
        ArgumentNullException.ThrowIfNull(cart);

        outbox.Enqueue(new CartAbandoned(
            cart.Id,
            cart.CustomerId,
            cart.LineCount,
            estimatedValue,
            cart.CurrencyCode,
            cart.LastActivityAt));
    }

    /// <summary>A basket became an order.</summary>
    /// <param name="cart">The basket, already marked converted.</param>
    /// <param name="checkoutSessionId">The session that closed it.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="orderNumber">Its human-readable number.</param>
    /// <param name="grandTotal">What the shopper agreed to pay.</param>
    public void Converted(
        Cart cart,
        Guid checkoutSessionId,
        Guid orderId,
        string orderNumber,
        decimal grandTotal)
    {
        ArgumentNullException.ThrowIfNull(cart);

        outbox.Enqueue(new CartConverted(
            cart.Id,
            checkoutSessionId,
            cart.CustomerId ?? Guid.Empty,
            orderId,
            orderNumber,
            grandTotal,
            cart.CurrencyCode));
    }
}
