using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Identity.Application.Administration;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure;
using KlaraHome.Modules.Identity.Infrastructure.Access;
using KlaraHome.Modules.Identity.Infrastructure.Persistence;
using KlaraHome.Modules.Identity.Infrastructure.Security;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Identity.Application.Authentication;

/// <summary>
/// Replaces a password, either voluntarily or because one was issued by an administrator.
/// </summary>
/// <param name="ChallengeToken">
/// The pending-sign-in token from a <c>password-change-required</c> challenge. Null when the
/// caller is already signed in and simply changing their password.
/// </param>
/// <param name="CurrentPassword">The password being replaced. Required either way.</param>
/// <param name="NewPassword">The replacement.</param>
/// <param name="Device">What the caller looks like, for the session the change issues.</param>
internal sealed record ChangePasswordCommand(
    string? ChallengeToken,
    string CurrentPassword,
    string NewPassword,
    DeviceInfo Device) : ICommand<SignInResult>;

/// <summary>Issues a temporary password for another account (ADR-014 decision 5).</summary>
/// <param name="UserId">The account.</param>
/// <param name="TemporaryPassword">The password the administrator will convey out of band.</param>
internal sealed record SetTemporaryPasswordCommand(Guid UserId, string TemporaryPassword)
    : ICommand<AdminUserResponse>;

/// <summary>Rules for a password change.</summary>
internal sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator(PasswordRules passwords)
    {
        ArgumentNullException.ThrowIfNull(passwords);

        RuleFor(command => command.CurrentPassword).NotEmpty().MaximumLength(256);
        passwords.Apply(RuleFor(command => command.NewPassword));

        RuleFor(command => command.NewPassword)
            .NotEqual(command => command.CurrentPassword)
            .WithMessage("The new password must be different from the current one.");
    }
}

/// <summary>Rules for issuing a temporary password.</summary>
internal sealed class SetTemporaryPasswordValidator : AbstractValidator<SetTemporaryPasswordCommand>
{
    public SetTemporaryPasswordValidator(PasswordRules passwords)
    {
        ArgumentNullException.ThrowIfNull(passwords);

        RuleFor(command => command.UserId).NotEmpty();

        // The same strength rules as any other password. It is short-lived, but it is a real
        // credential while it lives, and "temporary" is not a reason to accept `Welcome123`.
        passwords.Apply(RuleFor(command => command.TemporaryPassword));
    }
}

/// <summary>
/// Replaces a password and issues the session the change was standing in the way of.
/// </summary>
/// <remarks>
/// One handler for both routes into it — a forced change and a voluntary one — because the rules
/// are identical: prove the current password, meet the policy, sign every other device out. The
/// challenge token is the only difference, and it stands in for the session the caller does not
/// have yet.
/// </remarks>
/// <param name="context">The Identity data context.</param>
/// <param name="hasher">Verifies the old password and hashes the new one.</param>
/// <param name="tokens">Reads the challenge token.</param>
/// <param name="caller">The signed-in caller, on the voluntary route.</param>
/// <param name="signIn">Completes the sign-in once the obligation is discharged.</param>
/// <param name="sessions">Revokes the other devices.</param>
/// <param name="audit">Records the change.</param>
internal sealed class ChangePasswordCommandHandler(
    IdentityDbContext context,
    PasswordHasher hasher,
    TokenIssuer tokens,
    ICallerContext caller,
    SignInCoordinator signIn,
    SessionService sessions,
    IAuditLogger audit) : ICommandHandler<ChangePasswordCommand, SignInResult>
{
    /// <summary>The audited action for a password the owner changed themselves.</summary>
    public const string AuditAction = "identity.password.changed";

    public async Task<Result<SignInResult>> HandleAsync(
        ChangePasswordCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = string.IsNullOrWhiteSpace(command.ChallengeToken)
            ? caller.UserId
            : await tokens.ReadTwoFactorChallengeAsync(command.ChallengeToken).ConfigureAwait(false);

        if (userId is null)
        {
            return Error.Unauthorized();
        }

        var user = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null || user.Status != UserStatus.Active)
        {
            return AuthErrors.InvalidCredentials();
        }

        if (!hasher.Verify(command.CurrentPassword, user.PasswordHash, out _))
        {
            return AuthErrors.InvalidCredentials();
        }

        user.SetPasswordHash(hasher.Hash(command.NewPassword));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Every other device, including any the temporary password reached.
        await sessions
            .RevokeAllAsync(user.Id, SessionEndReason.CredentialChanged, null, cancellationToken)
            .ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = SignInCoordinator.UserEntityType,
                EntityId = user.Id.ToString(),
                ActorId = user.Id,
                ActorType = SignInCoordinator.ActorTypeOf(user.UserType),
                After = new { sessionsRevoked = true },
            },
            cancellationToken).ConfigureAwait(false);

        // The obligation is discharged, so the sign-in that was challenged now completes. The
        // second factor has already been satisfied by this point on the forced route, and there is
        // nothing to prove again on the voluntary one.
        return await signIn
            .CompleteAsync(user, command.Device, secondFactorSatisfied: true, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Issues a temporary password for somebody else.
/// </summary>
/// <remarks>
/// This is the knowingly weaker control ADR-014 decision 5 records: for as long as it takes the
/// owner to sign in, an administrator knows a credential that would sign in as them. What narrows
/// it is here — the account cannot be used until the password is replaced, every existing session
/// is ended, and the act is in the audit trail with the administrator's name on it. What closes it
/// is email delivery returning, at which point the reset link makes this endpoint unnecessary.
/// </remarks>
/// <param name="context">The Identity data context.</param>
/// <param name="scope">Applies the caller's vendor scope.</param>
/// <param name="hasher">Hashes the temporary password.</param>
/// <param name="sessions">Ends the account's sessions.</param>
/// <param name="audit">Records the act.</param>
/// <param name="caller">The administrator doing it.</param>
internal sealed class SetTemporaryPasswordCommandHandler(
    IdentityDbContext context,
    AdminUserScope scope,
    PasswordHasher hasher,
    SessionService sessions,
    IAuditLogger audit,
    ICallerContext caller) : ICommandHandler<SetTemporaryPasswordCommand, AdminUserResponse>
{
    /// <summary>The audited action for an administrator-issued password.</summary>
    public const string AuditAction = "identity.password.temporary-issued";

    public async Task<Result<AdminUserResponse>> HandleAsync(
        SetTemporaryPasswordCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await scope.FindAsync(command.UserId, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return AdminUserScope.NotFound();
        }

        if (user.Id == caller.UserId)
        {
            // An administrator issuing themselves a temporary password would be changing their own
            // password without knowing the current one, which is what /auth/password/change is for.
            return Error.Validation(
                "IDENTITY_CANNOT_SELF_ISSUE",
                "Use the password change endpoint to change your own password.");
        }

        user.SetPasswordHash(hasher.Hash(command.TemporaryPassword), mustChange: true);
        user.Unlock();

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await sessions
            .RevokeAllAsync(user.Id, SessionEndReason.CredentialChanged, null, cancellationToken)
            .ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = SignInCoordinator.UserEntityType,
                EntityId = user.Id.ToString(),
                // Recorded as the administrator's act, not the account's and not the system's:
                // "who issued this" is the whole question an access review asks of this row.
                ActorType = AuditActorType.StaffUser,
                // The password itself is of course not recorded.
                After = new { issuedBy = caller.UserId, mustChangeAtNextSignIn = true },
            },
            cancellationToken).ConfigureAwait(false);

        var (roles, vendorId) = await scope.GrantsOfAsync(user.Id, cancellationToken).ConfigureAwait(false);
        return SearchUsersQueryHandler.Project(user, roles, vendorId);
    }
}
