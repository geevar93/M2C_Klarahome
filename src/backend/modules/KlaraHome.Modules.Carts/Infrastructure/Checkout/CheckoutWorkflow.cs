using System.Globalization;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Modules.Carts.Application.Carts;
using KlaraHome.Modules.Carts.Application.Checkout;
using KlaraHome.Modules.Carts.Domain;
using KlaraHome.Modules.Carts.Infrastructure.Carts;
using KlaraHome.Modules.Carts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Carts.Infrastructure.Checkout;

/// <summary>
/// The steps every checkout handler shares: load the session, re-price the basket against what has
/// been chosen so far, and project the pair into one response.
/// </summary>
/// <remarks>
/// <para>
/// Every handler re-prices. That is not redundancy: an address changes the tax split, a delivery
/// choice changes the total, and a payment method changes both the fee and which promotions apply,
/// so a screen that showed the previous step's figures would be showing a price the shopper is not
/// going to be charged.
/// </para>
/// <para>
/// The basket is rendered by the same <see cref="CartRenderer"/> the cart page uses. One renderer
/// is what stops the review screen and the basket disagreeing about whether an item is in stock.
/// </para>
/// </remarks>
/// <param name="context">The Cart data context.</param>
/// <param name="renderer">Prices and validates the basket.</param>
internal sealed class CheckoutWorkflow(CartsDbContext context, CartRenderer renderer)
{
    /// <summary>Loads one of this shopper's sessions, tracked, with its delivery choices.</summary>
    /// <param name="sessionId">The session.</param>
    /// <param name="customerId">The shopper, which is half the lookup key and all of the authorisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<CheckoutSession?> FindAsync(
        Guid sessionId,
        Guid customerId,
        CancellationToken cancellationToken)
        => context.CheckoutSessions
            .Include(session => session.Shipments)
            .FirstOrDefaultAsync(
                session => session.Id == sessionId && session.CustomerId == customerId,
                cancellationToken);

    /// <summary>Loads the basket a session is paying for, with its lines.</summary>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<Cart?> FindCartAsync(CheckoutSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        return context.Carts
            .Include(cart => cart.Lines)
            .FirstOrDefaultAsync(cart => cart.Id == session.CartId, cancellationToken);
    }

    /// <summary>What the renderer needs to know, from what the shopper has chosen so far.</summary>
    /// <param name="session">The session.</param>
    public static CartRenderContext ContextFor(CheckoutSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new CartRenderContext(
            session.PlaceOfSupplyStateId,
            session.ShippingAddress?.Pincode,
            session.PaymentMethod == CheckoutPaymentMethod.CashOnDelivery
                ? QuotePaymentMethod.CashOnDelivery
                : QuotePaymentMethod.Prepaid,
            session.Shipments.ToDictionary(shipment => shipment.VendorId, shipment => shipment.Amount));
    }

    /// <summary>Re-prices the basket against the session, and snapshots the result onto it.</summary>
    /// <remarks>
    /// The snapshot is taken on every step rather than only at review, so a session that is paid for
    /// straight after a step carries the figures that step produced. Orders copies this snapshot and
    /// never re-prices, which is what makes the confirmation the same number the shopper agreed to.
    /// </remarks>
    /// <param name="session">The session.</param>
    /// <param name="cart">The basket.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CartResponse> RepriceAsync(
        CheckoutSession session,
        Cart cart,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var rendered = await renderer
            .RenderAsync(cart, ContextFor(session), cancellationToken)
            .ConfigureAwait(false);

        if (rendered.Quote is { } quote)
        {
            session.Snapshot(quote);
        }

        return rendered;
    }

    /// <summary>
    /// Prices the basket as though it were being paid for at the door, without changing the
    /// session.
    /// </summary>
    /// <remarks>
    /// Used only to answer "what would cash on delivery cost me". The figure is read off a quote
    /// rather than off the settings section that holds the fee, because this module computes no
    /// money — so the number shown next to the radio button is the number that will be charged, by
    /// construction rather than by two pieces of code agreeing.
    /// </remarks>
    /// <param name="session">The session, for the address and the delivery choices.</param>
    /// <param name="cart">The basket.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<QuoteResult?> QuoteAsCodAsync(
        CheckoutSession session,
        Cart cart,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var rendered = await renderer
            .RenderAsync(
                cart,
                ContextFor(session) with { PaymentMethod = QuotePaymentMethod.CashOnDelivery },
                cancellationToken)
            .ConfigureAwait(false);

        return rendered.Quote;
    }

    /// <summary>Projects a session and its freshly priced basket into one response.</summary>
    /// <param name="session">The session.</param>
    /// <param name="cart">The priced basket.</param>
    public static CheckoutResponse ToResponse(CheckoutSession session, CartResponse cart)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(cart);

        var chosen = session.Shipments.ToDictionary(shipment => shipment.VendorId);

        return new CheckoutResponse(
            session.Id,
            session.CartId,
            session.Status.ToString(),
            session.CurrencyCode,
            ToAddress(session.ShippingAddress),
            ToAddress(session.BillingAddress),
            session.Gstin,
            session.PaymentMethod == CheckoutPaymentMethod.CashOnDelivery ? "cod" : "prepaid",
            [
                .. cart.Groups.Select(group =>
                {
                    var shipment = chosen.GetValueOrDefault(group.VendorId);

                    return new VendorShippingOptionsResponse(
                        group.VendorId,
                        group.VendorName,
                        shipment?.OptionCode,
                        shipment is null
                            ? []
                            : [
                                new ShippingOptionResponse(
                                    shipment.OptionCode,
                                    shipment.ServiceName,
                                    shipment.Carrier,
                                    shipment.Amount,
                                    shipment.DispatchSlaHours,
                                    shipment.PromisedMinDays,
                                    shipment.PromisedMaxDays,
                                    IsCodAvailable: true),
                            ]);
                }),
            ],
            cart,
            session.ExpiresAt,
            session.OrderNumber);
    }

    /// <summary>Projects a stored snapshot for the API.</summary>
    private static CheckoutAddressResponse? ToAddress(AddressSnapshot? address)
        => address is null
            ? null
            : new CheckoutAddressResponse(
                address.SourceAddressId,
                address.RecipientName,
                address.Mobile,
                address.Line1,
                address.Line2,
                address.Landmark,
                address.City,
                address.StateId,
                address.Pincode,
                address.Gstin,
                address.IsBusiness);

    /// <summary>
    /// Whether cash on delivery may be chosen for this basket, and why not when it may not.
    /// </summary>
    /// <remarks>
    /// Four independent rules, all of which have to hold, and the shopper is told which one failed:
    /// the store has to offer COD at all, the order has to be under the value ceiling the business
    /// set, every item has to be one its seller will accept cash for, and every chosen delivery
    /// service has to be one the courier will collect on. "Not available" without the reason is the
    /// answer that generates a support call.
    /// </remarks>
    /// <param name="cart">The priced basket.</param>
    /// <param name="session">The session, for the chosen delivery services.</param>
    /// <param name="commerce">The store's commerce settings.</param>
    public static string? CodRefusalReason(CartResponse cart, CheckoutSession session, CommerceSettings commerce)
    {
        ArgumentNullException.ThrowIfNull(cart);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(commerce);

        if (!commerce.CodEnabled)
        {
            return "This store does not offer cash on delivery.";
        }

        if (cart.Lines.FirstOrDefault(line => !line.IsCodAllowed) is { } refused)
        {
            return $"{refused.Name} cannot be paid for at the door.";
        }

        var total = cart.Quote?.GrandTotal ?? 0m;

        if (commerce.CodOrderValueLimit > 0m && total > commerce.CodOrderValueLimit)
        {
            var ceiling = commerce.CodOrderValueLimit.ToString("0", CultureInfo.InvariantCulture);

            return $"Orders over {ceiling} cannot be paid for at the door.";
        }

        return null;
    }
}
