using KlaraHome.Modules.Payments.Application;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure.Gateway;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Payments.Infrastructure.Processing;

/// <summary>The webhook event types this platform acts on (docs/08-integrations.md §1).</summary>
/// <remarks>
/// A closed list, and anything outside it is stored and marked <c>Ignored</c> rather than refused.
/// Answering a gateway with a 4xx invites a retry storm, and a type nobody handles today is evidence
/// tomorrow.
/// </remarks>
internal static class GatewayEventTypes
{
    /// <summary>The shopper's bank is holding the money.</summary>
    public const string PaymentAuthorized = "payment.authorized";

    /// <summary>The money has been taken. The event that confirms an order.</summary>
    public const string PaymentCaptured = "payment.captured";

    /// <summary>The gateway refused.</summary>
    public const string PaymentFailed = "payment.failed";

    /// <summary>Every payment against a gateway order has been made.</summary>
    public const string OrderPaid = "order.paid";

    /// <summary>A refund was accepted by the gateway.</summary>
    public const string RefundCreated = "refund.created";

    /// <summary>A refund has actually moved the money.</summary>
    public const string RefundProcessed = "refund.processed";

    /// <summary>The gateway refused a refund.</summary>
    public const string RefundFailed = "refund.failed";

    /// <summary>A settlement was paid out. Ingestion pulls the report rather than trusting this.</summary>
    public const string SettlementProcessed = "settlement.processed";

    /// <summary>Every type this platform subscribes to.</summary>
    public static readonly IReadOnlySet<string> Subscribed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        PaymentAuthorized,
        PaymentCaptured,
        PaymentFailed,
        OrderPaid,
        RefundCreated,
        RefundProcessed,
        RefundFailed,
        SettlementProcessed,
    };
}

/// <summary>
/// Turns a stored webhook into a change to a payment.
/// </summary>
/// <remarks>
/// <para>
/// The webhook endpoint does not do this. It verifies, stores and answers <c>200</c>, and this runs
/// afterwards in the worker — which is what
/// <c>docs/04-api-specification.md §5</c> means by "return 200 fast; process asynchronously". A
/// gateway that is kept waiting while an order is confirmed, stock is committed and an invoice is
/// raised will time out and redeliver, and a redelivery storm during a sale is the last thing a
/// payment path needs.
/// </para>
/// <para>
/// <b>Nothing here trusts the payload.</b> The envelope is used only to work out which payment the
/// event is about; the facts are then re-fetched from the gateway's API and it is that answer which
/// is applied (docs/07-security-compliance.md §4). A webhook body is an unauthenticated claim that
/// happens to be signed — the signature proves who sent it, not that its contents are current.
/// </para>
/// <para>
/// Every path is idempotent, because delivery is at-least-once and a replay must be a no-op. The
/// unique event id already stops the same delivery being stored twice; this makes applying the same
/// <em>fact</em> twice harmless as well, which matters because the reconciliation sweep can reach it
/// independently.
/// </para>
/// </remarks>
/// <param name="context">The Payments data context.</param>
/// <param name="providers">Finds the adapter that can re-fetch the fact.</param>
/// <param name="workflow">Applies it. The single place a payment moves.</param>
/// <param name="logger">Reports what an event turned out to be about.</param>
internal sealed partial class GatewayEventProcessor(
    PaymentsDbContext context,
    PaymentProviderRegistry providers,
    PaymentWorkflow workflow,
    ILogger<GatewayEventProcessor> logger)
{
    /// <summary>
    /// Applies one stored event. Does not save — the caller commits it with the event's own row.
    /// </summary>
    /// <param name="stored">The event, as it arrived.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> ProcessAsync(GatewayEvent stored, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stored);

        // An event whose signature never verified is never processable, however it got here. It is
        // kept because it is evidence — somebody is sending us forged webhooks — and acting on it
        // would be acting on an unauthenticated instruction to confirm an order.
        if (!stored.SignatureValid)
        {
            return Result.Failure(PaymentsErrors.InvalidCheckoutSignature);
        }

        if (!GatewayEventTypes.Subscribed.Contains(stored.EventType))
        {
            return Result.Success();
        }

        var resolved = providers.Require(stored.Provider);

        if (resolved.IsFailure)
        {
            return resolved;
        }

        var provider = resolved.Value;
        var envelope = provider.ReadWebhook(stored.Payload);

        if (envelope.IsFailure)
        {
            return envelope;
        }

        return stored.EventType.ToLowerInvariant() switch
        {
            GatewayEventTypes.RefundCreated
                or GatewayEventTypes.RefundProcessed
                or GatewayEventTypes.RefundFailed =>
                await ApplyRefundEventAsync(stored, envelope.Value, provider, cancellationToken)
                    .ConfigureAwait(false),

            // Settlement events say a payout happened; they do not carry the report. Ingestion pulls
            // that on its own schedule, so acknowledging this one is the whole of handling it.
            GatewayEventTypes.SettlementProcessed => Result.Success(),

            _ => await ApplyPaymentEventAsync(stored, envelope.Value, provider, cancellationToken)
                .ConfigureAwait(false),
        };
    }

    /// <summary>Applies a payment event by re-fetching the payment and letting the workflow decide.</summary>
    private async Task<Result> ApplyPaymentEventAsync(
        GatewayEvent stored,
        WebhookEnvelope envelope,
        IPaymentProvider provider,
        CancellationToken cancellationToken)
    {
        var payment = await FindPaymentAsync(envelope, cancellationToken).ConfigureAwait(false);

        if (payment is null)
        {
            // A payment nobody here opened. Not an error to retry — retrying will not make the
            // collection exist — so it is recorded as processed and the mismatch stands in the log.
            UnknownPayment(logger, stored.EventType, envelope.ProviderOrderId, envelope.ProviderPaymentId);
            return Result.Success();
        }

        stored.MarkProcessed(payment.Id, stored.ReceivedAt);

        var reference = envelope.ProviderPaymentId ?? payment.ProviderPaymentId;

        if (string.IsNullOrWhiteSpace(reference))
        {
            // order.paid without a payment id in the body: ask the gateway which payments were made
            // against the collection instead of guessing from the payload.
            var listed = await provider
                .FetchPaymentsForOrderAsync(payment.ProviderOrderId ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);

            if (listed.IsFailure)
            {
                return listed;
            }

            var settled = listed.Value.FirstOrDefault(candidate => candidate.IsCaptured)
                          ?? listed.Value.FirstOrDefault();

            return settled is null
                ? Result.Success()
                : await workflow
                    .ApplyAsync(payment, settled, PaymentAttemptSource.Webhook, cancellationToken)
                    .ConfigureAwait(false);
        }

        var fetched = await provider.FetchPaymentAsync(reference, cancellationToken).ConfigureAwait(false);

        return fetched.IsFailure
            ? fetched
            : await workflow
                .ApplyAsync(payment, fetched.Value, PaymentAttemptSource.Webhook, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>Applies a refund event by re-fetching the refund and letting the workflow decide.</summary>
    private async Task<Result> ApplyRefundEventAsync(
        GatewayEvent stored,
        WebhookEnvelope envelope,
        IPaymentProvider provider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(envelope.ProviderRefundId))
        {
            UnknownRefund(logger, stored.EventType, envelope.ProviderPaymentId);
            return Result.Success();
        }

        var refund = await context.Refunds
            .FirstOrDefaultAsync(
                candidate => candidate.ProviderRefundId == envelope.ProviderRefundId,
                cancellationToken)
            .ConfigureAwait(false);

        // A refund the gateway knows about and this platform does not is a refund somebody raised in
        // the gateway's own dashboard. It is recorded on the payment by reconciliation rather than
        // invented here, because there is no reason for it and no initiator to attribute it to.
        if (refund is null)
        {
            UnknownRefund(logger, stored.EventType, envelope.ProviderRefundId);
            return Result.Success();
        }

        var payment = await LoadWithRefundsAsync(refund.PaymentId, cancellationToken).ConfigureAwait(false);

        if (payment is null)
        {
            return Result.Success();
        }

        stored.MarkProcessed(payment.Id, stored.ReceivedAt);

        var fetched = await provider
            .FetchRefundAsync(envelope.ProviderRefundId, cancellationToken)
            .ConfigureAwait(false);

        if (fetched.IsFailure)
        {
            return fetched;
        }

        var tracked = payment.Refunds.First(candidate => candidate.Id == refund.Id);

        return await workflow
            .ApplyRefundAsync(payment, tracked, fetched.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the collection an event is about, by the three routes in decreasing reliability.
    /// </summary>
    /// <remarks>
    /// Our own id in the notes first — it is exact, and it is why the notes are sent at all. Then the
    /// gateway order id, which every payment event carries. Then the payment id, which only helps
    /// once a payment has already been recorded against the collection.
    /// </remarks>
    private async Task<Payment?> FindPaymentAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope.PaymentId is { } id)
        {
            var byId = await LoadWithRefundsAsync(id, cancellationToken).ConfigureAwait(false);

            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(envelope.ProviderOrderId))
        {
            var byOrder = await context.Payments
                .Include(payment => payment.Refunds)
                .FirstOrDefaultAsync(
                    payment => payment.ProviderOrderId == envelope.ProviderOrderId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (byOrder is not null)
            {
                return byOrder;
            }
        }

        return string.IsNullOrWhiteSpace(envelope.ProviderPaymentId)
            ? null
            : await context.Payments
                .Include(payment => payment.Refunds)
                .FirstOrDefaultAsync(
                    payment => payment.ProviderPaymentId == envelope.ProviderPaymentId,
                    cancellationToken)
                .ConfigureAwait(false);
    }

    private async Task<Payment?> LoadWithRefundsAsync(Guid paymentId, CancellationToken cancellationToken)
        => await context.Payments
            .Include(payment => payment.Refunds)
            .FirstOrDefaultAsync(payment => payment.Id == paymentId, cancellationToken)
            .ConfigureAwait(false);

    [LoggerMessage(EventId = 1540, Level = LogLevel.Warning,
        Message = "A {EventType} webhook named gateway order {ProviderOrderId} / payment "
                  + "{ProviderPaymentId}, which this platform did not open.")]
    private static partial void UnknownPayment(
        ILogger logger,
        string eventType,
        string? providerOrderId,
        string? providerPaymentId);

    [LoggerMessage(EventId = 1541, Level = LogLevel.Warning,
        Message = "A {EventType} webhook named refund {Reference}, which this platform did not raise.")]
    private static partial void UnknownRefund(ILogger logger, string eventType, string? reference);
}
