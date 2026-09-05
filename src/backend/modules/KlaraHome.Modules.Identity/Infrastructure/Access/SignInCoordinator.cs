using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Identity.Application.Authentication;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure.Security;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Identity.Infrastructure.Access;

/// <summary>
/// The last step every sign-in shares: decide whether a second factor is owed, and if not, start
/// the session.
/// </summary>
/// <remarks>
/// Every way into the system — a customer's OTP, a staff password, a completed 2FA prompt — ends
/// here. That is deliberate: the mandatory-2FA rule, the session record and the login audit entry
/// have to hold for all of them, and a rule enforced in three places is a rule that will shortly
/// hold in two.
/// </remarks>
/// <param name="sessions">Starts sessions and issues tokens.</param>
/// <param name="access">Resolves roles and permissions.</param>
/// <param name="tokens">Mints the pending-sign-in challenge.</param>
/// <param name="audit">Records the login (docs/07-security-compliance.md §7).</param>
internal sealed class SignInCoordinator(
    SessionService sessions,
    AccessResolver access,
    TokenIssuer tokens,
    IAuditLogger audit)
{
    /// <summary>The audited action for a completed sign-in.</summary>
    public const string LoginSucceededAction = "identity.login.succeeded";

    /// <summary>The audited action for a rejected credential.</summary>
    public const string LoginFailedAction = "identity.login.failed";

    /// <summary>The entity type those actions are recorded against.</summary>
    public const string UserEntityType = "User";

    /// <summary>
    /// Completes a sign-in whose credential has already been accepted.
    /// </summary>
    /// <param name="user">The authenticated user.</param>
    /// <param name="device">What the caller looks like.</param>
    /// <param name="secondFactorSatisfied">
    /// Whether a second factor has just been proved. False for a first-step credential, true when
    /// this call comes from the 2FA endpoint.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<SignInResult>> CompleteAsync(
        User user,
        DeviceInfo device,
        bool secondFactorSatisfied,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var resolved = await access.ResolveAsync(user.Id, cancellationToken).ConfigureAwait(false);

        if (!secondFactorSatisfied)
        {
            if (user.TotpEnabled)
            {
                return SignInResult.Challenged(
                    TwoFactorChallengeResponse.CodeRequired,
                    tokens.IssueTwoFactorChallenge(user.Id));
            }

            if (resolved.RequiresTwoFactor)
            {
                // The account holds a role that cannot operate without a second factor and has not
                // enrolled one. Refusing outright would strand the first administrator of a new
                // deployment, so the challenge carries them to enrolment instead.
                return SignInResult.Challenged(
                    TwoFactorChallengeResponse.EnrolmentRequired,
                    tokens.IssueTwoFactorChallenge(user.Id));
            }
        }

        var issued = await sessions.StartAsync(user, device, cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = LoginSucceededAction,
                EntityType = UserEntityType,
                EntityId = user.Id.ToString(),
                ActorId = user.Id,
                ActorType = ActorTypeOf(user.UserType),
                After = new { sessionId = issued.SessionId, device = device.Device },
            },
            cancellationToken).ConfigureAwait(false);

        return SignInResult.Signed(issued, user, resolved);
    }

    /// <summary>
    /// Records a rejected credential.
    /// </summary>
    /// <remarks>
    /// Audited even when no account matched, with the identifier that was tried and no actor: a
    /// spray across a thousand addresses that never existed is exactly the pattern the audit trail
    /// is meant to make visible, and it leaves no trace anywhere else.
    /// </remarks>
    /// <param name="identifier">The mobile number or email address that was offered.</param>
    /// <param name="userId">The account, when one matched.</param>
    /// <param name="reason">A short machine-readable reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RecordFailureAsync(
        string identifier,
        Guid? userId,
        string reason,
        CancellationToken cancellationToken)
        => audit.RecordAsync(
            new AuditEntry
            {
                Action = LoginFailedAction,
                EntityType = UserEntityType,
                EntityId = userId?.ToString(),
                ActorId = userId,
                // Anonymous whether or not an account matched: nobody has authenticated, and
                // recording a failed attempt as the account's own action would put an act the
                // account holder did not perform into their history.
                ActorType = AuditActorType.Anonymous,
                After = new { identifier = Mask(identifier), reason },
            },
            cancellationToken);

    /// <summary>
    /// Masks an identifier for the audit trail: enough to recognise a pattern, not enough to be a
    /// list of customer contact details for anyone who can read the table
    /// (docs/07-security-compliance.md §3).
    /// </summary>
    /// <param name="identifier">The mobile number or email address.</param>
    internal static string Mask(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return string.Empty;
        }

        var at = identifier.IndexOf('@', StringComparison.Ordinal);

        if (at > 0)
        {
            var local = identifier[..at];
            var shown = local.Length <= 2 ? local[..1] : local[..2];
            return $"{shown}***{identifier[at..]}";
        }

        return identifier.Length <= 4
            ? "***"
            : $"***{identifier[^4..]}";
    }

    private static AuditActorType ActorTypeOf(UserType userType) => userType switch
    {
        UserType.Customer => AuditActorType.Customer,
        UserType.Vendor => AuditActorType.VendorUser,
        UserType.Staff => AuditActorType.StaffUser,
        _ => AuditActorType.System,
    };
}
