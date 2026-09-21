using System.Linq.Expressions;
using FluentValidation;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Identity.Application.Validation;
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

/// <summary>Signs in with an email address and a password. The admin surface's sign-in.</summary>
/// <param name="Email">The email address.</param>
/// <param name="Password">The password.</param>
/// <param name="Device">What the caller looks like, supplied by the endpoint.</param>
internal sealed record PasswordLoginCommand(string Email, string Password, DeviceInfo Device)
    : ICommand<SignInResult>;

/// <summary>Signs in with a mobile number and a password. The storefront's sign-in.</summary>
/// <param name="Mobile">The mobile number, in any form the validator can normalise to E.164.</param>
/// <param name="Password">The password.</param>
/// <param name="Device">What the caller looks like, supplied by the endpoint.</param>
internal sealed record MobilePasswordLoginCommand(string Mobile, string Password, DeviceInfo Device)
    : ICommand<SignInResult>;

/// <summary>Registers a shopper with a mobile number and a password.</summary>
/// <param name="Mobile">Their mobile number, which is what the account is keyed on.</param>
/// <param name="Password">The chosen password.</param>
/// <param name="Email">An optional email address, for receipts and a self-service password reset.</param>
/// <param name="MarketingConsent">Whether they opted in to marketing, unbundled from the sign-up.</param>
/// <param name="Device">What the caller looks like.</param>
internal sealed record RegisterCustomerCommand(
    string Mobile,
    string Password,
    string? Email,
    bool MarketingConsent,
    DeviceInfo Device) : ICommand<SignInResult>;

/// <summary>Asks for a password-reset link.</summary>
/// <param name="Email">The email address.</param>
internal sealed record ForgotPasswordCommand(string Email) : ICommand;

/// <summary>Sets a new password using a reset token.</summary>
/// <param name="Email">The address the link was sent to.</param>
/// <param name="Token">The token from the link.</param>
/// <param name="NewPassword">The new password.</param>
internal sealed record ResetPasswordCommand(string Email, string Token, string NewPassword) : ICommand;

/// <summary>Shared password rules (docs/07-security-compliance.md §1).</summary>
/// <remarks>
/// Length and a breached-password check, and nothing else. Composition rules — one capital, one
/// digit, one symbol — measurably push people towards <c>Password1!</c> and are explicitly ruled
/// out by §1 as "composition rules theatre".
/// </remarks>
/// <param name="options">Supplies the minimum length.</param>
/// <param name="breached">The breached-password list.</param>
internal sealed class PasswordRules(IOptions<AuthOptions> options, BreachedPasswords breached)
{
    /// <summary>Applies the rules to a password field.</summary>
    /// <typeparam name="T">The command being validated.</typeparam>
    /// <param name="rule">The rule builder for the password property.</param>
    public IRuleBuilderOptions<T, string> Apply<T>(IRuleBuilder<T, string> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule
            .NotEmpty()
            .MinimumLength(options.Value.Password.MinimumLength)
            .WithMessage($"Use at least {options.Value.Password.MinimumLength} characters.")
            .MaximumLength(256)
            .Must(password => !breached.IsBreached(password))
            .WithMessage("That password appears in known breach lists. Please choose another.");
    }
}

/// <summary>Rules for a password sign-in.</summary>
internal sealed class PasswordLoginValidator : AbstractValidator<PasswordLoginCommand>
{
    public PasswordLoginValidator()
    {
        // The login form checks that something was typed and nothing else. Applying the strength
        // rules here would tell an attacker which of their guesses were even worth trying.
        RuleFor(command => command.Email).NotEmpty().MaximumLength(320);
        RuleFor(command => command.Password).NotEmpty().MaximumLength(256);
    }
}

/// <summary>Rules for a mobile-number sign-in.</summary>
internal sealed class MobilePasswordLoginValidator : AbstractValidator<MobilePasswordLoginCommand>
{
    public MobilePasswordLoginValidator()
    {
        // The number's shape is checked, unlike the password's strength: a malformed number says
        // nothing about whether any account exists, and it has to normalise before it can be found.
        RuleFor(command => command.Mobile)
            .Must(IndianMobile.IsValid)
            .WithMessage("Enter a valid Indian mobile number.");

        RuleFor(command => command.Password).NotEmpty().MaximumLength(256);
    }
}

/// <summary>Rules for a shopper registration.</summary>
internal sealed class RegisterCustomerValidator : AbstractValidator<RegisterCustomerCommand>
{
    public RegisterCustomerValidator(PasswordRules passwords)
    {
        ArgumentNullException.ThrowIfNull(passwords);

        RuleFor(command => command.Mobile)
            .Must(IndianMobile.IsValid)
            .WithMessage("Enter a valid Indian mobile number.");

        passwords.Apply(RuleFor(command => command.Password));

        RuleFor(command => command.Email!)
            .MaximumLength(320)
            .Matches(IdentityFormats.Email()).WithMessage("Enter a valid email address.")
            .When(command => !string.IsNullOrWhiteSpace(command.Email));
    }
}

/// <summary>Rules for a reset request.</summary>
internal sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordValidator()
        => RuleFor(command => command.Email).NotEmpty().MaximumLength(320);
}

/// <summary>Rules for a reset.</summary>
internal sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator(PasswordRules passwords)
    {
        ArgumentNullException.ThrowIfNull(passwords);

        RuleFor(command => command.Email).NotEmpty().MaximumLength(320);
        RuleFor(command => command.Token).NotEmpty().MaximumLength(128);
        passwords.Apply(RuleFor(command => command.NewPassword));
    }
}

/// <summary>
/// Checks a password against the account an identifier names, and signs that user in.
/// </summary>
/// <remarks>
/// <para>
/// Shared by the email sign-in the admin surface uses and the mobile-number sign-in the storefront
/// uses. The two differ only in which column the identifier is matched against: the credential, the
/// lockout, the audit entry and the second-factor rule are identical, and what differs after that —
/// which permissions the token carries — is decided by the user's roles.
/// </para>
/// <para>
/// Every rejection returns the same error. A password verification is performed even when no
/// account matched, so the response time does not answer "does this account exist here" for an
/// attacker who is timing it (docs/07-security-compliance.md §3).
/// </para>
/// </remarks>
/// <param name="context">The Identity data context.</param>
/// <param name="hasher">Verifies and re-hashes the password.</param>
/// <param name="signIn">Completes the sign-in.</param>
/// <param name="options">Lockout settings.</param>
/// <param name="clock">The clock.</param>
internal abstract class PasswordSignInHandler(
    IdentityDbContext context,
    PasswordHasher hasher,
    SignInCoordinator signIn,
    IOptions<AuthOptions> options,
    IClock clock)
{
    /// <summary>
    /// A valid Argon2id hash of a value nobody holds, so an unknown identifier costs the same work
    /// as a known one. Computed once per handler, from a random secret that is then discarded.
    /// </summary>
    private readonly Lazy<string> _decoyHash = new(() => hasher.Hash(SecretHasher.NewOpaqueToken()));

    /// <summary>Signs in the user <paramref name="match"/> finds, if the password is theirs.</summary>
    /// <param name="identifier">The normalised identifier, as the failure log records it.</param>
    /// <param name="match">Finds the account the identifier names.</param>
    /// <param name="password">The password as typed.</param>
    /// <param name="device">What the caller looks like.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected async Task<Result<SignInResult>> SignInAsync(
        string identifier,
        Expression<Func<User, bool>> match,
        string password,
        DeviceInfo device,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var user = await context.Users
            .FirstOrDefaultAsync(match, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            hasher.Verify(password, _decoyHash.Value, out _);

            await signIn.RecordFailureAsync(identifier, null, "unknown-user", cancellationToken).ConfigureAwait(false);
            return AuthErrors.InvalidCredentials();
        }

        if (user.IsLockedOut(now))
        {
            await signIn
                .RecordFailureAsync(identifier, user.Id, "locked-out", cancellationToken)
                .ConfigureAwait(false);

            return AuthErrors.LockedOut(user.LockedUntil!.Value);
        }

        if (user.Status != UserStatus.Active
            || !hasher.Verify(password, user.PasswordHash, out var needsRehash))
        {
            var lockout = options.Value.Lockout;

            user.RecordLoginFailed(
                now,
                lockout.Threshold,
                TimeSpan.FromSeconds(lockout.BaseSeconds),
                TimeSpan.FromSeconds(lockout.MaximumSeconds));

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await signIn
                .RecordFailureAsync(identifier, user.Id, "bad-password", cancellationToken)
                .ConfigureAwait(false);

            return AuthErrors.InvalidCredentials();
        }

        if (needsRehash)
        {
            // The password was correct and its hash is weaker than the current setting. This is the
            // only moment the plaintext is available, so it is the only moment the cost can be
            // raised without asking the user to do anything.
            user.SetPasswordHash(hasher.Hash(password));
        }

        return await signIn
            .CompleteAsync(user, device, secondFactorSatisfied: false, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Signs a user in with an email address and a password.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="hasher">Verifies and re-hashes the password.</param>
/// <param name="signIn">Completes the sign-in.</param>
/// <param name="options">Lockout settings.</param>
/// <param name="clock">The clock.</param>
internal sealed class PasswordLoginCommandHandler(
    IdentityDbContext context,
    PasswordHasher hasher,
    SignInCoordinator signIn,
    IOptions<AuthOptions> options,
    IClock clock)
    : PasswordSignInHandler(context, hasher, signIn, options, clock),
        ICommandHandler<PasswordLoginCommand, SignInResult>
{
    public Task<Result<SignInResult>> HandleAsync(
        PasswordLoginCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var email = command.Email.Trim().ToLowerInvariant();

        return SignInAsync(
            email,
            candidate => candidate.Email == email,
            command.Password,
            command.Device,
            cancellationToken);
    }
}

/// <summary>
/// Signs a shopper in with a mobile number and a password.
/// </summary>
/// <remarks>
/// The bridge to one-time codes, not a replacement for them. The mobile number is the account from
/// registration onwards; until there is an SMS route (<see cref="IdentityFeatures.MobileOtpLogin"/>)
/// a password is what proves the caller holds it, and the day codes are switched on the same
/// account signs in with one instead, with nothing about the account having to change.
/// </remarks>
/// <param name="context">The Identity data context.</param>
/// <param name="hasher">Verifies and re-hashes the password.</param>
/// <param name="signIn">Completes the sign-in.</param>
/// <param name="options">Lockout settings.</param>
/// <param name="clock">The clock.</param>
internal sealed class MobilePasswordLoginCommandHandler(
    IdentityDbContext context,
    PasswordHasher hasher,
    SignInCoordinator signIn,
    IOptions<AuthOptions> options,
    IClock clock)
    : PasswordSignInHandler(context, hasher, signIn, options, clock),
        ICommandHandler<MobilePasswordLoginCommand, SignInResult>
{
    public Task<Result<SignInResult>> HandleAsync(
        MobilePasswordLoginCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var mobile = IndianMobile.Normalize(command.Mobile);

        return SignInAsync(
            mobile,
            candidate => candidate.Mobile == mobile,
            command.Password,
            command.Device,
            cancellationToken);
    }
}

/// <summary>Registers a shopper with a mobile number and a password.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="hasher">Hashes the chosen password.</param>
/// <param name="signIn">Signs the new customer in.</param>
/// <param name="access">Finds the customer role.</param>
/// <param name="otp">Sends the email verification link.</param>
/// <param name="flags">Decides whether there is anywhere to send it.</param>
/// <param name="clock">The clock.</param>
internal sealed class RegisterCustomerCommandHandler(
    IdentityDbContext context,
    PasswordHasher hasher,
    SignInCoordinator signIn,
    AccessResolver access,
    OtpService otp,
    Contracts.Platform.IFeatureFlags flags,
    IClock clock) : ICommandHandler<RegisterCustomerCommand, SignInResult>
{
    public async Task<Result<SignInResult>> HandleAsync(
        RegisterCustomerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var mobile = IndianMobile.Normalize(command.Mobile);
        var email = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim().ToLowerInvariant();

        var taken = await context.Users
            .AnyAsync(
                candidate => candidate.Mobile == mobile || (email != null && candidate.Email == email),
                cancellationToken)
            .ConfigureAwait(false);

        if (taken)
        {
            // The one place the specification's uniform-response rule gives way: a registration
            // form has to say why it refused, or the person cannot proceed. The mitigation is that
            // the same fact is reachable by simply trying to register, which is what an attacker
            // would do anyway — unlike a login form, where the same leak costs nothing to hide.
            return Error.Conflict(
                "IDENTITY_ACCOUNT_EXISTS",
                "An account already exists for that mobile number or email address. Try signing in.");
        }

        var now = clock.UtcNow;

        var user = User.RegisterCustomer(mobile, email);
        user.SetPasswordHash(hasher.Hash(command.Password));
        context.Users.Add(user);

        var profile = CustomerProfile.For(user.Id, ReferralCodes.New());
        profile.SetMarketingConsent(command.MarketingConsent, now);
        context.CustomerProfiles.Add(profile);

        var customerRole = await access
            .FindRoleAsync(Infrastructure.Seeding.SystemRoles.Customer, cancellationToken)
            .ConfigureAwait(false);

        if (customerRole is not null)
        {
            user.GrantRole(customerRole.Id);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The mobile number stays unverified here. Proving it takes an SMS, and the day there is an
        // SMS route one-time codes replace the password outright (IdentityFeatures.MobileOtpLogin).
        //
        // An email address, if one was given, gets its link. Registration succeeds either way: with
        // email delivery off the address simply stays unverified, which is a state the account model
        // already has and the storefront already shows — not a reason to refuse somebody an account.
        var canVerify = email is not null && await flags
            .IsEnabledAsync(IdentityFeatures.EmailVerification, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (canVerify)
        {
            await otp
                .IssueAsync(email!, OtpChannel.Email, OtpPurpose.VerifyEmail, user.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        return await signIn
            .CompleteAsync(user, command.Device, secondFactorSatisfied: false, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Sends a password-reset link, and says the same thing whether or not the address is known.
/// </summary>
/// <param name="context">The Identity data context.</param>
/// <param name="otp">Issues and delivers the link.</param>
internal sealed class ForgotPasswordCommandHandler(IdentityDbContext context, OtpService otp)
    : ICommandHandler<ForgotPasswordCommand>
{
    public async Task<Result> HandleAsync(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var email = command.Email.Trim().ToLowerInvariant();

        var userId = await context.Users
            .Where(user => user.Email == email && user.Status == UserStatus.Active)
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (userId is not null)
        {
            await otp
                .IssueAsync(email, OtpChannel.Email, OtpPurpose.PasswordReset, userId, cancellationToken)
                .ConfigureAwait(false);
        }

        // Success either way, including when the throttle refused to send. An address that is not
        // registered here must be indistinguishable from one that is.
        return Result.Success();
    }
}

/// <summary>
/// Sets a new password from a reset link, and signs every other device out.
/// </summary>
/// <remarks>
/// Revoking the other sessions is the point of the flow, not a courtesy. Somebody resetting a
/// password they have lost control of gains nothing if the party who took it keeps a live session.
/// </remarks>
/// <param name="context">The Identity data context.</param>
/// <param name="otp">Verifies the reset token.</param>
/// <param name="hasher">Hashes the new password.</param>
/// <param name="sessions">Revokes the other sessions.</param>
/// <param name="audit">Records the change.</param>
internal sealed class ResetPasswordCommandHandler(
    IdentityDbContext context,
    OtpService otp,
    PasswordHasher hasher,
    SessionService sessions,
    Contracts.Platform.IAuditLogger audit) : ICommandHandler<ResetPasswordCommand>
{
    /// <summary>The audited action for a completed password reset.</summary>
    public const string AuditAction = "identity.password.reset";

    public async Task<Result> HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var email = command.Email.Trim().ToLowerInvariant();

        var challenge = await otp
            .VerifyAsync(email, OtpPurpose.PasswordReset, command.Token, cancellationToken)
            .ConfigureAwait(false);

        if (challenge is null)
        {
            return Result.Failure(AuthErrors.InvalidCode());
        }

        var user = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Email == email, cancellationToken)
            .ConfigureAwait(false);

        if (user is null || user.Status != UserStatus.Active)
        {
            return Result.Failure(AuthErrors.InvalidCode());
        }

        user.SetPasswordHash(hasher.Hash(command.NewPassword));
        user.Unlock();

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await sessions
            .RevokeAllAsync(user.Id, SessionEndReason.CredentialChanged, null, cancellationToken)
            .ConfigureAwait(false);

        await audit.RecordAsync(
            new Contracts.Platform.AuditEntry
            {
                Action = AuditAction,
                EntityType = SignInCoordinator.UserEntityType,
                EntityId = user.Id.ToString(),
                ActorId = user.Id,
                ActorType = Contracts.Platform.AuditActorType.Anonymous,
                After = new { sessionsRevoked = true },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
