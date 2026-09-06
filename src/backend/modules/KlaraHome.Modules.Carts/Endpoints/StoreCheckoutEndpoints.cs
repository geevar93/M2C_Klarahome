using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Carts.Application;
using KlaraHome.Modules.Carts.Application.Checkout;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Carts.Endpoints;

/// <summary>The body of an address choice.</summary>
/// <param name="ShippingAddressId">One of the shopper's own addresses.</param>
/// <param name="BillingAddressId">Another, or null to bill to the shipping address.</param>
/// <param name="Gstin">The GSTIN to raise the invoice against, for a B2B purchase.</param>
internal sealed record CheckoutAddressBody(Guid ShippingAddressId, Guid? BillingAddressId, string? Gstin);

/// <summary>One seller's chosen delivery service, as the API states it.</summary>
/// <param name="VendorId">The seller.</param>
/// <param name="OptionCode">The service.</param>
internal sealed record VendorShippingChoiceBody(Guid VendorId, string OptionCode);

/// <summary>The body of a delivery choice.</summary>
/// <param name="PerVendor">One choice per seller in the basket.</param>
internal sealed record CheckoutShippingBody(IReadOnlyList<VendorShippingChoiceBody> PerVendor);

/// <summary>The body of a payment-method choice.</summary>
/// <param name="Method">Either <c>prepaid</c> or <c>cod</c>.</param>
internal sealed record CheckoutPaymentMethodBody(string Method);

/// <summary>
/// The path from a basket to a payment (docs/04-api-specification.md §3.3).
/// </summary>
/// <remarks>
/// <para>
/// Every route requires an account. An order needs somebody to send it to, somebody to answer for
/// it and somewhere to send the invoice, and guest checkout would mean building a second identity
/// for people the platform already has a way to identify (<c>CartsOptions.RequireSignInToCheckout</c>).
/// </para>
/// <para>
/// The session id is in the path, and it is safe there because every handler looks a session up by
/// <em>(session, customer)</em>: an id belonging to somebody else does not resolve, and the answer
/// is the same 404 a made-up id gets.
/// </para>
/// <para>
/// <c>place-order</c> is rate-limited separately and far more tightly than the rest. It is the one
/// route on the platform that reserves stock and creates an order, and it is the one worth spending
/// a dedicated limiter on.
/// </para>
/// </remarks>
internal static class StoreCheckoutEndpoints
{
    /// <summary>The header an idempotency key travels in.</summary>
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>Maps the checkout surface beneath <c>/store/checkout</c>.</summary>
    /// <param name="store">The <c>/store</c> group.</param>
    public static IEndpointRouteBuilder MapStoreCheckoutEndpoints(this IEndpointRouteBuilder store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var group = store
            .MapGroup("/checkout")
            .WithTags("Checkout")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.CartWrite);

        group.MapPost("/", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new StartCheckoutCommand(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeStartCheckout")
            .WithSummary("Opens a checkout against the caller's basket, refusing one that is not ready.")
            .Produces<CheckoutResponse>();

        group.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetCheckoutQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetCheckout")
            .WithSummary("The session as it stands, with the basket re-priced against it.")
            .Produces<CheckoutResponse>();

        group.MapPut("/{id:guid}/address", async (
                Guid id,
                CheckoutAddressBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new SetCheckoutAddressCommand(
                    id,
                    body.ShippingAddressId,
                    body.BillingAddressId,
                    body.Gstin);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeSetCheckoutAddress")
            .WithSummary("Chooses where the order goes and who it is billed to, freezing both onto the session.")
            .Produces<CheckoutResponse>();

        group.MapGet("/{id:guid}/shipping-options", async (
                Guid id,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetShippingOptionsQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeCheckoutShippingOptions")
            .WithSummary("The delivery services available for each seller in the basket, with the promised window.")
            .Produces<IReadOnlyList<VendorShippingOptionsResponse>>();

        group.MapPut("/{id:guid}/shipping", async (
                Guid id,
                CheckoutShippingBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new SetCheckoutShippingCommand(
                    id,
                    [.. body.PerVendor.Select(choice => new VendorShippingChoice(choice.VendorId, choice.OptionCode))]);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeSetCheckoutShipping")
            .WithSummary("Chooses a delivery service for every seller in the basket.")
            .Produces<CheckoutResponse>();

        group.MapGet("/{id:guid}/payment-methods", async (
                Guid id,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetPaymentMethodsQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeCheckoutPaymentMethods")
            .WithSummary("What this basket may be paid by, with the reason when cash on delivery may not be used.")
            .Produces<IReadOnlyList<PaymentMethodResponse>>();

        group.MapPut("/{id:guid}/payment-method", async (
                Guid id,
                CheckoutPaymentMethodBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SetPaymentMethodCommand(id, body.Method), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeSetCheckoutPaymentMethod")
            .WithSummary("Chooses how the order is paid for, checking cash-on-delivery eligibility first.")
            .Produces<CheckoutResponse>();

        group.MapGet("/{id:guid}/review", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ReviewCheckoutQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeReviewCheckout")
            .WithSummary("Re-validates and re-prices the session, immediately before payment.")
            .Produces<CheckoutResponse>();

        group.MapPost("/{id:guid}/abandon", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new AbandonCheckoutCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("storeAbandonCheckout")
            .WithSummary("Closes a session the shopper backed out of. The basket is left alone.")
            .Produces(StatusCodes.Status204NoContent);

        store.MapPost("/checkout/{id:guid}/place-order", async (
                Guid id,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                // Required, not optional. This is the one request on the platform that reserves
                // stock and creates an order, and without a key a retried request is a second order.
                if (!context.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var header)
                    || header.ToString() is not { Length: > 0 } key)
                {
                    return CartsErrors.IdempotencyKeyRequired.ToProblemResult(context);
                }

                var command = new PlaceOrderCommand(id, key, Channel: "web");
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithTags("Checkout")
            .WithName("storePlaceOrder")
            .WithSummary("Reserves the stock and creates the order. Idempotent on the Idempotency-Key header.")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.PlaceOrder)
            .Produces<PlaceOrderResponse>();

        return store;
    }
}
