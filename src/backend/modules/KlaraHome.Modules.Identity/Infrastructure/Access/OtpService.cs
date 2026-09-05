using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure.Persistence;
using KlaraHome.Modules.Identity.Infrastructure.Security;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Identity.Infrastructure.Access;

/// <summary>
/// Delivers a one-time code or a verification link.
/// </summary>
/// <remarks>
/// <para>
/// The seam the Notifications module fills at Step 8. Until it exists there is no SMS provider,
/// no DLT template registry and no email transport, and inventing a private one here would be
/// work thrown away — so the module states the dependency as an interface and ships an
/// implementation that is honest about what it does.
/// </para>
/// <para>
/// The code is passed to the dispatcher and to nothing else. It is never returned in a response,
/// never logged by the caller, and never stored except as a hash
/// (docs/07-security-compliance.md §1).
/// </para>
/// </remarks>
internal interface IOtpDispatcher
{
    /// <summary>Sends a code or link to its destination.</summary>
    /// <param name="destination">An E.164 mobile number or an email address.</param>
    /// <param name="channel">How to send it.</param>
    /// <param name="purpose">What it proves, which selects the template.</param>
    /// <param name="code">The plaintext code or token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DispatchAsync(
        string destination,
        OtpChannel channel,
        OtpPurpose purpose,
        string code,
        CancellationToken cancellationToken);
}

/// <summary>
/// The Development dispatcher: writes the code to the log so a developer can sign in, and refuses
/// to exist anywhere else.
/// </summary>
/// <remarks>
/// Registered only outside Production, and the module says so at startup rather than leaving it to
/// be discovered. Logging an OTP is precisely what §3's logging hygiene forbids — that is why this
/// class cannot be reached by a deployed host, and why replacing it is a Step 8 deliverable rather
/// than an optional improvement.
/// </remarks>
/// <param name="logger">Where the code is written.</param>
internal sealed partial class LoggingOtpDispatcher(ILogger<LoggingOtpDispatcher> logger) : IOtpDispatcher
{
    public Task DispatchAsync(
        string destination,
        OtpChannel channel,
        OtpPurpose purpose,
        string code,
        CancellationToken cancellationToken)
    {
        OtpDispatched(logger, purpose.ToString(), channel.ToString(), destination, code);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 1402, Level = LogLevel.Warning,
        Message = "DEVELOPMENT ONLY — {OtpPurpose} code for {OtpChannel} {OtpDestination} is {OtpCode}. "
                  + "The Notifications module replaces this dispatcher at Step 8.")]
    private static partial void OtpDispatched(
        ILogger logger,
        string otpPurpose,
        string otpChannel,
        string otpDestination,
        string otpCode);
}

/// <summary>Why a code could not be issued.</summary>
internal enum OtpIssueFailure
{
    /// <summary>The destination has asked for too many codes too recently.</summary>
    Throttled = 0,
}

/// <summary>
/// Issues and verifies one-time codes, with the throttling and attempt budget from
/// docs/07-security-compliance.md §1.
/// </summary>
/// <remarks>
/// Per-destination throttling lives here rather than in the edge rate limiter, and both are
/// needed: the limiter counts requests from one IP address, which does nothing about an attacker
/// spraying one number from a botnet, and this counts requests to one number, which does nothing
/// about an attacker enumerating numbers from one host.
/// </remarks>
/// <param name="context">The Identity data context.</param>
/// <param name="dispatcher">Delivers the code.</param>
/// <param name="options">Lifetime, length and throttle settings.</param>
/// <param name="clock">The clock.</param>
internal sealed class OtpService(
    IdentityDbContext context,
    IOtpDispatcher dispatcher,
    IOptions<AuthOptions> options,
    IClock clock)
{
    /// <summary>
    /// Issues a code and sends it, unless the destination is over its limit.
    /// </summary>
    /// <remarks>
    /// Any earlier open challenge for the same destination and purpose is superseded, so a user
    /// who taps "resend" is not left with two valid codes and no way to know which one the server
    /// will accept.
    /// </remarks>
    /// <param name="destination">The mobile number or email address.</param>
    /// <param name="channel">How to deliver it.</param>
    /// <param name="purpose">What it proves.</param>
    /// <param name="userId">The account it belongs to, when known.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<OtpIssueFailure?> IssueAsync(
        string destination,
        OtpChannel channel,
        OtpPurpose purpose,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var otp = options.Value.Otp;
        var now = clock.UtcNow;
        var windowStart = now.AddMinutes(-otp.ThrottleWindowMinutes);

        var recent = await context.OtpChallenges
            .Where(challenge => challenge.Destination == destination
                                && challenge.Purpose == purpose
                                && challenge.RequestedAt >= windowStart)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        if (recent >= otp.MaxRequestsPerDestination)
        {
            return OtpIssueFailure.Throttled;
        }

        var open = await context.OtpChallenges
            .Where(challenge => challenge.Destination == destination
                                && challenge.Purpose == purpose
                                && challenge.ConsumedAt == null
                                && challenge.ExpiresAt > now)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var superseded in open)
        {
            superseded.Supersede(now);
        }

        var isLink = purpose is OtpPurpose.PasswordReset or OtpPurpose.VerifyEmail;
        var code = isLink ? SecretHasher.NewOpaqueToken() : SecretHasher.NewNumericCode(otp.CodeLength);
        var lifetime = TimeSpan.FromMinutes(isLink ? otp.LinkLifetimeMinutes : otp.LifetimeMinutes);

        var issued = OtpChallenge.Issue(
            destination,
            channel,
            purpose,
            SecretHasher.Hash(code, destination),
            now,
            lifetime,
            userId);

        context.OtpChallenges.Add(issued);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await dispatcher
            .DispatchAsync(destination, channel, purpose, code, cancellationToken)
            .ConfigureAwait(false);

        return null;
    }

    /// <summary>
    /// Verifies a code against the newest open challenge for a destination and purpose, and
    /// consumes it on success.
    /// </summary>
    /// <remarks>
    /// A failed attempt spends one of the budget whether or not the destination has a challenge at
    /// all, so a caller cannot tell "no code was ever sent here" from "the code was wrong".
    /// </remarks>
    /// <param name="destination">The mobile number or email address.</param>
    /// <param name="purpose">What the code proves.</param>
    /// <param name="code">The code the user supplied.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<OtpChallenge?> VerifyAsync(
        string destination,
        OtpPurpose purpose,
        string code,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var otp = options.Value.Otp;
        var now = clock.UtcNow;

        var challenge = await context.OtpChallenges
            .Where(candidate => candidate.Destination == destination
                                && candidate.Purpose == purpose
                                && candidate.ConsumedAt == null)
            .OrderByDescending(candidate => candidate.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (challenge is null || !challenge.IsOpen(now, otp.MaxVerificationAttempts))
        {
            return null;
        }

        if (!SecretHasher.Verify(code.Trim(), destination, challenge.CodeHash))
        {
            challenge.RecordFailedAttempt();
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        challenge.Consume(now);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return challenge;
    }
}
