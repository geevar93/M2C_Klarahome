using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Identity.Application.Authentication;
using KlaraHome.Modules.Identity.Application.Validation;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure.Access;
using KlaraHome.Modules.Identity.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Identity.Application.Account;

/// <summary>The caller's own account.</summary>
internal sealed record GetMeQuery : IQuery<MeResponse>;

/// <summary>Updates the caller's own details.</summary>
/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Family name.</param>
/// <param name="DateOfBirth">Date of birth.</param>
/// <param name="Gender">Self-declared gender.</param>
/// <param name="Gstin">Default GSTIN for B2B invoices.</param>
/// <param name="MarketingConsent">Whether marketing is opted into.</param>
internal sealed record UpdateMeCommand(
    string? FirstName,
    string? LastName,
    DateOnly? DateOfBirth,
    string? Gender,
    string? Gstin,
    bool MarketingConsent) : ICommand<MeResponse>;

/// <summary>Asks for a code or link that proves the caller owns their email or mobile number.</summary>
/// <param name="Channel">Which identifier to verify.</param>
internal sealed record RequestVerificationCommand(OtpChannel Channel) : ICommand;

/// <summary>Proves an email address or mobile number with the code or token that was sent.</summary>
/// <param name="Channel">Which identifier is being proved.</param>
/// <param name="Code">The code or link token.</param>
internal sealed record ConfirmVerificationCommand(OtpChannel Channel, string Code) : ICommand;

/// <summary>The caller's account, as the storefront and the admin app both render it.</summary>
/// <param name="User">Who they are, and what they may do.</param>
/// <param name="Profile">Their shopper details, or null for a staff or vendor user.</param>
internal sealed record MeResponse(AuthenticatedUserResponse User, CustomerProfileResponse? Profile);

/// <summary>A shopper's own details.</summary>
/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Family name.</param>
/// <param name="DateOfBirth">Date of birth.</param>
/// <param name="Gender">Self-declared gender.</param>
/// <param name="Gstin">Default GSTIN.</param>
/// <param name="MarketingConsent">Whether marketing is currently opted into.</param>
/// <param name="MarketingConsentAt">When that decision was recorded (DPDP §6).</param>
/// <param name="ReferralCode">The code they share to refer others.</param>
internal sealed record CustomerProfileResponse(
    string? FirstName,
    string? LastName,
    DateOnly? DateOfBirth,
    string? Gender,
    string? Gstin,
    bool MarketingConsent,
    DateTimeOffset? MarketingConsentAt,
    string ReferralCode);

/// <summary>Rules for a profile update.</summary>
internal sealed class UpdateMeValidator : AbstractValidator<UpdateMeCommand>
{
    public UpdateMeValidator()
    {
        RuleFor(command => command.FirstName).MaximumLength(100);
        RuleFor(command => command.LastName).MaximumLength(100);
        RuleFor(command => command.Gender).MaximumLength(32);

        RuleFor(command => command.Gstin!)
            .Matches(IdentityFormats.Gstin())
            .When(command => !string.IsNullOrWhiteSpace(command.Gstin))
            .WithMessage("A GSTIN is 15 characters, for example 27AAPFU0939F1ZV.");

        RuleFor(command => command.DateOfBirth!.Value)
            .LessThan(_ => DateOnly.FromDateTime(DateTime.UtcNow))
            .When(command => command.DateOfBirth is not null)
            .WithMessage("A date of birth is in the past.");
    }
}

/// <summary>Reads the caller's own account.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="caller">The signed-in caller.</param>
/// <param name="access">Resolves their roles and permissions.</param>
internal sealed class GetMeQueryHandler(
    IdentityDbContext context,
    ICallerContext caller,
    AccessResolver access) : IQueryHandler<GetMeQuery, MeResponse>
{
    public async Task<Result<MeResponse>> HandleAsync(GetMeQuery query, CancellationToken cancellationToken)
    {
        if (caller.UserId is null)
        {
            return Error.Unauthorized();
        }

        var user = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == caller.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return AuthErrors.UnknownUser();
        }

        var resolved = await access.ResolveAsync(user.Id, cancellationToken).ConfigureAwait(false);

        var profile = await context.CustomerProfiles
            .FirstOrDefaultAsync(candidate => candidate.UserId == user.Id, cancellationToken)
            .ConfigureAwait(false);

        return new MeResponse(
            AuthenticatedUserResponse.From(user, resolved),
            profile is null ? null : Project(profile));
    }

    /// <summary>Projects a profile onto its response.</summary>
    /// <param name="profile">The profile.</param>
    internal static CustomerProfileResponse Project(CustomerProfile profile)
        => new(
            profile.FirstName,
            profile.LastName,
            profile.DateOfBirth,
            profile.Gender,
            profile.Gstin,
            profile.MarketingConsentAt is not null,
            profile.MarketingConsentAt,
            profile.ReferralCode);
}

/// <summary>Updates the caller's own details.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="caller">The signed-in caller.</param>
/// <param name="access">Resolves their roles and permissions for the response.</param>
/// <param name="clock">The clock, for the consent timestamp.</param>
internal sealed class UpdateMeCommandHandler(
    IdentityDbContext context,
    ICallerContext caller,
    AccessResolver access,
    IClock clock) : ICommandHandler<UpdateMeCommand, MeResponse>
{
    public async Task<Result<MeResponse>> HandleAsync(
        UpdateMeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (caller.UserId is null)
        {
            return Error.Unauthorized();
        }

        var user = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == caller.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return AuthErrors.UnknownUser();
        }

        var profile = await context.CustomerProfiles
            .FirstOrDefaultAsync(candidate => candidate.UserId == user.Id, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
        {
            // Staff and vendor users have no shopper profile, and this endpoint is the shopper's
            // account page. There is nothing here for them to edit.
            return Error.NotFound(
                "IDENTITY_PROFILE_NOT_FOUND",
                "This account has no customer profile.");
        }

        profile.Update(
            command.FirstName,
            command.LastName,
            command.DateOfBirth,
            command.Gender,
            string.IsNullOrWhiteSpace(command.Gstin) ? null : command.Gstin.Trim().ToUpperInvariant());

        // Consent is only re-timestamped when it actually changes, so a profile edit does not
        // silently rewrite the date somebody agreed to marketing (DPDP §6).
        var alreadyConsented = profile.MarketingConsentAt is not null;
        if (alreadyConsented != command.MarketingConsent)
        {
            profile.SetMarketingConsent(command.MarketingConsent, clock.UtcNow);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var resolved = await access.ResolveAsync(user.Id, cancellationToken).ConfigureAwait(false);
        return new MeResponse(AuthenticatedUserResponse.From(user, resolved), GetMeQueryHandler.Project(profile));
    }
}

/// <summary>Sends the caller a code or link proving an identifier is theirs.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="caller">The signed-in caller.</param>
/// <param name="otp">Issues and delivers the code.</param>
/// <param name="flags">Decides whether the channel this asks for can deliver anything.</param>
internal sealed class RequestVerificationCommandHandler(
    IdentityDbContext context,
    ICallerContext caller,
    OtpService otp,
    IFeatureFlags flags) : ICommandHandler<RequestVerificationCommand>
{
    public async Task<Result> HandleAsync(
        RequestVerificationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == caller.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Result.Failure(AuthErrors.UnknownUser());
        }

        var (destination, purpose) = command.Channel == OtpChannel.Email
            ? (user.Email, OtpPurpose.VerifyEmail)
            : (user.Mobile, OtpPurpose.VerifyMobile);

        // Gated here rather than on the endpoint, because one route serves two channels and each
        // needs a different paid provider — the email half and the SMS half go dark separately.
        // Checked before the destination, because "we cannot send anything on this channel" is
        // true whether or not the caller has an address to send to.
        var channelFlag = command.Channel == OtpChannel.Email
            ? Infrastructure.IdentityFeatures.EmailVerification
            : Infrastructure.IdentityFeatures.MobileOtpLogin;

        if (!await flags.IsEnabledAsync(channelFlag, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound(
                "FEATURE_DISABLED",
                $"The feature '{channelFlag}' is not enabled on this store."));
        }

        if (string.IsNullOrWhiteSpace(destination))
        {
            return Result.Failure(Error.Validation(
                "IDENTITY_NO_DESTINATION",
                "There is nothing to verify: add the address or number to your account first."));
        }

        var failure = await otp
            .IssueAsync(destination, command.Channel, purpose, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return failure == OtpIssueFailure.Throttled
            ? Result.Failure(AuthErrors.OtpThrottled())
            : Result.Success();
    }
}

/// <summary>Marks an identifier verified once its code has been presented.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="caller">The signed-in caller.</param>
/// <param name="otp">Verifies the code.</param>
/// <param name="audit">Records the verification.</param>
/// <param name="clock">The clock.</param>
internal sealed class ConfirmVerificationCommandHandler(
    IdentityDbContext context,
    ICallerContext caller,
    OtpService otp,
    IAuditLogger audit,
    IClock clock) : ICommandHandler<ConfirmVerificationCommand>
{
    /// <summary>The audited action for a proved identifier.</summary>
    public const string AuditAction = "identity.contact.verified";

    public async Task<Result> HandleAsync(
        ConfirmVerificationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == caller.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Result.Failure(AuthErrors.UnknownUser());
        }

        var (destination, purpose) = command.Channel == OtpChannel.Email
            ? (user.Email, OtpPurpose.VerifyEmail)
            : (user.Mobile, OtpPurpose.VerifyMobile);

        if (string.IsNullOrWhiteSpace(destination))
        {
            return Result.Failure(AuthErrors.InvalidCode());
        }

        var challenge = await otp
            .VerifyAsync(destination, purpose, command.Code, cancellationToken)
            .ConfigureAwait(false);

        if (challenge is null)
        {
            return Result.Failure(AuthErrors.InvalidCode());
        }

        var now = clock.UtcNow;

        if (command.Channel == OtpChannel.Email)
        {
            user.MarkEmailVerified(now);
        }
        else
        {
            user.MarkMobileVerified(now);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = "User",
                EntityId = user.Id.ToString(),
                After = new { channel = command.Channel.ToString() },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
