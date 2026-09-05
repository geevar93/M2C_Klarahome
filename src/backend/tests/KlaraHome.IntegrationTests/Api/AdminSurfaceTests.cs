using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Modules.Identity.Infrastructure.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Api;

/// <summary>
/// The rules that decide what protects each admin endpoint, asserted against the routes that are
/// actually mapped rather than against a document.
/// </summary>
/// <remarks>
/// <para>
/// The admin surface has exactly three kinds of route, and each is protected differently:
/// </para>
/// <list type="bullet">
/// <item><c>/admin/auth/*</c> is anonymous — it is how a caller stops being anonymous.</item>
/// <item><c>/admin/me/*</c> requires authentication and no permission — the resource is the caller,
/// and the scope comes from the token rather than from a path parameter.</item>
/// <item>Everything else requires a named permission.</item>
/// </list>
/// <para>
/// Before Step 7 these tests held a gap open: the endpoints declared permissions nothing enforced.
/// They now assert the enforcement itself, and the third rule is what stops a new admin endpoint
/// from being added with no permission at all.
/// </para>
/// </remarks>
public sealed class AdminSurfaceTests(KlaraHomeApiFactory factory) : IClassFixture<KlaraHomeApiFactory>
{
    private const string AdminPrefix = "/api/v1/admin";
    private const string AuthPrefix = "/api/v1/admin/auth";
    private const string SelfPrefix = "/api/v1/admin/me";

    [Fact]
    public void Every_admin_endpoint_that_is_not_auth_or_self_declares_a_permission()
    {
        var unprotected = AdminEndpoints()
            .Where(endpoint => !IsUnder(endpoint, AuthPrefix) && !IsUnder(endpoint, SelfPrefix))
            .Where(endpoint => endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>() is null)
            .Select(Describe)
            .ToList();

        Assert.True(
            unprotected.Count == 0,
            "Every endpoint under /api/v1/admin outside /auth and /me must declare a required permission "
            + $"with RequirePermission(...). These do not: {string.Join(", ", unprotected)}");
    }

    [Fact]
    public void A_declared_permission_is_a_dotted_lowercase_name()
    {
        var permissions = AdminEndpoints()
            .Select(endpoint => endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>()?.Permission)
            .Where(permission => permission is not null)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(permissions);

        Assert.All(permissions, permission =>
        {
            Assert.Equal(permission!.ToLowerInvariant(), permission);
            Assert.Contains(".", permission, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Every_declared_permission_exists_in_the_catalogue()
    {
        // A permission an endpoint asks for but the catalogue does not declare cannot be granted by
        // any role, so the endpoint is unreachable by everybody — a lockout that no other test
        // notices, because a 403 looks the same either way.
        var catalogue = PermissionCatalogueCodes();

        var undeclared = AdminEndpoints()
            .Select(endpoint => endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>()?.Permission)
            .Where(permission => permission is not null && !catalogue.Contains(permission))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            undeclared.Count == 0,
            "These permissions are required by an endpoint but are not in PermissionCatalogue, so no role "
            + $"can ever grant them: {string.Join(", ", undeclared)}");
    }

    [Fact]
    public void A_permissioned_endpoint_also_carries_the_policy_that_enforces_it()
    {
        // The metadata and the policy are attached by one call, and this is what keeps it that way:
        // a declaration without a policy is documentation, not authorisation.
        var declared = AdminEndpoints()
            .Where(endpoint => endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>() is not null)
            .ToList();

        Assert.NotEmpty(declared);

        Assert.All(declared, endpoint =>
        {
            var permission = endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>()!.Permission;

            var policies = endpoint.Metadata
                .GetOrderedMetadata<IAuthorizeData>()
                .Select(authorize => authorize.Policy)
                .ToList();

            Assert.Contains(PermissionPolicy.NameFor(permission), policies);
        });
    }

    [Fact]
    public void The_self_service_surface_requires_authentication_and_nothing_more()
    {
        var self = AdminEndpoints().Where(endpoint => IsUnder(endpoint, SelfPrefix)).ToList();

        Assert.NotEmpty(self);

        Assert.All(self, endpoint =>
        {
            Assert.Null(endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>());
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        });
    }

    [Fact]
    public void The_sign_in_surface_is_explicitly_anonymous()
    {
        var auth = AdminEndpoints().Where(endpoint => IsUnder(endpoint, AuthPrefix)).ToList();

        Assert.NotEmpty(auth);
        Assert.All(auth, endpoint => Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>()));
    }

    [Fact]
    public void No_endpoint_anywhere_is_left_undeclared()
    {
        // Deny-by-default means an endpoint that says nothing is closed rather than open, so this
        // could never be a security hole. It would be a 401 nobody expected, discovered by a user;
        // saying it out loud is how an endpoint declares which of the two it meant to be.
        var silent = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Where(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
            .Select(Describe)
            .ToList();

        Assert.True(
            silent.Count == 0,
            "Every endpoint must say whether it is anonymous or requires authorisation. These say neither: "
            + string.Join(", ", silent));
    }

    [Fact]
    public void No_anonymous_endpoint_claims_to_need_a_permission()
    {
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

    /// <summary>The catalogue codes. Visible here through the module's InternalsVisibleTo.</summary>
    private static HashSet<string> PermissionCatalogueCodes()
        => [.. PermissionCatalog.All.Select(permission => permission.Code)];

    private IEnumerable<RouteEndpoint> AdminEndpoints()
        => factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => IsUnder(endpoint, AdminPrefix));

    private static bool IsUnder(RouteEndpoint endpoint, string prefix)
        => endpoint.RoutePattern.RawText?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true;

    private static string Describe(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
        return $"{string.Join('|', methods)} {endpoint.RoutePattern.RawText}";
    }
}
