using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure.Persistence;
using KlaraHome.Modules.Identity.Infrastructure.Security;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Identity.Infrastructure.Access;

/// <summary>What the caller looked like when a session was started or refreshed.</summary>
/// <param name="Device">A readable device description, derived from the user agent.</param>
/// <param name="IpAddress">The originating address.</param>
internal readonly record struct DeviceInfo(string? Device, string? IpAddress);

/// <summary>
/// Starts, refreshes and ends sessions, and owns refresh-token rotation
/// (docs/07-security-compliance.md §1).
/// </summary>
/// <remarks>
/// This is the only place a refresh token is minted or accepted. Rotation, reuse detection and
/// revocation are one mechanism, and splitting them across the handlers that happen to need each
/// one is how a system ends up with a refresh path that rotates and a logout path that does not.
/// </remarks>
/// <param name="context">The Identity data context.</param>
/// <param name="tokens">Mints access tokens.</param>
/// <param name="access">Resolves what the user may do.</param>
/// <param name="options">Token lifetimes.</param>
/// <param name="clock">The clock.</param>
/// <param name="logger">Reports reuse detection, which is a security event.</param>
internal sealed partial class SessionService(
    IdentityDbContext context,
    TokenIssuer tokens,
    AccessResolver access,
    IOptions<AuthOptions> options,
    IClock clock,
    ILogger<SessionService> logger)
{
    /// <summary>Starts a session and issues its first token pair.</summary>
    /// <param name="user">The user signing in.</param>
    /// <param name="device">What the caller looked like.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IssuedTokens> StartAsync(User user, DeviceInfo device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = clock.UtcNow;
        var resolved = await access.ResolveAsync(user.Id, cancellationToken).ConfigureAwait(false);

        var session = UserSession.Start(user.Id, device.Device, device.IpAddress, now);
        context.Sessions.Add(session);

        var issued = tokens.Issue(user, session.Id, resolved.Permissions, resolved.VendorId);
        StoreRefreshToken(session.Id, user.Id, issued.RefreshToken, now);

        user.RecordLoginSucceeded(now);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return issued;
    }

    /// <summary>
    /// Starts a support session that acts as <paramref name="target"/>
    /// (docs/07-security-compliance.md §2).
    /// </summary>
    /// <remarks>
    /// No refresh token is stored, deliberately. The access token carries the whole window and
    /// there is nothing to exchange when it runs out, which is the only way "time-boxed" survives
    /// contact with a client that refreshes automatically.
    /// </remarks>
    /// <param name="target">The user being acted as.</param>
    /// <param name="operatorId">The support user doing the acting.</param>
    /// <param name="reason">Why, recorded on the session and in the audit trail.</param>
    /// <param name="device">What the operator's caller looked like.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ImpersonationTokens> StartImpersonationAsync(
        User target,
        Guid operatorId,
        string reason,
        DeviceInfo device,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var now = clock.UtcNow;
        var expiresAt = now.AddMinutes(options.Value.Impersonation.WindowMinutes);
        var resolved = await access.ResolveAsync(target.Id, cancellationToken).ConfigureAwait(false);

        var session = UserSession.StartImpersonation(
            target.Id,
            operatorId,
            reason,
            device.Device,
            device.IpAddress,
            now,
            expiresAt);

        context.Sessions.Add(session);

        var issued = tokens.Issue(
            target,
            session.Id,
            resolved.Permissions,
            resolved.VendorId,
            new ImpersonationStamp(operatorId, expiresAt));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ImpersonationStarted(logger, operatorId, target.Id, session.Id);
        return new ImpersonationTokens(issued.AccessToken, expiresAt, session.Id, resolved);
    }

    /// <summary>Ends one impersonated session, and reports what it was.</summary>
    /// <remarks>
    /// Returns the row rather than a boolean because the caller has to audit what it stopped —
    /// which user was being acted as, and why — and the row is the only place that is recorded.
    /// </remarks>
    /// <param name="sessionId">The impersonated session.</param>
    /// <param name="operatorId">The support user; one operator cannot end another's impersonation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ImpersonatedSession?> EndImpersonationAsync(
        Guid sessionId,
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        var session = await context.Sessions
            .FirstOrDefaultAsync(
                candidate => candidate.Id == sessionId
                             && candidate.ImpersonatedByUserId == operatorId,
                cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            return null;
        }

        var ended = new ImpersonatedSession(
            session.Id,
            session.UserId,
            operatorId,
            session.ImpersonationReason ?? string.Empty,
            session.StartedAt);

        if (session.IsActive)
        {
            await RevokeSessionAsync(session, SessionEndReason.ImpersonationEnded, cancellationToken)
                .ConfigureAwait(false);
        }

        return ended;
    }

    /// <summary>
    /// Exchanges a refresh token for a new pair, rotating it.
    /// </summary>
    /// <remarks>
    /// A token that has already been rotated is not merely rejected: two parties holding the same
    /// refresh secret means one of them stole it, and there is no way to tell which is which. The
    /// whole session is revoked, so the legitimate user is signed out and has to authenticate
    /// again — which is the only outcome that does not leave the thief with a working session.
    /// </remarks>
    /// <param name="presented">The refresh secret from the cookie.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RefreshOutcome> RefreshAsync(
        string? presented,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(presented))
        {
            return RefreshOutcome.Rejected;
        }

        var now = clock.UtcNow;
        var hash = HashOf(presented);

        var existing = await context.RefreshTokens
            .FirstOrDefaultAsync(token => token.TokenHash == hash, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return RefreshOutcome.Rejected;
        }

        var session = await context.Sessions
            .FirstOrDefaultAsync(candidate => candidate.Id == existing.SessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session is null || !session.IsActive)
        {
            return RefreshOutcome.Rejected;
        }

        if (session.IsImpersonated)
        {
            // Unreachable today — StartImpersonationAsync stores no refresh token — and checked
            // anyway, because the day something does store one is the day a support session stops
            // being time-boxed and nobody notices.
            return RefreshOutcome.Rejected;
        }

        if (!existing.IsUsable(now))
        {
            // Already spent, or revoked. If it was spent, the chain is compromised.
            if (existing.ReplacedById is not null)
            {
                await RevokeSessionAsync(session, SessionEndReason.TokenReuseDetected, cancellationToken)
                    .ConfigureAwait(false);

                RefreshTokenReused(logger, session.Id, existing.UserId);
                return RefreshOutcome.Reused;
            }

            return RefreshOutcome.Rejected;
        }

        var user = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == existing.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null || user.Status != UserStatus.Active)
        {
            await RevokeSessionAsync(session, SessionEndReason.AccountClosed, cancellationToken)
                .ConfigureAwait(false);

            return RefreshOutcome.Rejected;
        }

        var resolved = await access.ResolveAsync(user.Id, cancellationToken).ConfigureAwait(false);
        var issued = tokens.Issue(user, session.Id, resolved.Permissions, resolved.VendorId);

        var replacement = StoreRefreshToken(session.Id, user.Id, issued.RefreshToken, now);
        existing.Rotate(now, replacement.Id);

        // Touched, but never relabelled. A session is one device; a user agent that changes
        // mid-session is something the user should be able to see, not something to overwrite.
        session.Touch(now);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return RefreshOutcome.Refreshed(issued, user, resolved);
    }

    /// <summary>Ends the session a refresh token belongs to. An unknown token is a no-op.</summary>
    /// <param name="presented">The refresh secret from the cookie.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SignOutAsync(string? presented, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(presented))
        {
            return;
        }

        var hash = HashOf(presented);

        var token = await context.RefreshTokens
            .FirstOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken)
            .ConfigureAwait(false);

        if (token is null)
        {
            return;
        }

        var session = await context.Sessions
            .FirstOrDefaultAsync(candidate => candidate.Id == token.SessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session is not null)
        {
            await RevokeSessionAsync(session, SessionEndReason.SignedOut, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Ends one session by id, for the "sign out that device" button.</summary>
    /// <param name="userId">The owner, so one user cannot end another's session.</param>
    /// <param name="sessionId">The session to end.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> RevokeAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await context.Sessions
            .FirstOrDefaultAsync(
                candidate => candidate.Id == sessionId && candidate.UserId == userId,
                cancellationToken)
            .ConfigureAwait(false);

        if (session is null || !session.IsActive)
        {
            return false;
        }

        await RevokeSessionAsync(session, SessionEndReason.SignedOut, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Ends every session a user has, for "log out everywhere" and for a password change.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="reason">Why.</param>
    /// <param name="exceptSessionId">A session to leave alone — the one making the request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<int> RevokeAllAsync(
        Guid userId,
        SessionEndReason reason,
        Guid? exceptSessionId,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var sessions = await context.Sessions
            .Where(session => session.UserId == userId
                              && session.RevokedAt == null
                              && (exceptSessionId == null || session.Id != exceptSessionId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (sessions.Count == 0)
        {
            return 0;
        }

        var sessionIds = sessions.ConvertAll(session => session.Id);

        var live = await context.RefreshTokens
            .Where(token => sessionIds.Contains(token.SessionId) && token.RevokedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var session in sessions)
        {
            session.Revoke(now, reason);
        }

        foreach (var token in live)
        {
            token.Revoke(now);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return sessions.Count;
    }

    private async Task RevokeSessionAsync(
        UserSession session,
        SessionEndReason reason,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        session.Revoke(now, reason);

        var live = await context.RefreshTokens
            .Where(token => token.SessionId == session.Id && token.RevokedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var token in live)
        {
            token.Revoke(now);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private RefreshToken StoreRefreshToken(Guid sessionId, Guid userId, string secret, DateTimeOffset now)
    {
        var token = RefreshToken.Issue(
            sessionId,
            userId,
            HashOf(secret),
            now,
            now.AddDays(options.Value.Tokens.RefreshTokenDays));

        context.RefreshTokens.Add(token);
        return token;
    }

    /// <summary>
    /// The refresh token is hashed unsalted, because the lookup is <em>by</em> hash: a per-row
    /// salt could only be checked by loading every candidate row, which is exactly the scan the
    /// unique index exists to avoid. It is safe for the reason a salt exists at all — the token is
    /// 256 bits from a CSPRNG, so there is no rainbow table to build.
    /// </summary>
    private static string HashOf(string secret) => SecretHasher.Hash(secret, string.Empty);

    [LoggerMessage(EventId = 1402, Level = LogLevel.Warning,
        Message = "Support user {OperatorId} began impersonating user {UserId} in session {SessionId}.")]
    private static partial void ImpersonationStarted(
        ILogger logger,
        Guid operatorId,
        Guid userId,
        Guid sessionId);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Warning,
        Message = "A rotated refresh token was presented again for session {SessionId} (user {UserId}). "
                  + "The session has been revoked; the token is treated as compromised.")]
    private static partial void RefreshTokenReused(ILogger logger, Guid sessionId, Guid userId);
}

/// <summary>An issued support session: an access token with no refresh behind it.</summary>
/// <param name="AccessToken">The signed JWT, carrying the impersonator claim.</param>
/// <param name="ExpiresAt">When it stops being honoured. There is nothing to renew it with.</param>
/// <param name="SessionId">The impersonated session, which the exit control ends by id.</param>
/// <param name="Access">What the impersonated user may do.</param>
internal sealed record ImpersonationTokens(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid SessionId,
    UserAccess Access);

/// <summary>An impersonation that has just been ended, as the audit entry needs it.</summary>
/// <param name="SessionId">The session.</param>
/// <param name="UserId">Who was being acted as.</param>
/// <param name="OperatorId">Who was acting.</param>
/// <param name="Reason">Why they said they needed to.</param>
/// <param name="StartedAt">When it began, so its length is on the record.</param>
internal sealed record ImpersonatedSession(
    Guid SessionId,
    Guid UserId,
    Guid OperatorId,
    string Reason,
    DateTimeOffset StartedAt);

/// <summary>The result of presenting a refresh token.</summary>
/// <param name="Tokens">The new pair, when the exchange succeeded.</param>
/// <param name="User">Who the session belongs to.</param>
/// <param name="Access">What they may do, re-resolved as part of the exchange.</param>
/// <param name="WasReuse">Whether a rotated token was presented again, which revoked the session.</param>
internal sealed record RefreshOutcome(
    IssuedTokens? Tokens,
    Domain.User? User,
    UserAccess? Access,
    bool WasReuse)
{
    /// <summary>The token was unknown, expired, or its session was already over.</summary>
    public static readonly RefreshOutcome Rejected = new(null, null, null, WasReuse: false);

    /// <summary>A spent token was presented again; the session has been revoked.</summary>
    public static readonly RefreshOutcome Reused = new(null, null, null, WasReuse: true);

    /// <summary>The exchange succeeded.</summary>
    /// <param name="tokens">The new pair.</param>
    /// <param name="user">The session owner.</param>
    /// <param name="access">Their freshly resolved access.</param>
    public static RefreshOutcome Refreshed(IssuedTokens tokens, Domain.User user, UserAccess access)
        => new(tokens, user, access, WasReuse: false);
}
