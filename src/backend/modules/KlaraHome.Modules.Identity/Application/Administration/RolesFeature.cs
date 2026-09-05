using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure.Persistence;
using KlaraHome.Modules.Identity.Infrastructure.Seeding;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Identity.Application.Administration;

/// <summary>Lists the roles this deployment has.</summary>
internal sealed record GetRolesQuery : IQuery<IReadOnlyList<RoleResponse>>;

/// <summary>Lists the permission catalogue, grouped as the admin UI shows it.</summary>
internal sealed record GetPermissionsQuery : IQuery<IReadOnlyList<PermissionGroupResponse>>;

/// <summary>Defines a role of this deployment's own.</summary>
/// <param name="Code">The stable lowercase code.</param>
/// <param name="Name">The display name.</param>
/// <param name="Scope">Platform-wide, vendor-scoped or customer.</param>
/// <param name="Description">What the role is for.</param>
/// <param name="Permissions">What it grants.</param>
internal sealed record CreateRoleCommand(
    string Code,
    string Name,
    RoleScope Scope,
    string Description,
    IReadOnlyList<string> Permissions) : ICommand<RoleResponse>;

/// <summary>Changes a role's name, description and permission set.</summary>
/// <param name="Id">The role.</param>
/// <param name="Name">The display name.</param>
/// <param name="Description">What the role is for.</param>
/// <param name="Permissions">What it should grant.</param>
internal sealed record UpdateRoleCommand(
    Guid Id,
    string Name,
    string Description,
    IReadOnlyList<string> Permissions) : ICommand<RoleResponse>;

/// <summary>A role and what it grants.</summary>
/// <param name="Id">The role id.</param>
/// <param name="Code">The stable code.</param>
/// <param name="Name">The display name.</param>
/// <param name="Scope">Where it applies.</param>
/// <param name="Description">What it is for.</param>
/// <param name="IsSystem">Whether the platform defines it, and therefore refuses to let it be edited.</param>
/// <param name="Permissions">The permission codes it grants.</param>
internal sealed record RoleResponse(
    Guid Id,
    string Code,
    string Name,
    RoleScope Scope,
    string Description,
    bool IsSystem,
    IReadOnlyList<string> Permissions);

/// <summary>One heading in the permission catalogue.</summary>
/// <param name="Group">The heading.</param>
/// <param name="Permissions">The permissions filed under it.</param>
internal sealed record PermissionGroupResponse(string Group, IReadOnlyList<PermissionResponse> Permissions);

/// <summary>One permission, as a role editor renders it.</summary>
/// <param name="Code">The permission code.</param>
/// <param name="Description">What holding it allows.</param>
internal sealed record PermissionResponse(string Code, string Description);

/// <summary>Rules for defining a role.</summary>
internal sealed class CreateRoleValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleValidator()
    {
        RuleFor(command => command.Code)
            .NotEmpty()
            .MaximumLength(64)
            .Matches("^[a-z][a-z0-9-]*$")
            .WithMessage("A role code is lowercase letters, digits and hyphens, for example warehouse-lead.");

        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Description).MaximumLength(512);
        RuleFor(command => command.Permissions).NotNull();
    }
}

/// <summary>Rules for changing a role.</summary>
internal sealed class UpdateRoleValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Description).MaximumLength(512);
        RuleFor(command => command.Permissions).NotNull();
    }
}

/// <summary>Lists the roles.</summary>
/// <param name="context">The Identity data context.</param>
internal sealed class GetRolesQueryHandler(IdentityDbContext context)
    : IQueryHandler<GetRolesQuery, IReadOnlyList<RoleResponse>>
{
    public async Task<Result<IReadOnlyList<RoleResponse>>> HandleAsync(
        GetRolesQuery query,
        CancellationToken cancellationToken)
    {
        var roles = await context.Roles
            .AsNoTracking()
            .Include(role => role.Permissions)
            .OrderBy(role => role.Scope)
            .ThenBy(role => role.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<RoleResponse> response = roles.ConvertAll(RoleProjection.Project);
        return Result.Success(response);
    }
}

/// <summary>
/// Lists the permission catalogue.
/// </summary>
/// <remarks>
/// Served from the compiled-in catalogue rather than from <c>identity.permissions</c>. The table is
/// a projection of this list; reading it here would mean a role editor could offer a permission
/// that no endpoint enforces, or omit one that a deploy has not yet reconciled.
/// </remarks>
internal sealed class GetPermissionsQueryHandler
    : IQueryHandler<GetPermissionsQuery, IReadOnlyList<PermissionGroupResponse>>
{
    public Task<Result<IReadOnlyList<PermissionGroupResponse>>> HandleAsync(
        GetPermissionsQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PermissionGroupResponse> groups = PermissionCatalog.All
            .GroupBy(permission => permission.Group, StringComparer.Ordinal)
            .Select(group => new PermissionGroupResponse(
                group.Key,
                [.. group.Select(permission => new PermissionResponse(permission.Code, permission.Description))]))
            .ToList();

        return Task.FromResult(Result.Success(groups));
    }
}

/// <summary>Defines a role of this deployment's own.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="audit">Records the change.</param>
internal sealed class CreateRoleCommandHandler(IdentityDbContext context, IAuditLogger audit)
    : ICommandHandler<CreateRoleCommand, RoleResponse>
{
    /// <summary>The audited action for a new role.</summary>
    public const string AuditAction = "identity.role.created";

    public async Task<Result<RoleResponse>> HandleAsync(
        CreateRoleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var invalid = RoleProjection.UnknownPermissions(command.Permissions);
        if (invalid is not null)
        {
            return invalid;
        }

        var exists = await context.Roles
            .AnyAsync(role => role.Code == command.Code, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return Error.Conflict("IDENTITY_ROLE_EXISTS", $"A role with the code '{command.Code}' already exists.");
        }

        var role = Role.Define(command.Code, command.Name, command.Scope, command.Description);
        role.SetPermissions(command.Permissions);

        context.Roles.Add(role);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = RoleProjection.EntityType,
                EntityId = role.Id.ToString(),
                After = new { role.Code, role.Name, permissions = command.Permissions },
            },
            cancellationToken).ConfigureAwait(false);

        return RoleProjection.Project(role);
    }
}

/// <summary>Changes what a role grants.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="audit">Records the change.</param>
internal sealed class UpdateRoleCommandHandler(IdentityDbContext context, IAuditLogger audit)
    : ICommandHandler<UpdateRoleCommand, RoleResponse>
{
    /// <summary>The audited action for a changed role.</summary>
    public const string AuditAction = "identity.role.updated";

    public async Task<Result<RoleResponse>> HandleAsync(
        UpdateRoleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var invalid = RoleProjection.UnknownPermissions(command.Permissions);
        if (invalid is not null)
        {
            return invalid;
        }

        var role = await context.Roles
            .Include(candidate => candidate.Permissions)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (role is null)
        {
            return Error.NotFound("IDENTITY_ROLE_NOT_FOUND", "That role does not exist.");
        }

        if (role.IsSystem)
        {
            // The seeder reasserts a system role's permissions on every deploy, so an edit here
            // would be undone without anybody being told. Refusing is the honest answer.
            return Error.Forbidden(
                "IDENTITY_ROLE_IS_SYSTEM",
                $"'{role.Code}' is defined by the platform and cannot be edited. Create a role of your own instead.");
        }

        var before = role.Permissions.Select(permission => permission.PermissionCode).ToList();

        role.Rename(command.Name, command.Description);
        role.SetPermissions(command.Permissions);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = RoleProjection.EntityType,
                EntityId = role.Id.ToString(),
                Before = new { permissions = before },
                After = new { role.Name, permissions = command.Permissions },
            },
            cancellationToken).ConfigureAwait(false);

        return RoleProjection.Project(role);
    }
}

/// <summary>Shared projection and validation for the role endpoints.</summary>
internal static class RoleProjection
{
    /// <summary>The entity type roles are audited under.</summary>
    public const string EntityType = "Role";

    /// <summary>Projects a role onto its response.</summary>
    /// <param name="role">The role.</param>
    public static RoleResponse Project(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);

        return new RoleResponse(
            role.Id,
            role.Code,
            role.Name,
            role.Scope,
            role.Description,
            role.IsSystem,
            [.. role.Permissions.Select(permission => permission.PermissionCode).Order(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Rejects permission codes no endpoint enforces, or null when every code is known.
    /// </summary>
    /// <remarks>
    /// A role granting a permission that nothing checks is worse than a role granting nothing: it
    /// reads, on the access-review spreadsheet, as an access somebody has.
    /// </remarks>
    /// <param name="permissions">The codes requested.</param>
    public static Error? UnknownPermissions(IReadOnlyList<string> permissions)
    {
        if (permissions is null)
        {
            return null;
        }

        var unknown = permissions
            .Where(code => !PermissionCatalog.Contains(code))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return unknown.Count == 0
            ? null
            : Error.Validation(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["permissions"] = [$"No such permission: {string.Join(", ", unknown)}."],
                });
    }
}
