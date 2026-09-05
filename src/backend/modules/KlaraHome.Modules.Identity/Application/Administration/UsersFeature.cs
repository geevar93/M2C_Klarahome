using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Identity.Application.Authentication;
using KlaraHome.Modules.Identity.Application.Validation;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure;
using KlaraHome.Modules.Identity.Infrastructure.Access;
using KlaraHome.Modules.Identity.Infrastructure.Persistence;
using KlaraHome.Modules.Identity.Infrastructure.Security;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Identity.Application.Administration;

/// <summary>Lists the user accounts the caller is allowed to see.</summary>
/// <param name="Search">A fragment of an email address or mobile number.</param>
/// <param name="UserType">Restrict to one class of actor.</param>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record SearchUsersQuery(string? Search, UserType? UserType, string? Cursor, int? Size)
    : IQuery<PagedResult<AdminUserResponse>>;

/// <summary>Reads one user account.</summary>
/// <param name="Id">The account.</param>
internal sealed record GetUserQuery(Guid Id) : IQuery<AdminUserResponse>;

/// <summary>Creates a staff or vendor account.</summary>
/// <param name="Email">Their email address, which is how they sign in.</param>
/// <param name="Mobile">An optional mobile number.</param>
/// <param name="UserType">Vendor staff or platform staff.</param>
/// <param name="RoleCodes">The roles to grant.</param>
/// <param name="VendorId">The seller a vendor user belongs to.</param>
internal sealed record CreateUserCommand(
    string Email,
    string? Mobile,
    UserType UserType,
    IReadOnlyList<string> RoleCodes,
    Guid? VendorId) : ICommand<AdminUserResponse>;

/// <summary>Replaces a user's roles.</summary>
/// <param name="Id">The account.</param>
/// <param name="RoleCodes">The roles they should hold.</param>
internal sealed record SetUserRolesCommand(Guid Id, IReadOnlyList<string> RoleCodes)
    : ICommand<AdminUserResponse>;

/// <summary>Suspends or reinstates an account, or clears a lockout.</summary>
/// <param name="Id">The account.</param>
/// <param name="Status">The status it should have.</param>
internal sealed record SetUserStatusCommand(Guid Id, UserStatus Status) : ICommand<AdminUserResponse>;

/// <summary>A user account, as an administrator sees it.</summary>
/// <param name="Id">The account id.</param>
/// <param name="UserType">Which surface they belong to.</param>
/// <param name="Email">Their email address.</param>
/// <param name="Mobile">Their mobile number, masked.</param>
/// <param name="Status">Whether they may sign in.</param>
/// <param name="MobileVerified">Whether the mobile number has been proved.</param>
/// <param name="EmailVerified">Whether the email address has been proved.</param>
/// <param name="TwoFactorEnabled">Whether a second factor is enrolled.</param>
/// <param name="LockedUntil">When the current lockout expires, if any.</param>
/// <param name="LastLoginAt">When they last signed in.</param>
/// <param name="CreatedAt">When the account was created.</param>
/// <param name="VendorId">The seller they act for, or null.</param>
/// <param name="Roles">The roles they hold.</param>
/// <param name="MustChangePassword">Whether an administrator issued the current password.</param>
/// <param name="PasswordSetupPending">
/// Whether this account has no password at all. True for one just created while email delivery is
/// off — nobody can sign in to it until an administrator issues a temporary password.
/// </param>
internal sealed record AdminUserResponse(
    Guid Id,
    UserType UserType,
    string? Email,
    string? Mobile,
    UserStatus Status,
    bool MobileVerified,
    bool EmailVerified,
    bool TwoFactorEnabled,
    DateTimeOffset? LockedUntil,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    Guid? VendorId,
    IReadOnlyList<string> Roles,
    bool MustChangePassword,
    bool PasswordSetupPending);

/// <summary>Rules for creating an account.</summary>
internal sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty()
            .MaximumLength(320)
            .Matches(IdentityFormats.Email()).WithMessage("Enter a valid email address.");

        RuleFor(command => command.Mobile!)
            .Must(IndianMobile.IsValid)
            .When(command => !string.IsNullOrWhiteSpace(command.Mobile))
            .WithMessage("Enter a valid Indian mobile number.");

        RuleFor(command => command.UserType)
            .NotEqual(UserType.Customer)
            .WithMessage("Shoppers register themselves; this endpoint creates staff and vendor accounts.");

        // The seller is deliberately not required here. A vendor owner creating their own staff
        // supplies none and takes it from their token, and a validator cannot see the caller —
        // so the rule that a vendor user must end up with exactly one seller lives in the handler.
        RuleFor(command => command.RoleCodes).NotEmpty().WithMessage("Grant at least one role.");
    }
}

/// <summary>Rules for a role change.</summary>
internal sealed class SetUserRolesValidator : AbstractValidator<SetUserRolesCommand>
{
    public SetUserRolesValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.RoleCodes).NotNull();
    }
}

/// <summary>
/// Lists user accounts, keyset-paginated.
/// </summary>
/// <remarks>
/// <para>
/// This is where the vendor scope stops being a claim and becomes an outcome. The query joins
/// <c>user_roles</c>, which is <see cref="KlaraHome.SharedKernel.Domain.IVendorScoped"/>, so a
/// vendor caller's rows are filtered in the data layer before this handler sees them: a vendor
/// owner listing "their" users cannot discover that another seller's staff exist.
/// </para>
/// <para>
/// Platform staff have no vendor scope, so the filter is inert for them and they see everyone —
/// which is what distinguishes the two, and why the scope is decided by the token rather than by a
/// parameter this handler could get wrong.
/// </para>
/// </remarks>
/// <param name="context">The Identity data context.</param>
/// <param name="caller">The signed-in caller, for their vendor scope.</param>
internal sealed class SearchUsersQueryHandler(IdentityDbContext context, ICallerContext caller)
    : IQueryHandler<SearchUsersQuery, PagedResult<AdminUserResponse>>
{
    public async Task<Result<PagedResult<AdminUserResponse>>> HandleAsync(
        SearchUsersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);

        var users = context.Users.AsNoTracking().AsQueryable();

        if (caller.VendorId is not null)
        {
            // The grants are already filtered to the caller's vendor; restricting the users to the
            // ones that have such a grant is what turns that into "my seller's staff".
            users = users.Where(user => context.UserRoles.Any(grant => grant.UserId == user.Id));
        }

        if (query.UserType is not null)
        {
            users = users.Where(user => user.UserType == query.UserType);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            users = users.Where(user =>
                (user.Email != null && user.Email.Contains(term))
                || (user.Mobile != null && user.Mobile.Contains(term)));
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            // UUIDv7 ids are time-ordered, so the id alone is a stable keyset cursor for a
            // newest-first list — no second sort column, and no rows skipped or repeated when an
            // account is created mid-page.
            users = users.Where(user => user.Id.CompareTo(after) < 0);
        }

        var page = await users
            .OrderByDescending(user => user.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        var ids = page.ConvertAll(user => user.Id);

        var grants = await context.UserRoles
            .AsNoTracking()
            .Where(grant => ids.Contains(grant.UserId))
            .Join(
                context.Roles.AsNoTracking(),
                grant => grant.RoleId,
                role => role.Id,
                (grant, role) => new { grant.UserId, role.Code, grant.VendorId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = page.ConvertAll(user =>
        {
            var held = grants.FindAll(grant => grant.UserId == user.Id);

            return Project(
                user,
                held.ConvertAll(grant => grant.Code),
                held.Find(grant => grant.VendorId is not null)?.VendorId);
        });

        return new PagedResult<AdminUserResponse>(
            items,
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].Id.ToString()) : null));
    }

    /// <summary>Projects a user onto the administrative response.</summary>
    /// <param name="user">The user.</param>
    /// <param name="roles">The role codes they hold.</param>
    /// <param name="vendorId">The seller they act for.</param>
    internal static AdminUserResponse Project(User user, IReadOnlyList<string> roles, Guid? vendorId)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new AdminUserResponse(
            user.Id,
            user.UserType,
            user.Email,
            // Masked in a list, as personal data is (docs/07-security-compliance.md §5). The full
            // number is on the detail view, which is a separate, individually audited read.
            SignInCoordinator.Mask(user.Mobile ?? string.Empty) is var masked && masked.Length == 0
                ? null
                : masked,
            user.Status,
            user.MobileVerifiedAt is not null,
            user.EmailVerifiedAt is not null,
            user.TotpEnabled,
            user.LockedUntil,
            user.LastLoginAt,
            user.CreatedAt,
            vendorId,
            roles,
            user.MustChangePassword,
            user.PasswordHash is null && user.UserType != Domain.UserType.Customer);
    }
}

/// <summary>
/// Reads one account, scoped to the caller.
/// </summary>
/// <remarks>
/// An account outside the caller's vendor answers 404, not 403: an object-level check that says
/// "forbidden" confirms the id exists, which is the leak §2 rules out.
/// </remarks>
/// <param name="scope">Finds the account and applies the caller's vendor scope.</param>
internal sealed class GetUserQueryHandler(AdminUserScope scope)
    : IQueryHandler<GetUserQuery, AdminUserResponse>
{
    public async Task<Result<AdminUserResponse>> HandleAsync(
        GetUserQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await scope.FindAsync(query.Id, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return AdminUserScope.NotFound();
        }

        var (roles, vendorId) = await scope.GrantsOfAsync(user.Id, cancellationToken).ConfigureAwait(false);
        var response = SearchUsersQueryHandler.Project(user, roles, vendorId);

        // Only the list masks the number. This read is the detail view, and an administrator
        // answering a support call needs the number they are looking at.
        return response with { Mobile = user.Mobile };
    }
}

/// <summary>Creates a staff or vendor account.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="scope">Applies and checks the caller's vendor scope.</param>
/// <param name="caller">The signed-in caller.</param>
/// <param name="otp">Sends the new user a password-reset link to set their first password.</param>
/// <param name="audit">Records the creation.</param>
/// <param name="flags">Decides whether a link can be sent at all.</param>
internal sealed class CreateUserCommandHandler(
    IdentityDbContext context,
    AdminUserScope scope,
    ICallerContext caller,
    OtpService otp,
    IAuditLogger audit,
    IFeatureFlags flags) : ICommandHandler<CreateUserCommand, AdminUserResponse>
{
    /// <summary>The audited action for a created account.</summary>
    public const string AuditAction = "identity.user.created";

    public async Task<Result<AdminUserResponse>> HandleAsync(
        CreateUserCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var vendorId = caller.VendorId ?? command.VendorId;

        if (caller.VendorId is not null && command.VendorId is not null && command.VendorId != caller.VendorId)
        {
            // A vendor owner creating a user "for" another seller. Refused as validation rather
            // than 403, because the caller has the permission — it is the value that is wrong.
            return Error.Validation(
                "IDENTITY_VENDOR_SCOPE",
                "You can only create users in your own organisation.");
        }

        if (command.UserType == UserType.Vendor && vendorId is null)
        {
            return Error.Validation(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["vendorId"] = ["A vendor user belongs to exactly one seller."],
                });
        }

        var email = command.Email.Trim().ToLowerInvariant();
        var mobile = string.IsNullOrWhiteSpace(command.Mobile) ? null : IndianMobile.Normalize(command.Mobile);

        var taken = await context.Users
            .AnyAsync(
                candidate => candidate.Email == email || (mobile != null && candidate.Mobile == mobile),
                cancellationToken)
            .ConfigureAwait(false);

        if (taken)
        {
            return Error.Conflict(
                "IDENTITY_ACCOUNT_EXISTS",
                "An account already exists with that email address or mobile number.");
        }

        var roles = await scope
            .ResolveRolesAsync(command.RoleCodes, command.UserType, cancellationToken)
            .ConfigureAwait(false);

        if (roles.IsFailure)
        {
            return roles.Error;
        }

        var user = User.RegisterStaff(command.UserType, email, mobile);
        context.Users.Add(user);

        foreach (var role in roles.Value)
        {
            user.GrantRole(role.Id, role.Scope == RoleScope.Vendor ? vendorId : null);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // No password is set here. The account is created without one and the new user chooses
        // theirs from a reset link, so nobody — including the administrator who created it — ever
        // knows a credential that would let them sign in as somebody else.
        //
        // With email delivery off there is no link to send, and the account is left unable to sign
        // in until an administrator issues a temporary password (ADR-014 decision 5). The response
        // says so through PasswordSetupPending rather than leaving it to be discovered.
        var canEmail = await flags
            .IsEnabledAsync(IdentityFeatures.PasswordResetEmail, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (canEmail)
        {
            await otp
                .IssueAsync(email, OtpChannel.Email, OtpPurpose.PasswordReset, user.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = SignInCoordinator.UserEntityType,
                EntityId = user.Id.ToString(),
                After = new
                {
                    email,
                    userType = command.UserType.ToString(),
                    roles = command.RoleCodes,
                    vendorId,
                    passwordLinkSent = canEmail,
                },
            },
            cancellationToken).ConfigureAwait(false);

        return SearchUsersQueryHandler.Project(
            user,
            [.. roles.Value.Select(role => role.Code)],
            vendorId);
    }
}

/// <summary>Replaces a user's roles.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="scope">Applies the caller's vendor scope.</param>
/// <param name="caller">The signed-in caller.</param>
/// <param name="sessions">Ends the user's sessions, so the change takes effect at once.</param>
/// <param name="audit">Records the change.</param>
internal sealed class SetUserRolesCommandHandler(
    IdentityDbContext context,
    AdminUserScope scope,
    ICallerContext caller,
    SessionService sessions,
    IAuditLogger audit) : ICommandHandler<SetUserRolesCommand, AdminUserResponse>
{
    /// <summary>The audited action for a role change.</summary>
    public const string AuditAction = "identity.user.roles-changed";

    public async Task<Result<AdminUserResponse>> HandleAsync(
        SetUserRolesCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await scope.FindAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return AdminUserScope.NotFound();
        }

        var (before, vendorId) = await scope.GrantsOfAsync(user.Id, cancellationToken).ConfigureAwait(false);

        var roles = await scope
            .ResolveRolesAsync(command.RoleCodes, user.UserType, cancellationToken)
            .ConfigureAwait(false);

        if (roles.IsFailure)
        {
            return roles.Error;
        }

        var scopeVendorId = caller.VendorId ?? vendorId;

        var existing = await context.UserRoles
            .Where(grant => grant.UserId == user.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        context.UserRoles.RemoveRange(existing);

        foreach (var role in roles.Value)
        {
            context.UserRoles.Add(UserRole.Create(
                user.Id,
                role.Id,
                role.Scope == RoleScope.Vendor ? scopeVendorId : null));
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Permissions live in the access token, so a revoked role would otherwise keep working
        // until it expired. Ending the sessions is what makes "revoked" mean now.
        await sessions
            .RevokeAllAsync(user.Id, SessionEndReason.CredentialChanged, null, cancellationToken)
            .ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = SignInCoordinator.UserEntityType,
                EntityId = user.Id.ToString(),
                Before = new { roles = before },
                After = new { roles = command.RoleCodes },
            },
            cancellationToken).ConfigureAwait(false);

        return SearchUsersQueryHandler.Project(
            user,
            [.. roles.Value.Select(role => role.Code)],
            scopeVendorId);
    }
}

/// <summary>Suspends, reinstates or unlocks an account.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="scope">Applies the caller's vendor scope.</param>
/// <param name="sessions">Ends the sessions of an account that is being closed.</param>
/// <param name="audit">Records the change.</param>
internal sealed class SetUserStatusCommandHandler(
    IdentityDbContext context,
    AdminUserScope scope,
    SessionService sessions,
    IAuditLogger audit) : ICommandHandler<SetUserStatusCommand, AdminUserResponse>
{
    /// <summary>The audited action for a status change.</summary>
    public const string AuditAction = "identity.user.status-changed";

    public async Task<Result<AdminUserResponse>> HandleAsync(
        SetUserStatusCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await scope.FindAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return AdminUserScope.NotFound();
        }

        var before = user.Status;

        user.ChangeStatus(command.Status);

        if (command.Status == UserStatus.Active)
        {
            user.Unlock();
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (command.Status != UserStatus.Active)
        {
            await sessions
                .RevokeAllAsync(user.Id, SessionEndReason.AccountClosed, null, cancellationToken)
                .ConfigureAwait(false);
        }

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = SignInCoordinator.UserEntityType,
                EntityId = user.Id.ToString(),
                Before = new { status = before.ToString() },
                After = new { status = command.Status.ToString() },
            },
            cancellationToken).ConfigureAwait(false);

        var (roles, vendorId) = await scope.GrantsOfAsync(user.Id, cancellationToken).ConfigureAwait(false);
        return SearchUsersQueryHandler.Project(user, roles, vendorId);
    }
}

/// <summary>
/// The vendor scope, applied to a single account rather than to a list.
/// </summary>
/// <remarks>
/// A list is filtered by the data layer; a lookup by id has to be checked, because the id came from
/// the caller. Both live here so the two forms of the same rule cannot drift apart.
/// </remarks>
/// <param name="context">The Identity data context.</param>
/// <param name="caller">The signed-in caller.</param>
internal sealed class AdminUserScope(IdentityDbContext context, ICallerContext caller)
{
    /// <summary>The response for an account outside the caller's scope, or one that is absent.</summary>
    public static Error NotFound()
        => Error.NotFound("IDENTITY_USER_NOT_FOUND", "That user does not exist.");

    /// <summary>Finds an account the caller is allowed to act on, or null.</summary>
    /// <param name="id">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<User?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return null;
        }

        if (caller.VendorId is null)
        {
            return user;
        }

        // The grant query carries the vendor filter, so this returns nothing at all for a user
        // outside the caller's seller — which is why the caller then gets a 404.
        var inScope = await context.UserRoles
            .AnyAsync(grant => grant.UserId == user.Id, cancellationToken)
            .ConfigureAwait(false);

        return inScope ? user : null;
    }

    /// <summary>The role codes an account holds, and the seller its grants point at.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<(IReadOnlyList<string> Roles, Guid? VendorId)> GrantsOfAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var grants = await context.UserRoles
            .IgnoreQueryFilters([ModelConventions.VendorFilter])
            .Where(grant => grant.UserId == userId
                            && (caller.VendorId == null || grant.VendorId == caller.VendorId))
            .Join(
                context.Roles,
                grant => grant.RoleId,
                role => role.Id,
                (grant, role) => new { role.Code, grant.VendorId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (
            grants.ConvertAll(grant => grant.Code),
            grants.Find(grant => grant.VendorId is not null)?.VendorId);
    }

    /// <summary>
    /// Resolves role codes to roles, refusing any the caller may not grant.
    /// </summary>
    /// <remarks>
    /// A vendor user may hold only vendor-scoped roles. Without this check a vendor owner holding
    /// <c>identity.role.assign</c> could grant themselves <c>platform-admin</c> — which is the
    /// classic privilege-escalation path through a delegated user-management screen.
    /// </remarks>
    /// <param name="codes">The role codes requested.</param>
    /// <param name="userType">The account's actor class.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<IReadOnlyList<Role>>> ResolveRolesAsync(
        IReadOnlyList<string> codes,
        UserType userType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codes);

        var wanted = codes.Distinct(StringComparer.Ordinal).ToList();

        var roles = await context.Roles
            .Where(role => wanted.Contains(role.Code))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var unknown = wanted.Except(roles.ConvertAll(role => role.Code), StringComparer.Ordinal).ToList();

        if (unknown.Count > 0)
        {
            return Error.Validation(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["roleCodes"] = [$"No such role: {string.Join(", ", unknown)}."],
                });
        }

        var forbidden = roles.FindAll(role =>
            (caller.VendorId is not null && role.Scope != RoleScope.Vendor)
            || (userType == UserType.Vendor && role.Scope == RoleScope.Platform)
            || (userType == UserType.Staff && role.Scope == RoleScope.Vendor));

        if (forbidden.Count > 0)
        {
            return Error.Forbidden(
                "IDENTITY_ROLE_NOT_GRANTABLE",
                $"You cannot grant this account: {string.Join(", ", forbidden.ConvertAll(role => role.Code))}.");
        }

        return Result.Success<IReadOnlyList<Role>>(roles);
    }
}
