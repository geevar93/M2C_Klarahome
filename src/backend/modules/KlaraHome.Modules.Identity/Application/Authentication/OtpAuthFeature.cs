using FluentValidation;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Identity.Application.Validation;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure.Access;
using KlaraHome.Modules.Identity.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Primitives;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Identity.Application.Authentication;

/// <summary>Asks for a one-time code to be sent to a mobile number.</summary>
/// <param name="Mobile">The mobile number, in any form the validator can normalise to E.164.</param>
internal sealed record RequestOtpCommand(string Mobile) : ICommand<OtpRequestedResponse>;

/// <summary>
/// What the caller is told after asking for a code.
/// </summary>
/// <remarks>
/// It says only that a code was sent and how long it lasts. Whether the number belongs to an
/// existing customer is exactly the fact this endpoint must not reveal
/// (docs/07-security-compliance.md §3), and it is also not something the caller needs: the same
/// verify call registers a new customer or signs in an existing one.
/// </remarks>
/// <param name="ExpiresInSeconds">How long the code is valid for.</param>
/// <param name="CodeLength">How many digits to ask the user for.</param>
internal sealed record OtpRequestedResponse(int ExpiresInSeconds, int CodeLength);

/// <summary>Presents a one-time code, which signs in an existing customer or registers a new one.</summary>
/// <param name="Mobile">The mobile number the code was sent to.</param>
/// <param name="Code">The code the customer typed.</param>
/// <param name="Device">What the caller looks like, supplied by the endpoint.</param>
internal sealed record VerifyOtpCommand(string Mobile, string Code, DeviceInfo Device)
    : ICommand<SignInResult>;

/// <summary>Rules for an OTP request.</summary>
internal sealed class RequestOtpValidator : AbstractValidator<RequestOtpCommand>
{
    public RequestOtpValidator()
        => RuleFor(command => command.Mobile)
            .Must(IndianMobile.IsValid)
            .WithMessage("Enter a valid Indian mobile number.");
}

/// <summary>Rules for an OTP verification.</summary>
internal sealed class VerifyOtpValidator : AbstractValidator<VerifyOtpCommand>
{
    public VerifyOtpValidator()
    {
        RuleFor(command => command.Mobile)
            .Must(IndianMobile.IsValid)
            .WithMessage("Enter a valid Indian mobile number.");

        RuleFor(command => command.Code)
            .NotEmpty()
            .Matches("^[0-9]{4,10}$").WithMessage("The code is a short sequence of digits.");
    }
}

/// <summary>
/// Sends a one-time code to a mobile number.
/// </summary>
/// <remarks>
/// The response is identical whether the number is known, unknown, or currently throttled by the
/// per-destination limit — with one exception: a throttled request answers 429, because a client
/// that is told nothing will simply ask again, and a silent success would make "resend" appear
/// broken to a legitimate user.
/// </remarks>
/// <param name="context">The Identity data context.</param>
/// <param name="otp">Issues and delivers the code.</param>
/// <param name="options">Supplies the lifetime reported to the caller.</param>
internal sealed class RequestOtpCommandHandler(
    IdentityDbContext context,
    OtpService otp,
    Microsoft.Extensions.Options.IOptions<Infrastructure.AuthOptions> options)
    : ICommandHandler<RequestOtpCommand, OtpRequestedResponse>
{
    public async Task<Result<OtpRequestedResponse>> HandleAsync(
        RequestOtpCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var mobile = IndianMobile.Normalize(command.Mobile);

        var userId = await context.Users
            .Where(user => user.Mobile == mobile)
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var failure = await otp
            .IssueAsync(mobile, OtpChannel.Sms, OtpPurpose.Login, userId, cancellationToken)
            .ConfigureAwait(false);

        if (failure == OtpIssueFailure.Throttled)
        {
            return AuthErrors.OtpThrottled();
        }

        var settings = options.Value.Otp;
        return new OtpRequestedResponse(settings.LifetimeMinutes * 60, settings.CodeLength);
    }
}

/// <summary>
/// Verifies a one-time code and signs the customer in, registering them if the number is new.
/// </summary>
/// <remarks>
/// Registration on first successful verification, rather than a separate sign-up step, is what
/// makes mobile-OTP the primary credential rather than a convenience layered over an account the
/// customer had to create first. A verified code proves the number; there is nothing further to
/// ask for.
/// </remarks>
/// <param name="context">The Identity data context.</param>
/// <param name="otp">Verifies the code.</param>
/// <param name="signIn">Completes the sign-in.</param>
/// <param name="access">Finds the customer role to grant a new account.</param>
/// <param name="clock">The clock.</param>
internal sealed class VerifyOtpCommandHandler(
    IdentityDbContext context,
    OtpService otp,
    SignInCoordinator signIn,
    AccessResolver access,
    KlaraHome.SharedKernel.Time.IClock clock)
    : ICommandHandler<VerifyOtpCommand, SignInResult>
{
    public async Task<Result<SignInResult>> HandleAsync(
        VerifyOtpCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var mobile = IndianMobile.Normalize(command.Mobile);

        var challenge = await otp
            .VerifyAsync(mobile, OtpPurpose.Login, command.Code, cancellationToken)
            .ConfigureAwait(false);

        if (challenge is null)
        {
            await signIn
                .RecordFailureAsync(mobile, userId: null, "otp-invalid", cancellationToken)
                .ConfigureAwait(false);

            return AuthErrors.InvalidCode();
        }

        var user = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Mobile == mobile, cancellationToken)
            .ConfigureAwait(false);

        var now = clock.UtcNow;

        if (user is null)
        {
            user = User.RegisterCustomer(mobile);
            user.MarkMobileVerified(now);
            context.Users.Add(user);

            context.CustomerProfiles.Add(CustomerProfile.For(user.Id, ReferralCodes.New()));

            var customerRole = await access
                .FindRoleAsync(Infrastructure.Seeding.SystemRoles.Customer, cancellationToken)
                .ConfigureAwait(false);

            if (customerRole is not null)
            {
                user.GrantRole(customerRole.Id);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (user.Status != UserStatus.Active)
            {
                await signIn
                    .RecordFailureAsync(mobile, user.Id, "account-not-active", cancellationToken)
                    .ConfigureAwait(false);

                return AuthErrors.InvalidCredentials();
            }

            if (user.MobileVerifiedAt is null)
            {
                // The code proves the number, whatever the row said before it was presented.
                user.MarkMobileVerified(now);
            }
        }

        return await signIn
            .CompleteAsync(user, command.Device, secondFactorSatisfied: false, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Generates the short code a customer shares to refer others.
/// </summary>
/// <remarks>
/// Crockford's base32 alphabet without I, L, O and U: the code is read aloud and retyped, and
/// those four are the characters that get confused with 1, 0 and each other.
/// </remarks>
internal static class ReferralCodes
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Mints a code. Collisions are caught by the unique index and retried by the caller.</summary>
    public static string New()
    {
        Span<char> code = stackalloc char[8];

        for (var index = 0; index < code.Length; index++)
        {
            code[index] = Alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(code);
    }
}
