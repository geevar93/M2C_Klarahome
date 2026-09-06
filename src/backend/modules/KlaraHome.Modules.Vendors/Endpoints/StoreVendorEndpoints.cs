using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Vendors.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Vendors.Endpoints;

/// <summary>
/// The seller's public page (docs/04-api-specification.md §3).
/// </summary>
/// <remarks>
/// One route, and deliberately thin: a shopper looking at a marketplace listing wants to know who
/// they are buying from, how fast that seller dispatches, and what happens if they send it back.
/// Nothing here is scoped to a caller, so it is anonymous — and everything it returns is either
/// public already or the seller's own published promise.
/// </remarks>
internal static class StoreVendorEndpoints
{
    /// <summary>Maps the storefront seller surface beneath <c>/store</c>.</summary>
    /// <param name="store">The <c>/store</c> group.</param>
    public static IEndpointRouteBuilder MapStoreVendorEndpoints(this IEndpointRouteBuilder store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var vendors = store
            .MapGroup("/vendors")
            .WithTags("Vendors")
            .RequireRateLimiting(RateLimitPolicies.StorefrontRead);

        vendors.MapGet("/{slug}", async (string slug, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetStorefrontVendorQuery(slug), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeVendorGet")
            .WithSummary("A seller's public profile. A seller who is not trading answers 404, whatever "
                         + "the reason — a suspension is between the platform and the seller.")
            .AllowAnonymous()
            .Produces<StorefrontVendorResponse>();

        return store;
    }
}
