using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Infrastructure.Authorization;

/// <summary>
/// Declares the permission an endpoint requires. Kept as endpoint metadata alongside the policy
/// it produces, because the declaration is what the authorisation matrix test enumerates: a policy
/// alone is not readable from outside the request that evaluates it.
/// </summary>
/// <remarks>
/// The metadata is not decoration. <see cref="UnsecuredEndpointGuard"/> refuses to start a
/// non-Development host in which any endpoint carries it while no authentication scheme is
/// registered, so the window between "the endpoint exists" and "the endpoint is protected" cannot
/// be closed by forgetting about it.
/// </remarks>
/// <param name="Permission">The granular permission, as a dotted noun-verb: <c>platform.settings.manage</c>.</param>
public sealed record RequiredPermissionMetadata(string Permission);

/// <summary>Endpoint-builder helpers for the permission convention.</summary>
public static class PermissionEndpoints
{
    /// <summary>
    /// Requires the permission, and records it so it can be enumerated. Both, in one call:
    /// declaring a permission that nothing checks is the failure mode this convention exists to
    /// prevent.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint or group being built.</param>
    /// <param name="permission">The granular permission, for example <c>platform.settings.manage</c>.</param>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        builder.WithMetadata(new RequiredPermissionMetadata(permission));
        builder.RequireAuthorization(PermissionPolicy.NameFor(permission));

        return builder;
    }

    /// <summary>Registers the guard that refuses to serve permissioned endpoints without auth.</summary>
    /// <param name="services">The container.</param>
    public static IServiceCollection AddPermissionGuard(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService<UnsecuredEndpointGuard>();
        return services;
    }

    /// <summary>
    /// Registers permission-based authorisation: the dynamic policy provider, the handler that
    /// checks the claim, and the caller context handlers read the request's identity from.
    /// </summary>
    /// <remarks>
    /// The authentication <em>scheme</em> is not registered here. Which credentials this system
    /// accepts, and the keys that validate them, belong to the Identity module; the shared layer
    /// only knows what a permission is and how an endpoint asks for one.
    /// </remarks>
    /// <param name="services">The container.</param>
    /// <param name="httpContextAvailable">
    /// Whether the host serves HTTP. A background host has no principal, and its caller context
    /// says so rather than reaching for an <c>HttpContext</c> that will never exist.
    /// </param>
    public static IServiceCollection AddKlaraHomeAuthorization(
        this IServiceCollection services,
        bool httpContextAvailable = true)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthorization();

        // Replace, not TryAdd. AddAuthorization registers the framework's provider with a TryAdd of
        // its own, so a TryAdd here would silently lose and every perm: policy would come back
        // "not found" at the first request rather than at startup.
        services.Replace(ServiceDescriptor.Singleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>());

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAuthorizationHandler, PermissionAuthorizationHandler>());

        return services.AddKlaraHomeAuthorizationContext(httpContextAvailable);
    }

    /// <summary>
    /// Registers only <see cref="ICallerContext"/>. Split out because the vendor query filter
    /// needs it in hosts that never register a policy or serve a request.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="httpContextAvailable">Whether the host serves HTTP.</param>
    public static IServiceCollection AddKlaraHomeAuthorizationContext(
        this IServiceCollection services,
        bool httpContextAvailable)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (httpContextAvailable)
        {
            services.AddHttpContextAccessor();
            services.TryAddScoped<ICallerContext, ClaimsCallerContext>();
        }
        else
        {
            services.TryAddSingleton<ICallerContext, SystemCallerContext>();
        }

        return services;
    }
}

/// <summary>
/// Fails the host at startup if an endpoint declares a required permission while nothing is able
/// to authenticate a caller.
/// </summary>
/// <remarks>
/// <para>
/// Between Step 6, which builds the admin surface, and Step 7, which builds authentication, the
/// admin endpoints exist and cannot yet be protected. The honest way to hold that gap is to make
/// it impossible to deploy: Development runs with a loud warning on every start, and anything else
/// refuses to boot.
/// </para>
/// <para>
/// The check is on the presence of an authentication scheme rather than on a policy, because that
/// is the thing whose absence makes the endpoints open. Once Step 7 calls
/// <c>AddAuthentication</c> and attaches the policies, this guard goes quiet on its own.
/// </para>
/// </remarks>
/// <param name="endpoints">Every mapped endpoint, resolved after routing has been built.</param>
/// <param name="schemes">The registered authentication schemes, or null when there are none.</param>
/// <param name="environment">Decides whether an unprotected surface is a warning or a refusal.</param>
/// <param name="logger">Reports the gap on every Development start.</param>
internal sealed partial class UnsecuredEndpointGuard(
    EndpointDataSource endpoints,
    IHostEnvironment environment,
    ILogger<UnsecuredEndpointGuard> logger,
    IAuthenticationSchemeProvider? schemes = null) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var permissioned = endpoints.Endpoints
            .Select(endpoint => endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>())
            .Count(metadata => metadata is not null);

        if (permissioned == 0)
        {
            return;
        }

        var registered = schemes is null
            ? 0
            : (await schemes.GetAllSchemesAsync().ConfigureAwait(false)).Count();

        if (registered > 0)
        {
            return;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"{permissioned} endpoint(s) declare a required permission, but no authentication scheme is "
                + "registered, so every one of them is open. The Identity module (Step 7) registers the scheme "
                + "and the policies that enforce these permissions; until it does, this host may only run in "
                + "Development.");
        }

        PermissionedEndpointsAreOpen(logger, permissioned);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 1300, Level = LogLevel.Warning,
        Message = "{PermissionedEndpointCount} endpoint(s) declare a required permission but no authentication "
                  + "scheme is registered. They are OPEN. This is expected until Step 7 and is refused outside "
                  + "Development.")]
    private static partial void PermissionedEndpointsAreOpen(ILogger logger, int permissionedEndpointCount);
}
