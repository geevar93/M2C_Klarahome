using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Identity.Application.Authentication;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure;
using KlaraHome.Modules.Identity.Infrastructure.Access;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Identity.Application.Administration;

/// <summary>Begins acting as a customer, for support.</summary>
/// <param name="UserId">The customer to act as.</param>
/// <param name="Reason">Why. Recorded verbatim on the session and in the audit trail.</param>
/// <param name="Device">What the operator's caller looked like, read by the endpoint.</param>
internal sealed record StartImpersonationCommand(Guid UserId, string Reason, DeviceInfo Device)
    : ICommand<ImpersonationResponse>;

/// <summary>Ends an impersonation the caller started.</summary>
/// <param name="SessionId">The impersonated session.</param>
internal sealed record EndImpersonationCommand(Guid SessionId) : ICommand;

/// <summary>
/// A support session: an access token for somebody else, and the marker that says so.
/// </summary>
/// <remarks>
/// Deliberately not a <see cref="SignInResponse"/>. There is no refresh token and no cookie, and
/// reusing the sign-in shape would invite a client to feed this token to the same interceptor that
/// renews an ordinary session — which is exactly the renewal this must not have.
/// </remarks>
/// <param name="AccessToken">The signed JWT, carrying the impersonator claim.</param>
/// <param name="ExpiresAt">When it stops working. It cannot be renewed or extended.</param>
/// <param name="SessionId">The impersonated session, which the exit control ends by id.</param>
/// <param name="Reason">The reason given, echoed so the banner can show it.</param>
/// <param name="User">The customer now being acted as.</param>
internal sealed record ImpersonationResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid SessionId,
    string Reason,
    AuthenticatedUserResponse User);

/// <summary>The refusals impersonation can produce, in one place.</summary>
internal static class ImpersonationErrors
{
    /// <summary>The reason given was too short to be a reason.</summary>
    /// <param name="minimum">The configured minimum length.</param>
    public static Error ReasonTooShort(int minimum)
        => Error.Validation(
            "IDENTITY_IMPERSONATION_REASON_REQUIRED",
            $"Say why you need to act as this customer, in at least {minimum} characters. "
            + "It is recorded in the audit trail.");

    /// <summary>The target is staff or a vendor user, which is never impersonable.</summary>
    public static Error NotACustomer()
        => Error.Validation(
            "IDENTITY_IMPERSONATION_NOT_A_CUSTOMER",
            "Only a customer account can be impersonated. Acting as another staff or seller user "
            + "would let one operator inherit another's permissions.");

    /// <summary>The target account is suspended, locked or retired.</summary>
    public static Error NotActive()
        => Error.Validation(
            "IDENTITY_IMPERSONATION_TARGET_INACTIVE",
            "That account is not active, so there is no session to reproduce.");

    /// <summary>The caller is already acting as somebody else.</summary>
    public static Error AlreadyImpersonating()
        => Error.Conflict(
            "IDENTITY_IMPERSONATION_NESTED",
            "End the impersonation you are already in before starting another.");

    /// <summary>No impersonation with that session id was started by this caller.</summary>
    public static Error NotFound()
        => Error.NotFound(
            "IDENTITY_IMPERSONATION_NOT_FOUND",
            "That impersonation does not exist, or it was not started by you.");
}

/// <summary>
/// Starts a support impersonation (docs/07-security-compliance.md §2).
/// </summary>
/// <remarks>
/// <para>
/// Four refusals, and each of them is the point rather than defensive noise. Only a customer may be
/// impersonated, so an operator cannot borrow a colleague's permissions. The account must be
/// active, so a suspended one cannot be used as a back door. The reason must be long enough to
/// read, because the audit trail is the control and an unreadable trail is not one. And an
/// impersonated caller cannot impersonate, so the chain from a real operator to an action is
/// always one hop.
/// </para>
/// <para>
/// The audit entry names the operator explicitly rather than relying on the ambient user, because
/// from the next request onwards the ambient user <em>is</em> the customer.
/// </para>
/// </remarks>
/// <param name="scope">Finds the target account within the caller's scope.</param>
/// <param name="caller">The operator making the request.</param>
/// <param name="sessions">Mints the support session.</param>
/// <param name="options">The window and the minimum reason length.</param>
/// <param name="audit">Records the start.</param>
internal sealed class StartImpersonationCommandHandler(
    AdminUserScope scope,
    ICallerContext caller,
    SessionService sessions,
    IOptions<AuthOptions> options,
    IAuditLogger audit) : ICommandHandler<StartImpersonationCommand, ImpersonationResponse>
{
    /// <summary>The audited action for a started impersonation.</summary>
    public const string AuditAction = "identity.impersonation.started";

    public async Task<Result<ImpersonationResponse>> HandleAsync(
        StartImpersonationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var operatorId = caller.UserId;

        if (operatorId is null)
        {
            return Error.Unauthorized();
        }

        if (caller.ImpersonatorId is not null)
        {
            return ImpersonationErrors.AlreadyImpersonating();
        }

        var reason = command.Reason?.Trim() ?? string.Empty;
        var minimum = options.Value.Impersonation.MinimumReasonLength;

        if (reason.Length < minimum)
        {
            return ImpersonationErrors.ReasonTooShort(minimum);
        }

        var target = await scope.FindAsync(command.UserId, cancellationToken).ConfigureAwait(false);

        if (target is null)
        {
            return AdminUserScope.NotFound();
        }

        if (target.UserType != UserType.Customer)
        {
            return ImpersonationErrors.NotACustomer();
        }

        if (target.Status != UserStatus.Active)
        {
            return ImpersonationErrors.NotActive();
        }

        var issued = await sessions
            .StartImpersonationAsync(target, operatorId.Value, reason, command.Device, cancellationToken)
            .ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = SignInCoordinator.UserEntityType,
                EntityId = target.Id.ToString(),
                ActorType = AuditActorType.StaffUser,
                ActorId = operatorId,
                After = new
                {
                    sessionId = issued.SessionId,
                    reason,
                    expiresAt = issued.ExpiresAt,
                },
            },
            cancellationToken).ConfigureAwait(false);

        return new ImpersonationResponse(
            issued.AccessToken,
            issued.ExpiresAt,
            issued.SessionId,
            reason,
            AuthenticatedUserResponse.From(target, issued.Access));
    }
}

/// <summary>
/// Ends a support impersonation, and records how long it lasted.
/// </summary>
/// <remarks>
/// Called with the <em>operator's</em> own token, not the impersonated one: the customer whose
/// session this is holds no permission that could end it, and attributing the stop to the operator
/// is the whole point of recording it.
/// </remarks>
/// <param name="caller">The operator making the request.</param>
/// <param name="sessions">Ends the support session.</param>
/// <param name="audit">Records the stop.</param>
internal sealed class EndImpersonationCommandHandler(
    ICallerContext caller,
    SessionService sessions,
    IAuditLogger audit) : ICommandHandler<EndImpersonationCommand>
{
    /// <summary>The audited action for a finished impersonation.</summary>
    public const string AuditAction = "identity.impersonation.ended";

    public async Task<Result> HandleAsync(EndImpersonationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var operatorId = caller.UserId;

        if (operatorId is null)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var ended = await sessions
            .EndImpersonationAsync(command.SessionId, operatorId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (ended is null)
        {
            return Result.Failure(ImpersonationErrors.NotFound());
        }

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = SignInCoordinator.UserEntityType,
                EntityId = ended.UserId.ToString(),
                ActorType = AuditActorType.StaffUser,
                ActorId = operatorId,
                After = new
                {
                    sessionId = ended.SessionId,
                    reason = ended.Reason,
                    startedAt = ended.StartedAt,
                },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
