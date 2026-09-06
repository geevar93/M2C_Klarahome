using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure.Gateway;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Payments.Infrastructure.Processing;

/// <summary>
/// Sends an approved refund to the gateway, and applies what comes back.
/// </summary>
/// <remarks>
/// <para>
/// Two callers reach it — the operator who approves a refund, and the cancellation handler that
/// raises one automatically — and both must send it the same way. A refund sent from two places is a
/// refund sent twice the day the two paths disagree about whether the gateway had already been
/// asked.
/// </para>
/// <para>
/// It refuses to send anything that is not <see cref="RefundStatus.Approved"/>. A refund still
/// waiting for its second signature is not an intention this platform is allowed to act on, and
/// making that a check here rather than a caller's responsibility is what keeps the maker-checker
/// control from having a way round it.
/// </para>
/// <para>
/// The refund's own key goes to the gateway as an idempotency header, so a send that timed out on
/// this side and succeeded on theirs does not refund the shopper twice when it is retried.
/// </para>
/// </remarks>
/// <param name="providers">Finds the adapter that can send it.</param>
/// <param name="workflow">Applies what the gateway says. The single place a refund moves.</param>
/// <param name="logger">Reports a refund the gateway would not take.</param>
internal sealed partial class RefundDispatcher(
    PaymentProviderRegistry providers,
    PaymentWorkflow workflow,
    ILogger<RefundDispatcher> logger)
{
    /// <summary>Sends one approved refund. Does not save — the caller commits.</summary>
    /// <param name="payment">The collection it comes out of, with its refunds loaded.</param>
    /// <param name="refund">The refund, already approved.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> SendAsync(Payment payment, Refund refund, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(refund);

        if (refund.Status != RefundStatus.Approved)
        {
            // Not an error. A refund waiting for a second signature is simply not ready to be sent,
            // and the approval endpoint will bring it back here when it is.
            return Result.Success();
        }

        var resolved = providers.Require(payment.Provider);

        if (resolved.IsFailure)
        {
            return resolved;
        }

        if (string.IsNullOrWhiteSpace(payment.ProviderPaymentId))
        {
            return Result.Failure(Application.PaymentsErrors.NothingToRefund);
        }

        var sent = await resolved.Value
            .RefundAsync(
                payment.ProviderPaymentId,
                refund.Amount,
                refund.CurrencyCode,
                refund.IdempotencyKey,
                refund.Speed,
                cancellationToken)
            .ConfigureAwait(false);

        if (sent.IsFailure)
        {
            // Left Approved rather than Failed: the gateway being unreachable is not the gateway
            // refusing, and marking it failed would tell a shopper their refund was declined when
            // nobody has asked yet. An operator re-sends it, or syncs it once the gateway answers.
            RefundNotSent(logger, refund.Id, sent.Error.Code, sent.Error.Message);
            return sent;
        }

        return await workflow
            .ApplyRefundAsync(payment, refund, sent.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 1600, Level = LogLevel.Error,
        Message = "Refund {RefundId} was not sent to the gateway: {Code} {Detail}. It stays approved and "
                  + "unsent.")]
    private static partial void RefundNotSent(ILogger logger, Guid refundId, string code, string detail);
}
