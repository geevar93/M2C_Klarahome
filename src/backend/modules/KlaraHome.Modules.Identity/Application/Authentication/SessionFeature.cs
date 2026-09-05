using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure.Access;
using KlaraHome.Modules.Identity.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Identity.Application.Authentication;

/// <summary>Exchanges the refresh cookie for a new token pair.</summary>
/// <param name="RefreshToken">The secret from the cookie, read by the endpoint.</param>
internal sealed record RefreshSessionCommand(string? RefreshToken) : ICommand<SignInResult>;

/// <summary>Ends the session the refresh cookie belongs to.</summary>
/// <param name="RefreshToken">The secret from the cookie.</param>
internal sealed record SignOutCommand(string? RefreshToken) : ICommand;

/// <summary>Lists the caller's signed-in devices.</summary>
internal sealed record GetSessionsQuery : IQuery<IReadOnlyList<SessionResponse>>;

/// <summary>Ends one of the caller's sessions.</summary>
/// <param name="SessionId">The session to end.</param>
internal sealed record RevokeSessionCommand(Guid SessionId) : ICommand;

/// <summary>Ends every session except the one making the request.</summary>
/// <param name="ExceptCurrent">Whether to keep the calling session alive.</param>
internal sealed record RevokeAllSessionsCommand(bool ExceptCurrent) : ICommand<RevokedSessionsResponse>;

/// <summary>One signed-in device, as the account screen shows it.</summary>
/// <param name="Id">The session id.</param>
/// <param name="Device">A readable device description.</param>
/// <param name="IpAddress">Where it signed in from, masked.</param>
/// <param name="StartedAt">When it signed in.</param>
/// <param name="LastSeenAt">When it last exchanged a token.</param>
/// <param name="IsCurrent">Whether this is the session making the request.</param>
internal sealed record SessionResponse(
    Guid Id,
    string? Device,
    string? IpAddress,
    DateTimeOffset StartedAt,
    DateTimeOffset LastSeenAt,
    bool IsCurrent);

/// <summary>How many devices were signed out.</summary>
/// <param name="Revoked">The number of sessions ended.</param>
internal sealed record RevokedSessionsResponse(int Revoked);

/// <summary>Exchanges the refresh cookie for a new pair.</summary>
/// <param name="sessions">Owns rotation.</param>
internal sealed class RefreshSessionCommandHandler(SessionService sessions)
    : ICommandHandler<RefreshSessionCommand, SignInResult>
{
    public async Task<Result<SignInResult>> HandleAsync(
        RefreshSessionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var outcome = await sessions.RefreshAsync(command.RefreshToken, cancellationToken).ConfigureAwait(false);

        if (outcome.Tokens is null || outcome.User is null || outcome.Access is null)
        {
            // A detected reuse and an unknown token answer the same way. The client's next step is
            // identical — sign in again — and telling them apart would confirm to whoever
            // presented the stolen token that it was once valid.
            return AuthErrors.InvalidRefreshToken();
        }

        return SignInResult.Signed(outcome.Tokens, outcome.User, outcome.Access);
    }
}

/// <summary>Ends the session the cookie belongs to.</summary>
/// <param name="sessions">Owns revocation.</param>
internal sealed class SignOutCommandHandler(SessionService sessions) : ICommandHandler<SignOutCommand>
{
    public async Task<Result> HandleAsync(SignOutCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Always succeeds. A logout that reports failure because the cookie was already gone gives
        // the client nothing to do about it and leaves a user staring at an error on their way out.
        await sessions.SignOutAsync(command.RefreshToken, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>Lists the caller's live sessions, newest first.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="caller">The signed-in caller.</param>
internal sealed class GetSessionsQueryHandler(IdentityDbContext context, ICallerContext caller)
    : IQueryHandler<GetSessionsQuery, IReadOnlyList<SessionResponse>>
{
    public async Task<Result<IReadOnlyList<SessionResponse>>> HandleAsync(
        GetSessionsQuery query,
        CancellationToken cancellationToken)
    {
        var userId = caller.UserId;

        if (userId is null)
        {
            return Error.Unauthorized();
        }

        var sessions = await context.Sessions
            .Where(session => session.UserId == userId && session.RevokedAt == null)
            .OrderByDescending(session => session.LastSeenAt)
            .Select(session => new
            {
                session.Id,
                session.Device,
                session.IpAddress,
                session.StartedAt,
                session.LastSeenAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<SessionResponse> response = sessions
            .ConvertAll(session => new SessionResponse(
                session.Id,
                session.Device,
                MaskIp(session.IpAddress),
                session.StartedAt,
                session.LastSeenAt,
                session.Id == caller.SessionId));

        return Result.Success(response);
    }

    /// <summary>
    /// Drops the last octet of an IPv4 address and everything after the network prefix of an IPv6
    /// one. Enough for a person to recognise "that was not me", not enough to be a location history
    /// on a screen anybody who borrows the laptop can read (docs/07-security-compliance.md §5).
    /// </summary>
    /// <param name="ipAddress">The stored address.</param>
    internal static string? MaskIp(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        if (ipAddress.Contains(':', StringComparison.Ordinal))
        {
            var groups = ipAddress.Split(':');
            return groups.Length <= 3 ? "::x" : string.Join(':', groups.Take(3)) + ":x";
        }

        var octets = ipAddress.Split('.');
        return octets.Length == 4 ? string.Join('.', octets.Take(3)) + ".x" : ipAddress;
    }
}

/// <summary>Ends one of the caller's sessions.</summary>
/// <param name="sessions">Owns revocation.</param>
/// <param name="caller">The signed-in caller, so one user cannot end another's session.</param>
internal sealed class RevokeSessionCommandHandler(SessionService sessions, ICallerContext caller)
    : ICommandHandler<RevokeSessionCommand>
{
    public async Task<Result> HandleAsync(RevokeSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (caller.UserId is null)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var revoked = await sessions
            .RevokeAsync(caller.UserId.Value, command.SessionId, cancellationToken)
            .ConfigureAwait(false);

        // 404, not 403, for a session that belongs to someone else: an out-of-scope resource must
        // not be distinguishable from one that does not exist (docs/07-security-compliance.md §2).
        return revoked
            ? Result.Success()
            : Result.Failure(Error.NotFound("IDENTITY_SESSION_NOT_FOUND", "That session does not exist."));
    }
}

/// <summary>Signs the caller out everywhere.</summary>
/// <param name="sessions">Owns revocation.</param>
/// <param name="caller">The signed-in caller.</param>
internal sealed class RevokeAllSessionsCommandHandler(SessionService sessions, ICallerContext caller)
    : ICommandHandler<RevokeAllSessionsCommand, RevokedSessionsResponse>
{
    public async Task<Result<RevokedSessionsResponse>> HandleAsync(
        RevokeAllSessionsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (caller.UserId is null)
        {
            return Error.Unauthorized();
        }

        var revoked = await sessions
            .RevokeAllAsync(
                caller.UserId.Value,
                SessionEndReason.SignedOutEverywhere,
                command.ExceptCurrent ? caller.SessionId : null,
                cancellationToken)
            .ConfigureAwait(false);

        return new RevokedSessionsResponse(revoked);
    }
}
