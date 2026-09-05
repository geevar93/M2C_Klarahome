using KlaraHome.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Api;

/// <summary>
/// The admin surface exists before the authorisation that protects it does. These tests hold that
/// gap open deliberately rather than letting it close by being forgotten.
/// </summary>
public sealed class AdminSurfaceTests(KlaraHomeApiFactory factory) : IClassFixture<KlaraHomeApiFactory>
{
    [Fact]
    public void Every_admin_endpoint_declares_the_permission_it_requires()
    {
        var unprotected = AdminEndpoints()
            .Where(endpoint => endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>() is null)
            .Select(Describe)
            .ToList();

        // Step 7 turns these declarations into policies. Until then this list is the specification
        // of what has to be protected, and it must never grow silently.
        Assert.True(
            unprotected.Count == 0,
            "Every endpoint under /api/v1/admin must declare a required permission with "
            + $"RequirePermission(...). These do not: {string.Join(", ", unprotected)}");
    }

    [Fact]
    public void A_declared_permission_is_a_dotted_lowercase_name()
    {
        var permissions = AdminEndpoints()
            .Select(endpoint => endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>()!.Permission)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(permissions);

        Assert.All(permissions, permission =>
        {
            Assert.Equal(permission.ToLowerInvariant(), permission);
            Assert.Contains(".", permission, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void No_anonymous_endpoint_claims_to_need_a_permission()
    {
        // A permission on an endpoint that also allows anonymous access is a contradiction, and it
        // is the shape a copy-pasted storefront endpoint would take.
        var contradictory = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>() is not null)
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Describe)
            .ToList();

        Assert.True(contradictory.Count == 0, string.Join(", ", contradictory));
    }

    private IEnumerable<RouteEndpoint> AdminEndpoints()
        => factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/api/v1/admin",
                StringComparison.OrdinalIgnoreCase) == true);

    private static string Describe(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
        return $"{string.Join('|', methods)} {endpoint.RoutePattern.RawText}";
    }
}
