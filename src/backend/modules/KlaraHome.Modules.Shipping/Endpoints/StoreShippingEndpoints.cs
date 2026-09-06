using KlaraHome.Infrastructure.Caching;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Shipping.Application;
using KlaraHome.Modules.Shipping.Application.Operations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Shipping.Endpoints;

/// <summary>
/// What a shopper can ask about delivery (docs/04-api-specification.md §3.7).
/// </summary>
/// <remarks>
/// <para>
/// One route, and it is anonymous. "Do you deliver to my PIN code" has to be answerable on a product
/// page before anybody has an account, and the answer reveals nothing about any person — it is a
/// fact about a postcode.
/// </para>
/// <para>
/// It reads the cache and never a courier, which is the rule the whole serviceability design exists
/// to enforce (docs/08-integrations.md §2). It is cached at the edge as well, because the answer
/// changes about as often as India's postal geography does.
/// </para>
/// <para>
/// There is deliberately no tracking route here. A shopper tracks their parcel through their order,
/// where the timeline already carries every courier scan; a second tracking screen reading this
/// schema would be a second answer to the same question, and the two would drift.
/// </para>
/// </remarks>
internal static class StoreShippingEndpoints
{
    /// <summary>Maps the delivery surface beneath <c>/store/shipping</c>.</summary>
    /// <param name="store">The <c>/store</c> group.</param>
    public static IEndpointRouteBuilder MapStoreShippingEndpoints(this IEndpointRouteBuilder store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var group = store
            .MapGroup("/shipping")
            .WithTags("Shipping")
            .AllowAnonymous();

        // The route constraint turns a malformed code into a routing 404 before a handler or a query
        // parameter ever sees it — the same shape the platform's own PIN-code lookup uses.
        group.MapGet("/serviceability/{pincode:regex(^[1-9][0-9]{{5}}$)}", async (
                string pincode,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetServiceabilityQuery(pincode), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeShippingServiceability")
            .WithSummary("Whether this store can deliver to a PIN code, and how quickly.")
            .RequireRateLimiting(RateLimitPolicies.StorefrontRead)
            .CachePublicRead()
            .Produces<ServiceabilityResponse>();

        return store;
    }
}
