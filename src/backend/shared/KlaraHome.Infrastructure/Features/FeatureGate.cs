using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Infrastructure.Features;

/// <summary>Records the feature flag an endpoint is gated by, so the set can be enumerated.</summary>
/// <remarks>
/// The same reason <c>RequiredPermissionMetadata</c> exists alongside its policy: a filter cannot
/// be read from outside the request that runs it, and "which endpoints does this flag control"
/// is a question an operator about to throw the switch needs answered.
/// </remarks>
/// <param name="Flag">The flag key, for example <c>identity.mobile-otp-login</c>.</param>
public sealed record RequiredFeatureMetadata(string Flag);

/// <summary>Endpoint-builder helpers for gating a route on a feature flag.</summary>
public static class FeatureGate
{
    /// <summary>
    /// Refuses the endpoint while the flag is off, and records which flag that is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The refusal is <b>404</b>, not 403. A feature that is switched off has no resource to talk
    /// about, and 403 would tell an anonymous caller that the endpoint exists and is merely closed
    /// to them — the same leak <c>04-api-specification.md</c> §1.2 forbids for a resource they may
    /// not see. It is also what the Platform module already answers, so a client sees one shape.
    /// </para>
    /// <para>
    /// The check runs per request rather than at startup, because the point of a flag is that an
    /// operator can throw it without a deploy.
    /// </para>
    /// </remarks>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint or group being built.</param>
    /// <param name="flag">The flag key.</param>
    public static TBuilder RequireFeature<TBuilder>(this TBuilder builder, string flag)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(flag);

        builder.WithMetadata(new RequiredFeatureMetadata(flag));

        builder.Add(endpoint => endpoint.FilterFactories.Add((_, next) => async context =>
        {
            var flags = context.HttpContext.RequestServices.GetRequiredService<IFeatureFlags>();
            var caller = context.HttpContext.RequestServices.GetService<ICallerContext>();

            var audience = new FeatureAudience(caller?.UserId, caller?.Segment);

            var enabled = await flags
                .IsEnabledAsync(flag, audience, context.HttpContext.RequestAborted)
                .ConfigureAwait(false);

            return enabled
                ? await next(context).ConfigureAwait(false)
                : Disabled(context.HttpContext, flag);
        }));

        return builder;
    }

    /// <summary>The response for a feature that is switched off.</summary>
    /// <param name="context">The request, for the instance path and correlation id.</param>
    /// <param name="flag">The flag key, named so an operator reading a support ticket knows which switch.</param>
    public static IResult Disabled(HttpContext context, string flag)
        => Error
            .NotFound("FEATURE_DISABLED", $"The feature '{flag}' is not enabled on this store.")
            .ToProblemResult(context);
}
