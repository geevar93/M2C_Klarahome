using KlaraHome.Contracts.Pricing;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Carts.Application.Carts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Carts.Endpoints;

/// <summary>The body of an add-to-cart request.</summary>
/// <param name="ListingId">The offer.</param>
/// <param name="Quantity">How many units to add. One when the caller does not say.</param>
internal sealed record AddCartItemBody(Guid ListingId, int? Quantity);

/// <summary>The body of a cart-line change.</summary>
/// <param name="Quantity">The new quantity, or null to leave it.</param>
/// <param name="SavedForLater">Whether to set it aside, or null to leave it.</param>
internal sealed record UpdateCartItemBody(int? Quantity, bool? SavedForLater);

/// <summary>The body of a coupon application.</summary>
/// <param name="Code">The code the shopper typed.</param>
internal sealed record CouponBody(string Code);

/// <summary>
/// The shopper's own basket (docs/04-api-specification.md §3.3).
/// </summary>
/// <remarks>
/// <para>
/// Every route here is anonymous, and none of them takes a cart id. The basket is found from the
/// access token when the caller has one and from the <c>kh_cart</c> cookie when they do not, so a
/// caller cannot name somebody else's — the authorisation is in the shape of the lookup rather than
/// in a check eight handlers each have to remember.
/// </para>
/// <para>
/// The writes are rate-limited under <c>cart-write</c> rather than as ordinary storefront reads.
/// Adding to a basket prices it, and pricing reads the catalogue, the price lists and every live
/// promotion; without a limit the add endpoint is the cheapest way to make the site slow for
/// everybody.
/// </para>
/// </remarks>
internal static class StoreCartEndpoints
{
    /// <summary>Maps the basket surface beneath <c>/store/cart</c>.</summary>
    /// <param name="store">The <c>/store</c> group.</param>
    public static IEndpointRouteBuilder MapStoreCartEndpoints(this IEndpointRouteBuilder store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var reads = store
            .MapGroup("/cart")
            .WithTags("Cart")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.StorefrontRead);

        reads.MapGet("/", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetCartQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetCart")
            .WithSummary("The caller's own basket: lines, seller groups, the itemised price, and every issue.")
            .Produces<CartResponse>();

        reads.MapGet("/quote", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetCartQuoteQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetCartQuote")
            .WithSummary("The itemised price of the caller's own basket, with the full GST breakdown.")
            .Produces<QuoteResult>();

        var writes = store
            .MapGroup("/cart")
            .WithTags("Cart")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.CartWrite);

        writes.MapPost("/items", async (AddCartItemBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new AddCartItemCommand(body.ListingId, body.Quantity ?? 1);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeAddCartItem")
            .WithSummary("Puts units of an offer into the basket, opening one if the caller has none.")
            .Produces<CartResponse>();

        writes.MapPatch("/items/{lineId:guid}", async (
                Guid lineId,
                UpdateCartItemBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateCartItemCommand(lineId, body.Quantity, body.SavedForLater);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeUpdateCartItem")
            .WithSummary("Changes a line's quantity, or sets it aside for later.")
            .Produces<CartResponse>();

        writes.MapDelete("/items/{lineId:guid}", async (
                Guid lineId,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new RemoveCartItemCommand(lineId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeRemoveCartItem")
            .WithSummary("Takes a line out of the basket.")
            .Produces<CartResponse>();

        writes.MapDelete("/", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new ClearCartCommand(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeClearCart")
            .WithSummary("Empties the basket, coupon included.")
            .Produces<CartResponse>();

        writes.MapPost("/merge", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new MergeCartCommand(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeMergeCart")
            .WithSummary("Folds the browser's basket into the signed-in shopper's own.")
            .RequireAuthorization()
            .Produces<CartResponse>();

        writes.MapPost("/coupon", async (CouponBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new ApplyCouponCommand(body.Code), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeApplyCoupon")
            .WithSummary("Records a coupon code. Whether it applies comes back on the quote, with the reason.")
            .Produces<CartResponse>();

        writes.MapDelete("/coupon", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new RemoveCouponCommand(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeRemoveCoupon")
            .WithSummary("Takes the coupon off the basket.")
            .Produces<CartResponse>();

        return store;
    }
}
