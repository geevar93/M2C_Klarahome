using FluentValidation;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Payments.Application.Payments;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure;
using KlaraHome.Modules.Payments.Infrastructure.Gateway;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using KlaraHome.Modules.Payments.Infrastructure.Processing;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Payments.Application.Refunds;

/// <summary>Raises a refund against a collection.</summary>
/// <param name="PaymentId">The collection it comes out of.</param>
/// <param name="Amount">What to send back.</param>
/// <param name="Reason">Why. Required, and it goes on the audit trail.</param>
/// <param name="SubOrderId">The seller's part it relates to, when it relates to one.</param>
/// <param name="Speed">How quickly to send it: <c>normal</c> or <c>optimum</c>.</param>
/// <param name="IdempotencyKey">The caller's key. A duplicate here is money out of the door twice.</param>
internal sealed record RaiseRefundCommand(
    Guid PaymentId,
    decimal Amount,
    string Reason,
    Guid? SubOrderId,
    string? Speed,
    string IdempotencyKey) : ICommand<RefundResponse>;

/// <summary>Lists refunds for the approvals queue and the finance report.</summary>
/// <param name="Status">Filter by where the refund stands.</param>
/// <param name="OrderId">Filter to one order.</param>
/// <param name="From">Only refunds raised on or after this instant.</param>
/// <param name="To">Only refunds raised strictly before this instant.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListRefundsQuery(
    string? Status,
    Guid? OrderId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Cursor,
    int? Size) : IQuery<PagedResult<RefundResponse>>;

/// <summary>The second signature on a refund above the threshold.</summary>
/// <param name="RefundId">The refund.</param>
internal sealed record ApproveRefundCommand(Guid RefundId) : ICommand<RefundResponse>;

/// <summary>Withholds the second signature. Nothing is sent to the gateway.</summary>
/// <param name="RefundId">The refund.</param>
/// <param name="Reason">Why.</param>
internal sealed record RejectRefundCommand(Guid RefundId, string? Reason) : ICommand<RefundResponse>;

/// <summary>Re-reads a refund from the gateway and applies what it says.</summary>
/// <param name="RefundId">The refund.</param>
internal sealed record SyncRefundCommand(Guid RefundId) : ICommand<RefundResponse>;

/// <summary>Validates a refund request.</summary>
internal sealed class RaiseRefundValidator : AbstractValidator<RaiseRefundCommand>
{
    public RaiseRefundValidator()
    {
        RuleFor(command => command.Amount).GreaterThan(0m);

        // A reason is mandatory rather than encouraged: docs/07-security-compliance.md §4 requires a
        // permission *plus* an audit reason, and a refund nobody wrote a reason for is one nobody can
        // account for six months later.
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(500);
        RuleFor(command => command.IdempotencyKey).NotEmpty().MaximumLength(128);
    }
}

/// <summary>Validates a rejection.</summary>
internal sealed class RejectRefundValidator : AbstractValidator<RejectRefundCommand>
{
    public RejectRefundValidator()
        => RuleFor(command => command.Reason).MaximumLength(500);
}

/// <summary>
/// Raises a refund and, if it needs no second signature, sends it.
/// </summary>
/// <remarks>
/// The threshold decision lives in <see cref="PaymentWorkflow.RaiseRefundAsync"/> and not here, so
/// that the operator's refund and the automatic cancellation refund are governed by the same rule.
/// A refund raised above the threshold is created and deliberately not sent: it appears in the
/// approvals queue and waits for somebody else.
/// </remarks>
/// <param name="context">The Payments data context.</param>
/// <param name="workflow">Raises it, applying the threshold.</param>
/// <param name="dispatcher">Sends it when it needed no second signature.</param>
/// <param name="scope">Who is asking; becomes the initiator.</param>
internal sealed class RaiseRefundCommandHandler(
    PaymentsDbContext context,
    PaymentWorkflow workflow,
    RefundDispatcher dispatcher,
    PaymentsScope scope) : ICommandHandler<RaiseRefundCommand, RefundResponse>
{
    public async Task<Result<RefundResponse>> HandleAsync(
        RaiseRefundCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var payment = await context.Payments
            .Include(candidate => candidate.Refunds)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.PaymentId, cancellationToken)
            .ConfigureAwait(false);

        if (payment is null)
        {
            return Result.Failure<RefundResponse>(PaymentsErrors.NotFound("payment"));
        }

        // The replayed-request path, checked before anything is created. The unique index is the
        // real guarantee; this is what turns a retry into the original answer rather than a 409.
        var existing = await context.Refunds
            .FirstOrDefaultAsync(refund => refund.IdempotencyKey == command.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return Result.Success(PaymentProjection.ToRefund(existing));
        }

        var raised = await workflow
            .RaiseRefundAsync(
                payment,
                command.Amount,
                command.Reason,
                command.IdempotencyKey,
                scope.ActorId,
                cancellationToken)
            .ConfigureAwait(false);

        if (raised.IsFailure)
        {
            return Result.Failure<RefundResponse>(raised.Error);
        }

        var refund = raised.Value;
        refund.AttachCause(command.SubOrderId, returnId: null);

        if (string.Equals(command.Speed, "optimum", StringComparison.OrdinalIgnoreCase))
        {
            refund.SetSpeed(RefundSpeed.Optimum);
        }

        var sent = await dispatcher.SendAsync(payment, refund, cancellationToken).ConfigureAwait(false);

        // The row is saved whether or not the gateway took it. A refund that exists here and not at
        // the gateway can be re-sent; one that exists at the gateway and not here is money nobody can
        // account for.
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return sent.IsFailure && refund.Status == RefundStatus.Approved
            ? Result.Failure<RefundResponse>(sent.Error)
            : Result.Success(PaymentProjection.ToRefund(refund));
    }
}

/// <summary>Lists refunds, newest first.</summary>
/// <param name="context">The Payments data context.</param>
/// <param name="options">Supplies the page ceiling.</param>
internal sealed class ListRefundsQueryHandler(PaymentsDbContext context, IOptions<PaymentsOptions> options)
    : IQueryHandler<ListRefundsQuery, PagedResult<RefundResponse>>
{
    public async Task<Result<PagedResult<RefundResponse>>> HandleAsync(
        ListRefundsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);
        var rows = context.Refunds.AsNoTracking().AsQueryable();

        if (Enum.TryParse<RefundStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(refund => refund.Status == status);
        }

        if (query.OrderId is { } orderId)
        {
            rows = rows.Where(refund => refund.OrderId == orderId);
        }

        if (query.From is { } from)
        {
            rows = rows.Where(refund => refund.InitiatedAt >= from);
        }

        if (query.To is { } to)
        {
            rows = rows.Where(refund => refund.InitiatedAt < to);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(refund => refund.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(refund => refund.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).Select(PaymentProjection.ToRefund).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<RefundResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>
/// Records the second signature and sends the refund.
/// </summary>
/// <remarks>
/// The self-approval check is here <em>and</em> as a check constraint on the table. A control that
/// lives in only one of those two places is one a future handler can forget about, and this is the
/// one operation in the platform where forgetting it means money leaves without a second pair of
/// eyes (docs/07-security-compliance.md §4).
/// </remarks>
/// <param name="context">The Payments data context.</param>
/// <param name="dispatcher">Sends it once it is approved.</param>
/// <param name="scope">Who is approving.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ApproveRefundCommandHandler(
    PaymentsDbContext context,
    RefundDispatcher dispatcher,
    PaymentsScope scope,
    IClock clock) : ICommandHandler<ApproveRefundCommand, RefundResponse>
{
    public async Task<Result<RefundResponse>> HandleAsync(
        ApproveRefundCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var refund = await context.Refunds
            .FirstOrDefaultAsync(candidate => candidate.Id == command.RefundId, cancellationToken)
            .ConfigureAwait(false);

        if (refund is null)
        {
            return Result.Failure<RefundResponse>(PaymentsErrors.NotFound("refund"));
        }

        if (refund.Status != RefundStatus.Requested)
        {
            return Result.Failure<RefundResponse>(PaymentsErrors.RefundNotPending);
        }

        if (scope.ActorId is not { } approver)
        {
            return Result.Failure<RefundResponse>(PaymentsErrors.SelfApproval);
        }

        if (refund.InitiatedBy == approver)
        {
            return Result.Failure<RefundResponse>(PaymentsErrors.SelfApproval);
        }

        var payment = await context.Payments
            .Include(candidate => candidate.Refunds)
            .FirstOrDefaultAsync(candidate => candidate.Id == refund.PaymentId, cancellationToken)
            .ConfigureAwait(false);

        if (payment is null)
        {
            return Result.Failure<RefundResponse>(PaymentsErrors.NotFound("payment"));
        }

        var tracked = payment.Refunds.First(candidate => candidate.Id == refund.Id);
        tracked.Approve(approver, clock.UtcNow);

        var sent = await dispatcher.SendAsync(payment, tracked, cancellationToken).ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return sent.IsFailure
            ? Result.Failure<RefundResponse>(sent.Error)
            : Result.Success(PaymentProjection.ToRefund(tracked));
    }
}

/// <summary>Withholds the second signature.</summary>
/// <param name="context">The Payments data context.</param>
internal sealed class RejectRefundCommandHandler(PaymentsDbContext context)
    : ICommandHandler<RejectRefundCommand, RefundResponse>
{
    public async Task<Result<RefundResponse>> HandleAsync(
        RejectRefundCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var refund = await context.Refunds
            .FirstOrDefaultAsync(candidate => candidate.Id == command.RefundId, cancellationToken)
            .ConfigureAwait(false);

        if (refund is null)
        {
            return Result.Failure<RefundResponse>(PaymentsErrors.NotFound("refund"));
        }

        if (!refund.Reject(command.Reason))
        {
            return Result.Failure<RefundResponse>(PaymentsErrors.RefundNotPending);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(PaymentProjection.ToRefund(refund));
    }
}

/// <summary>
/// Re-reads a refund from the gateway and applies what it says.
/// </summary>
/// <remarks>
/// The repair for a refund whose <c>refund.processed</c> webhook never arrived, and the way an
/// operator finds out whether a refund the gateway would not acknowledge actually went through. Like
/// every other route, it applies the answer through the workflow rather than setting a status.
/// </remarks>
/// <param name="context">The Payments data context.</param>
/// <param name="providers">Finds the adapter to ask.</param>
/// <param name="workflow">Applies what it says.</param>
internal sealed class SyncRefundCommandHandler(
    PaymentsDbContext context,
    PaymentProviderRegistry providers,
    PaymentWorkflow workflow) : ICommandHandler<SyncRefundCommand, RefundResponse>
{
    public async Task<Result<RefundResponse>> HandleAsync(
        SyncRefundCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var refund = await context.Refunds
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == command.RefundId, cancellationToken)
            .ConfigureAwait(false);

        if (refund is null)
        {
            return Result.Failure<RefundResponse>(PaymentsErrors.NotFound("refund"));
        }

        if (string.IsNullOrWhiteSpace(refund.ProviderRefundId))
        {
            // Never reached the gateway. Nothing to read back, and saying so is more useful than a
            // gateway error about an id that does not exist.
            return Result.Success(PaymentProjection.ToRefund(refund));
        }

        var payment = await context.Payments
            .Include(candidate => candidate.Refunds)
            .FirstOrDefaultAsync(candidate => candidate.Id == refund.PaymentId, cancellationToken)
            .ConfigureAwait(false);

        if (payment is null)
        {
            return Result.Failure<RefundResponse>(PaymentsErrors.NotFound("payment"));
        }

        var resolved = providers.Require(payment.Provider);

        if (resolved.IsFailure)
        {
            return Result.Failure<RefundResponse>(resolved.Error);
        }

        var fetched = await resolved.Value
            .FetchRefundAsync(refund.ProviderRefundId, cancellationToken)
            .ConfigureAwait(false);

        if (fetched.IsFailure)
        {
            return Result.Failure<RefundResponse>(fetched.Error);
        }

        var tracked = payment.Refunds.First(candidate => candidate.Id == refund.Id);

        var applied = await workflow
            .ApplyRefundAsync(payment, tracked, fetched.Value, cancellationToken)
            .ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return applied.IsFailure
            ? Result.Failure<RefundResponse>(applied.Error)
            : Result.Success(PaymentProjection.ToRefund(tracked));
    }
}
