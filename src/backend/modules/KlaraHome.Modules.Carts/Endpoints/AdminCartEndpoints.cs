using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Carts.Application.Carts;
using KlaraHome.Modules.Carts.Application.Checkout;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Carts.Endpoints;

/// <summary>
/// What an operator can see of a basket (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// <para>
/// Read-only but for one verb. Support diagnoses a basket, merchandising reads the abandoned
/// worklist, and neither of them may edit what a shopper is about to buy — there is no endpoint
/// that adds, removes or reprices a line on somebody's behalf, and the single write retires a
/// basket rather than changing one.
/// </para>
/// <para>
/// Not vendor-scoped, and it must not become so. A multi-vendor basket belongs to none of the
/// sellers in it, and letting a seller read one would let them read what a shopper is buying from
/// their competitors.
/// </para>
/// </remarks>
internal static class AdminCartEndpoints
{
    /// <summary>Maps the basket surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminCartEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var group = admin.MapGroup("/carts").WithTags("Cart");

        group.MapGet("/", async (
                string? status,
                Guid? customerId,
                bool? hasCoupon,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListCartsQuery(status, customerId, hasCoupon, cursor, size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListCarts")
            .WithSummary("Lists baskets, newest first.")
            .RequirePermission(CartsPermissions.CartRead)
            .Produces<PagedResult<AdminCartSummary>>();

        group.MapGet("/abandoned", async (
                DateTimeOffset? since,
                decimal? minValue,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListAbandonedCartsQuery(since, minValue, cursor, size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListAbandonedCarts")
            .WithSummary("The abandoned-cart worklist, most valuable first.")
            .RequirePermission(CartsPermissions.CartRead)
            .Produces<PagedResult<AdminCartSummary>>();

        group.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetAdminCartQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetCart")
            .WithSummary("One basket in full: lines, seller groups and the live quote.")
            .RequirePermission(CartsPermissions.CartRead)
            .Produces<CartResponse>();

        group.MapPost("/{id:guid}/expire", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new ExpireCartCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminExpireCart")
            .WithSummary("Retires a basket. Audited, because it is a change to a shopper's own data.")
            .RequirePermission(CartsPermissions.CartManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces(StatusCodes.Status204NoContent);

        admin.MapGet("/checkout-sessions/{id:guid}", async (
                Guid id,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetAdminCheckoutQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithTags("Checkout")
            .WithName("adminGetCheckoutSession")
            .WithSummary("One checkout session, its address snapshots and its place-order attempts.")
            .RequirePermission(CartsPermissions.CartRead)
            .Produces<CheckoutResponse>();

        return admin;
    }
}
