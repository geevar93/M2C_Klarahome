using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Identity.Application.Administration;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure.Seeding;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Identity.Endpoints;

/// <summary>The body of an account creation.</summary>
/// <param name="Email">Their email address, which is how they sign in.</param>
/// <param name="Mobile">An optional mobile number.</param>
/// <param name="UserType">Vendor staff or platform staff.</param>
/// <param name="RoleCodes">The roles to grant.</param>
/// <param name="VendorId">The seller a vendor user belongs to. Ignored for a vendor caller.</param>
internal sealed record CreateUserBody(
    string Email,
    string? Mobile,
    UserType UserType,
    IReadOnlyList<string> RoleCodes,
    Guid? VendorId);

/// <summary>The body of a role change.</summary>
/// <param name="RoleCodes">The roles the account should hold.</param>
internal sealed record SetUserRolesBody(IReadOnlyList<string> RoleCodes);

/// <summary>The body of a status change.</summary>
/// <param name="Status">The status the account should have.</param>
internal sealed record SetUserStatusBody(UserStatus Status);

/// <summary>The body of a role definition.</summary>
/// <param name="Code">The stable lowercase code.</param>
/// <param name="Name">The display name.</param>
/// <param name="Scope">Where it applies.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Permissions">What it grants.</param>
internal sealed record CreateRoleBody(
    string Code,
    string Name,
    RoleScope Scope,
    string Description,
    IReadOnlyList<string> Permissions);

/// <summary>The body of a role change.</summary>
/// <param name="Name">The display name.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Permissions">What it should grant.</param>
internal sealed record UpdateRoleBody(string Name, string Description, IReadOnlyList<string> Permissions);

/// <summary>Query-string filters for a user search.</summary>
/// <param name="Search">A fragment of an email address or mobile number.</param>
/// <param name="UserType">Restrict to one class of actor.</param>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record UserSearchFilter(string? Search, UserType? UserType, string? Cursor, int? Size);

/// <summary>
/// Managing user accounts and roles (docs/04-api-specification.md §4, Platform).
/// </summary>
/// <remarks>
/// Every route is vendor-scoped by the caller's token rather than by a path parameter: a vendor
/// owner manages their own organisation's staff through the same URLs a platform administrator
/// uses to manage everybody, and cannot address another seller's data at all
/// (docs/04-api-specification.md §2).
/// </remarks>
internal static class AdminIdentityEndpoints
{
    /// <summary>Maps the administrative identity surface.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminIdentityEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        MapUsers(admin);
        MapRoles(admin);

        return admin;
    }

    private static void MapUsers(IEndpointRouteBuilder admin)
    {
        var users = admin.MapGroup("/users").WithTags("Identity");

        users.MapGet("/", async (
                [AsParameters] UserSearchFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new SearchUsersQuery(filter.Search, filter.UserType, filter.Cursor, filter.Size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminUsersGet")
            .WithSummary("Lists user accounts, keyset-paginated. A vendor caller sees only their own organisation.")
            .RequirePermission(PermissionCatalog.IdentityUserRead)
            .Produces<PagedResult<AdminUserResponse>>();

        users.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetUserQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminUserGet")
            .WithSummary("Reads one account. Answers 404 for an account outside the caller's scope.")
            .RequirePermission(PermissionCatalog.IdentityUserRead)
            .Produces<AdminUserResponse>();

        users.MapPost("/", async (CreateUserBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreateUserCommand(
                    body.Email,
                    body.Mobile,
                    body.UserType,
                    body.RoleCodes,
                    body.VendorId);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminUserCreate")
            .WithSummary("Creates a staff or vendor account and emails them a link to set their password. "
                         + "No password is ever chosen on their behalf.")
            .RequirePermission(PermissionCatalog.IdentityUserManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<AdminUserResponse>();

        users.MapPut("/{id:guid}/roles", async (
                Guid id,
                SetUserRolesBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SetUserRolesCommand(id, body.RoleCodes), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminUserRolesPut")
            .WithSummary("Replaces an account's roles and signs it out, so the change takes effect at once.")
            .RequirePermission(PermissionCatalog.IdentityRoleAssign)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<AdminUserResponse>();

        users.MapPut("/{id:guid}/status", async (
                Guid id,
                SetUserStatusBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SetUserStatusCommand(id, body.Status), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminUserStatusPut")
            .WithSummary("Suspends, reinstates or unlocks an account. Suspending it ends its sessions.")
            .RequirePermission(PermissionCatalog.IdentityUserManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<AdminUserResponse>();
    }

    private static void MapRoles(IEndpointRouteBuilder admin)
    {
        var roles = admin.MapGroup("/roles").WithTags("Identity");

        roles.MapGet("/", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetRolesQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminRolesGet")
            .WithSummary("Lists the roles and what each one grants.")
            .RequirePermission(PermissionCatalog.IdentityRoleRead)
            .Produces<IReadOnlyList<RoleResponse>>();

        roles.MapPost("/", async (CreateRoleBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreateRoleCommand(
                    body.Code,
                    body.Name,
                    body.Scope,
                    body.Description,
                    body.Permissions);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminRoleCreate")
            .WithSummary("Defines a role of this deployment's own.")
            .RequirePermission(PermissionCatalog.IdentityRoleManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<RoleResponse>();

        roles.MapPut("/{id:guid}", async (
                Guid id,
                UpdateRoleBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateRoleCommand(id, body.Name, body.Description, body.Permissions);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminRolePut")
            .WithSummary("Changes what a role grants. Refused for the roles the platform itself defines.")
            .RequirePermission(PermissionCatalog.IdentityRoleManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<RoleResponse>();

        admin.MapGet("/permissions", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetPermissionsQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPermissionsGet")
            .WithSummary("The permission catalogue, grouped as the role editor renders it.")
            .WithTags("Identity")
            .RequirePermission(PermissionCatalog.IdentityRoleRead)
            .Produces<IReadOnlyList<PermissionGroupResponse>>();
    }
}
