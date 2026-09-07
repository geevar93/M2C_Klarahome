using KlaraHome.Contracts.Payments;
using KlaraHome.Modules.Payments.Application;
using KlaraHome.Modules.Payments.Application.Payments;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure.Gateway;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using KlaraHome.Modules.Payments.Infrastructure.Processing;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Payments.Infrastructure.Refunds;

/// <summary>
/// Answers <see cref="IRefundInitiation"/> from this module's own tables.
/// </summary>
/// <remarks>
/// <para>
/// The seam other modules send money back through, declared at Step 17 for the Returns module and
/// implemented here for the reason every payments path is implemented here: the maker–checker
/// threshold, the idempotency index and the conversation with the gateway all live in this module,
/// and a caller writing to <c>payments.refunds</c> directly would be a caller who could bypass all
/// three.
/// </para>
/// <para>
/// It resolves the collection itself rather than taking a payment id. Which collection a refund comes
/// out of, whether it was a card or cash on delivery, and how much of it is still refundable are all
/// facts of this schema — asking a returns queue to work them out first would put a join across a
/// module boundary on the one path where money leaves.
/// </para>
/// <para>
/// The initiator is deliberately null. The platform raised this on a decision somebody else already
/// took and recorded; leaving it null is what lets any operator approve it above the threshold
/// without tripping the self-approval constraint, exactly as an automatic cancellation refund does.
/// </para>
/// </remarks>
/// <param name="context">The Payments data context.</param>
/// <param name="workflow">Raises the refund, applying the threshold.</param>
/// <param name="dispatcher">Sends the ones approved on creation.</param>
/// <param name="logger">Reports what was raised.</param>
internal sealed partial class RefundInitiationService(
    PaymentsDbContext context,
    PaymentWorkflow workflow,
    RefundDispatcher dispatcher,
    ILogger<RefundInitiationService> logger) : IRefundInitiation
{
    /// <inheritdoc />
    public async Task<Result<RefundInitiationResult>> RefundAsync(
        RefundInitiationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The replayed-request path, checked before anything is created. The unique index is the real
        // guarantee; this is what turns a retry into the original answer rather than a conflict.
        var existing = await context.Refunds
            .FirstOrDefaultAsync(
                refund => refund.IdempotencyKey == request.IdempotencyKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return Result.Success(Describe(existing));
        }

        var payment = await FindCollectionAsync(request.OrderId, cancellationToken).ConfigureAwait(false);

        if (payment is null)
        {
            return Result.Failure<RefundInitiationResult>(PaymentsErrors.NotFound("payment"));
        }

        var raised = await workflow
            .RaiseRefundAsync(
                payment,
                request.Amount,
                request.Reason,
                request.IdempotencyKey,
                initiatedBy: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (raised.IsFailure)
        {
            return Result.Failure<RefundInitiationResult>(raised.Error);
        }

        var refund = raised.Value;
        refund.AttachCause(request.SubOrderId, request.ReturnId);

        // Sends only if the amount was under the approval threshold; anything above it waits in the
        // approvals queue for a second pair of eyes, which is the whole point of the threshold.
        var sent = await dispatcher.SendAsync(payment, refund, cancellationToken).ConfigureAwait(false);

        // The row is saved whether or not the gateway took it. A refund that exists here and not at
        // the gateway can be re-sent; one that exists at the gateway and not here is money nobody can
        // account for.
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (sent.IsFailure && refund.Status == RefundStatus.Approved)
        {
            return Result.Failure<RefundInitiationResult>(sent.Error);
        }

        Raised(logger, request.ReturnId, request.Amount, refund.Status);

        return Result.Success(Describe(refund));
    }

    /// <inheritdoc />
    public async ValueTask<decimal> RefundableAmountAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var payment = await FindCollectionAsync(orderId, cancellationToken).ConfigureAwait(false);

        return payment?.AmountRefundable ?? 0m;
    }

    /// <summary>
    /// The collection a refund against this order comes out of.
    /// </summary>
    /// <remarks>
    /// The gateway collection in preference to the cash one, and the most recent where there are
    /// several — a retried payment leaves the failed attempts behind it. A cash-on-delivery order has
    /// only the internal record, which has nothing refundable on it until the courier remits, and
    /// that is exactly what a caller needs to be told.
    /// </remarks>
    private async Task<Payment?> FindCollectionAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var payments = await context.Payments
            .Include(candidate => candidate.Refunds)
            .Where(candidate => candidate.OrderId == orderId)
            .OrderByDescending(candidate => candidate.OpenedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return payments.Find(candidate =>
                   candidate.Provider != PaymentProviders.InternalCod
                   && candidate.AmountRefundable > 0m)
               ?? payments.Find(candidate => candidate.AmountRefundable > 0m)
               ?? payments.FirstOrDefault();
    }

    /// <summary>Describes a refund in the shape the seam returns.</summary>
    private static RefundInitiationResult Describe(Refund refund)
        => new(
            refund.Id,
            refund.Status.ToString(),
            refund.Amount,
            refund.CurrencyCode,
            refund.Status == RefundStatus.Requested);

    [LoggerMessage(EventId = 1560, Level = LogLevel.Information,
        Message = "A refund of {Amount} was raised for return {ReturnId}. It is {Status}.")]
    private static partial void Raised(ILogger logger, Guid? returnId, decimal amount, RefundStatus status);
}
