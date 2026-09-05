using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Caching;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Platform.Application.Storefront;
using KlaraHome.Modules.Platform.Infrastructure.FeatureFlags;
using KlaraHome.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Platform.Endpoints;

/// <summary>
/// The anonymous storefront surface the Platform module owns: the store configuration document,
/// the jurisdiction list and PIN code lookup (docs/04-api-specification.md §3.6).
/// </summary>
/// <remarks>
/// Both configuration endpoints are gated by a feature flag, which is what makes the flag service
/// observable rather than merely present: turning <c>platform.public-store-config</c> off takes the
/// document off the internet on the next request, with no deploy and no restart.
/// </remarks>
internal static class StorePlatformEndpoints
{
    /// <summary>Maps the storefront surface beneath the versioned API group.</summary>
    /// <param name="endpoints">The versioned API group the module is handed.</param>
    public static IEndpointRouteBuilder MapStorePlatformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/store")
            .WithTags("Platform")
            .RequireRateLimiting(RateLimitPolicies.StorefrontRead);

        group.MapGet("/config", async (IDispatcher dispatcher, IFeatureFlags flags, HttpContext context) =>
            {
                if (!await flags.IsEnabledAsync(
                        PlatformFeatures.PublicStoreConfig,
                        cancellationToken: context.RequestAborted).ConfigureAwait(false))
                {
                    return Disabled(context, PlatformFeatures.PublicStoreConfig);
                }

                var result = await dispatcher
                    .QueryAsync(new GetStoreConfigQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeConfigGet")
            .WithSummary("Public store configuration: branding, legal and support details, commerce rules "
                         + "and the feature flags the storefront switches on.")
            .AllowAnonymous()
            .Produces<StoreConfigResponse>();

        group.MapGet("/states", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetStatesQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeStatesGet")
            .WithSummary("The Indian states and union territories, with their GST state codes.")
            .AllowAnonymous()
            // Reference data that changes when a state is created. Cached hard, deliberately.
            .CacheReferenceData()
            .Produces<IReadOnlyList<StateResponse>>();

        // The route constraint turns a malformed code into a routing 404 before a handler or a
        // query parameter ever sees it.
        group.MapGet("/pincodes/{pincode:regex(^[1-9][0-9]{{5}}$)}", async (
                string pincode,
                IDispatcher dispatcher,
                IFeatureFlags flags,
                HttpContext context) =>
            {
                if (!await flags.IsEnabledAsync(
                        PlatformFeatures.PincodeLookup,
                        cancellationToken: context.RequestAborted).ConfigureAwait(false))
                {
                    return Disabled(context, PlatformFeatures.PincodeLookup);
                }

                var result = await dispatcher
                    .QueryAsync(new GetPincodeQuery(pincode), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storePincodeGet")
            .WithSummary("City, district and state for a PIN code, for address autofill. Serviceability "
                         + "is added by the Shipping module.")
            .AllowAnonymous()
            .Produces<PincodeResponse>();

        return endpoints;
    }

    /// <summary>
    /// The response for a feature that is switched off: 404, not 403.
    /// </summary>
    /// <remarks>
    /// A disabled feature has no resource to talk about, and 403 would tell an anonymous caller
    /// that the endpoint exists and is merely closed to them — which is the same leak §1.2 forbids
    /// for a resource they may not see.
    /// </remarks>
    private static IResult Disabled(HttpContext context, string flagKey)
        => Error
            .NotFound("FEATURE_DISABLED", $"The feature '{flagKey}' is not enabled on this store.")
            .ToProblemResult(context);
}
