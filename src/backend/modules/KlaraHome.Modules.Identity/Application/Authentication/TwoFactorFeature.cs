using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure;
using KlaraHome.Modules.Identity.Infrastructure.Access;
using KlaraHome.Modules.Identity.Infrastructure.Persistence;
using KlaraHome.Modules.Identity.Infrastructure.Security;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Identity.Application.Authentication;

/// <summary>What an authenticator app needs to enrol.</summary>
/// <param name="Secret">The base32 shared secret, to be typed if the code cannot be scanned.</param>
/// <param name="OtpAuthUri">The <c>otpauth://</c> URI the client renders as a QR code.</param>
internal sealed record TwoFactorSetupResponse(string Secret, string OtpAuthUri);

/// <summary>
/// Stages a TOTP secret for a sign-in that stopped at enrolment.
/// </summary>
/// <param name="ChallengeToken">The pending-sign-in token from the login response.</param>
internal sealed record BeginTwoFactorEnrolmentCommand(string ChallengeToken)
    : ICommand<TwoFactorSetupResponse>;

/// <summary>
/// Answers the second factor and completes the sign-in, enrolling the staged secret if this is the
/// first code it has produced.
/// </summary>
/// <param name="ChallengeToken">The pending-sign-in token.</param>
/// <param name="Code">The six digits from the authenticator app.</param>
/// <param name="Device">What the caller looks like.</param>
internal sealed record CompleteTwoFactorCommand(string ChallengeToken, string Code, DeviceInfo Device)
    : ICommand<SignInResult>;

/// <summary>Stages a new TOTP secret for a user who is already signed in.</summary>
internal sealed record StartTwoFactorSetupCommand : ICommand<TwoFactorSetupResponse>;

/// <summary>Turns the second factor on, once a code from the staged secret has been produced.</summary>
/// <param name="Code">The six digits from the authenticator app.</param>
internal sealed record EnableTwoFactorCommand(string Code) : ICommand;

/// <summary>Turns the second factor off.</summary>
/// <param name="Password">The account password, so a borrowed session cannot remove it.</param>
/// <param name="Code">A current code, proving the authenticator is still in hand.</param>
internal sealed record DisableTwoFactorCommand(string Password, string Code) : ICommand;

/// <summary>Rules for the 2FA commands.</summary>
internal sealed class TwoFactorValidators
    : AbstractValidator<CompleteTwoFactorCommand>
{
    public TwoFactorValidators()
    {
        RuleFor(command => command.ChallengeToken).NotEmpty().MaximumLength(4096);
        RuleFor(command => command.Code).NotEmpty().Matches("^[0-9]{6}$");
    }
}

/// <summary>Rules for enabling a second factor.</summary>
internal sealed class EnableTwoFactorValidator : AbstractValidator<EnableTwoFactorCommand>
{
    public EnableTwoFactorValidator()
        => RuleFor(command => command.Code).NotEmpty().Matches("^[0-9]{6}$");
}

/// <summary>Rules for disabling a second factor.</summary>
internal sealed class DisableTwoFactorValidator : AbstractValidator<DisableTwoFactorCommand>
{
    public DisableTwoFactorValidator()
    {
        RuleFor(command => command.Password).NotEmpty();
        RuleFor(command => command.Code).NotEmpty().Matches("^[0-9]{6}$");
    }
}

/// <summary>The failures and the audited actions the 2FA flows share.</summary>
internal static class TwoFactorAudit
{
    /// <summary>A second factor was enrolled.</summary>
    public const string EnabledAction = "identity.two-factor.enabled";

    /// <summary>A second factor was removed.</summary>
    public const string DisabledAction = "identity.two-factor.disabled";
}

/// <summary>Stages a secret for a sign-in that reached the enrolment challenge.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="tokens">Reads the challenge token.</param>
/// <param name="protector">Encrypts the staged secret.</param>
/// <param name="settings">Supplies the store name shown in the authenticator app.</param>
internal sealed class BeginTwoFactorEnrolmentCommandHandler(
    IdentityDbContext context,
    TokenIssuer tokens,
    SecretProtector protector,
    IStoreSettings settings) : ICommandHandler<BeginTwoFactorEnrolmentCommand, TwoFactorSetupResponse>
{
    public async Task<Result<TwoFactorSetupResponse>> HandleAsync(
        BeginTwoFactorEnrolmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = await tokens
            .ReadTwoFactorChallengeAsync(command.ChallengeToken)
            .ConfigureAwait(false);

        if (userId is null)
        {
            return AuthErrors.InvalidCode();
        }

        var user = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null || user.Status != UserStatus.Active)
        {
            return AuthErrors.InvalidCredentials();
        }

        if (user.TotpEnabled)
        {
            // Re-enrolling from a login challenge would let anyone holding a first-factor
            // credential replace the second one, which is the whole thing it defends against.
            return Error.Conflict(
                "IDENTITY_TWO_FACTOR_ALREADY_ENABLED",
                "This account already has an authenticator. Sign in with a code, or ask an administrator to reset it.");
        }

        return await StageAsync(context, protector, settings, user, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Mints a secret, encrypts it against the user, and returns what the app needs.</summary>
    internal static async Task<TwoFactorSetupResponse> StageAsync(
        IdentityDbContext context,
        SecretProtector protector,
        IStoreSettings settings,
        User user,
        CancellationToken cancellationToken)
    {
        var secret = Totp.NewSecret();
        user.StageTotpSecret(protector.Protect(secret));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The authenticator app shows this next to the code, so it has to be the store's name
        // rather than the product's: a customer of two Klara Home deployments would otherwise see
        // two identical entries and no way to tell them apart.
        var branding = await settings.GetAsync<BrandingSettings>(cancellationToken).ConfigureAwait(false);
        var issuer = string.IsNullOrWhiteSpace(branding.StoreName) ? "Klara Home" : branding.StoreName;
        var account = user.Email ?? user.Mobile ?? user.Id.ToString();

        return new TwoFactorSetupResponse(secret, Totp.ProvisioningUri(secret, issuer, account));
    }
}

/// <summary>
/// Answers the second factor and completes the sign-in.
/// </summary>
/// <remarks>
/// A correct code from a staged-but-not-enabled secret both enables it and signs the user in, which
/// is what makes the enrolment flow a single round trip from the user's point of view: scan, type,
/// you are in. The alternative — enable, then sign in again — is where people abandon a mandatory
/// 2FA rollout.
/// </remarks>
/// <param name="context">The Identity data context.</param>
/// <param name="tokens">Reads the challenge token.</param>
/// <param name="protector">Decrypts the stored secret.</param>
/// <param name="signIn">Completes the sign-in.</param>
/// <param name="audit">Records an enrolment.</param>
/// <param name="options">Lockout settings, applied to wrong codes as well as wrong passwords.</param>
/// <param name="clock">The clock.</param>
internal sealed class CompleteTwoFactorCommandHandler(
    IdentityDbContext context,
    TokenIssuer tokens,
    SecretProtector protector,
    SignInCoordinator signIn,
    IAuditLogger audit,
    IOptions<AuthOptions> options,
    IClock clock) : ICommandHandler<CompleteTwoFactorCommand, SignInResult>
{
    public async Task<Result<SignInResult>> HandleAsync(
        CompleteTwoFactorCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = await tokens.ReadTwoFactorChallengeAsync(command.ChallengeToken).ConfigureAwait(false);

        if (userId is null)
        {
            return AuthErrors.InvalidCode();
        }

        var user = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null || user.Status != UserStatus.Active)
        {
            return AuthErrors.InvalidCredentials();
        }

        var now = clock.UtcNow;

        if (user.IsLockedOut(now))
        {
            return AuthErrors.LockedOut(user.LockedUntil!.Value);
        }

        var secret = protector.Unprotect(user.TotpSecretEncrypted);

        if (secret is null || !Totp.Verify(secret, command.Code, now))
        {
            // A wrong code counts against the same budget as a wrong password. Without it, an
            // attacker holding a stolen password has a million free guesses at six digits.
            var lockout = options.Value.Lockout;

            user.RecordLoginFailed(
                now,
                lockout.Threshold,
                TimeSpan.FromSeconds(lockout.BaseSeconds),
                TimeSpan.FromSeconds(lockout.MaximumSeconds));

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await signIn
                .RecordFailureAsync(user.Email ?? user.Mobile ?? string.Empty, user.Id, "bad-totp", cancellationToken)
                .ConfigureAwait(false);

            return AuthErrors.InvalidCode();
        }

        var justEnrolled = !user.TotpEnabled;

        if (justEnrolled)
        {
            user.EnableTotp();
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await audit.RecordAsync(
                new AuditEntry
                {
                    Action = TwoFactorAudit.EnabledAction,
                    EntityType = SignInCoordinator.UserEntityType,
                    EntityId = user.Id.ToString(),
                    ActorId = user.Id,
                    ActorType = AuditActorType.Anonymous,
                    After = new { enrolledAtSignIn = true },
                },
                cancellationToken).ConfigureAwait(false);
        }

        return await signIn
            .CompleteAsync(user, command.Device, secondFactorSatisfied: true, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Stages a secret for a user who is already signed in.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="caller">The signed-in caller.</param>
/// <param name="protector">Encrypts the staged secret.</param>
/// <param name="settings">Supplies the store name.</param>
internal sealed class StartTwoFactorSetupCommandHandler(
    IdentityDbContext context,
    ICallerContext caller,
    SecretProtector protector,
    IStoreSettings settings) : ICommandHandler<StartTwoFactorSetupCommand, TwoFactorSetupResponse>
{
    public async Task<Result<TwoFactorSetupResponse>> HandleAsync(
        StartTwoFactorSetupCommand command,
        CancellationToken cancellationToken)
    {
        var user = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == caller.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return AuthErrors.UnknownUser();
        }

        if (user.TotpEnabled)
        {
            return Error.Conflict(
                "IDENTITY_TWO_FACTOR_ALREADY_ENABLED",
                "An authenticator is already enrolled. Remove it before enrolling another.");
        }

        return await BeginTwoFactorEnrolmentCommandHandler
            .StageAsync(context, protector, settings, user, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Turns on a second factor a signed-in user has just staged.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="caller">The signed-in caller.</param>
/// <param name="protector">Decrypts the staged secret.</param>
/// <param name="audit">Records the change.</param>
/// <param name="clock">The clock.</param>
internal sealed class EnableTwoFactorCommandHandler(
    IdentityDbContext context,
    ICallerContext caller,
    SecretProtector protector,
    IAuditLogger audit,
    IClock clock) : ICommandHandler<EnableTwoFactorCommand>
{
    public async Task<Result> HandleAsync(EnableTwoFactorCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == caller.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Result.Failure(AuthErrors.UnknownUser());
        }

        var secret = protector.Unprotect(user.TotpSecretEncrypted);

        if (secret is null || !Totp.Verify(secret, command.Code, clock.UtcNow))
        {
            return Result.Failure(AuthErrors.InvalidCode());
        }

        user.EnableTotp();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = TwoFactorAudit.EnabledAction,
                EntityType = SignInCoordinator.UserEntityType,
                EntityId = user.Id.ToString(),
                After = new { enrolledAtSignIn = false },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>
/// Removes a second factor, if the account's roles allow it to be removed.
/// </summary>
/// <remarks>
/// Both a password and a current code are required. A session token alone is not enough: the whole
/// value of the second factor is that holding the first one is insufficient, and a "disable 2FA"
/// button that only needs a live session hands that back.
/// </remarks>
/// <param name="context">The Identity data context.</param>
/// <param name="caller">The signed-in caller.</param>
/// <param name="access">Decides whether the second factor is mandatory for this user.</param>
/// <param name="hasher">Verifies the password.</param>
/// <param name="protector">Decrypts the secret.</param>
/// <param name="audit">Records the change.</param>
/// <param name="clock">The clock.</param>
internal sealed class DisableTwoFactorCommandHandler(
    IdentityDbContext context,
    ICallerContext caller,
    AccessResolver access,
    PasswordHasher hasher,
    SecretProtector protector,
    IAuditLogger audit,
    IClock clock) : ICommandHandler<DisableTwoFactorCommand>
{
    public async Task<Result> HandleAsync(DisableTwoFactorCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == caller.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Result.Failure(AuthErrors.UnknownUser());
        }

        var resolved = await access.ResolveAsync(user.Id, cancellationToken).ConfigureAwait(false);

        if (resolved.RequiresTwoFactor)
        {
            return Result.Failure(Error.Forbidden(
                "IDENTITY_TWO_FACTOR_MANDATORY",
                "A second factor is required for this account's roles and cannot be removed."));
        }

        if (!hasher.Verify(command.Password, user.PasswordHash, out _))
        {
            return Result.Failure(AuthErrors.InvalidCredentials());
        }

        var secret = protector.Unprotect(user.TotpSecretEncrypted);

        if (secret is null || !Totp.Verify(secret, command.Code, clock.UtcNow))
        {
            return Result.Failure(AuthErrors.InvalidCode());
        }

        user.DisableTotp();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = TwoFactorAudit.DisabledAction,
                EntityType = SignInCoordinator.UserEntityType,
                EntityId = user.Id.ToString(),
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
