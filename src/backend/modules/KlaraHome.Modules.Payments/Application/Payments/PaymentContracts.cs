using KlaraHome.Modules.Payments.Domain;

namespace KlaraHome.Modules.Payments.Application.Payments;

/// <summary>One collection, as a list shows it.</summary>
/// <param name="Id">The collection.</param>
/// <param name="OrderId">The order it is against.</param>
/// <param name="OrderNumber">Its number, which is what a support call quotes.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Provider">The gateway, or <c>internal_cod</c>.</param>
/// <param name="Method">The rail it was taken on, or <c>Unknown</c> until the gateway says.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Amount">What was asked for.</param>
/// <param name="AmountCaptured">What was taken.</param>
/// <param name="AmountRefunded">What has gone back.</param>
/// <param name="CurrencyCode">ISO 4217 code the amounts are in.</param>
/// <param name="Reference">The gateway's payment id.</param>
/// <param name="OpenedAt">When the collection was opened.</param>
/// <param name="CapturedAt">When the money was taken.</param>
internal sealed record PaymentSummaryResponse(
    Guid Id,
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    string Provider,
    string Method,
    string Status,
    decimal Amount,
    decimal AmountCaptured,
    decimal AmountRefunded,
    string CurrencyCode,
    string? Reference,
    DateTimeOffset OpenedAt,
    DateTimeOffset? CapturedAt);

/// <summary>One try at collecting, and what came back.</summary>
/// <param name="Id">The attempt.</param>
/// <param name="Reference">The gateway's id for this try.</param>
/// <param name="Status">What the gateway called it, verbatim.</param>
/// <param name="Method">The rail.</param>
/// <param name="Amount">What was being collected.</param>
/// <param name="Issuer">The card network, bank or wallet. Never identifying.</param>
/// <param name="Last4">The last four digits of a card, where there was one.</param>
/// <param name="UpiHandle">The domain half of a UPI handle, where there was one.</param>
/// <param name="ErrorCode">The gateway's refusal code.</param>
/// <param name="ErrorDescription">Its description of the refusal.</param>
/// <param name="Source">Which route learned about this try.</param>
/// <param name="AttemptedAt">When it happened.</param>
internal sealed record PaymentAttemptResponse(
    Guid Id,
    string? Reference,
    string Status,
    string Method,
    decimal Amount,
    string? Issuer,
    string? Last4,
    string? UpiHandle,
    string? ErrorCode,
    string? ErrorDescription,
    string Source,
    DateTimeOffset AttemptedAt);

/// <summary>One refund.</summary>
/// <param name="Id">The refund.</param>
/// <param name="PaymentId">The collection it comes out of.</param>
/// <param name="OrderId">The order.</param>
/// <param name="SubOrderId">The seller's part it relates to, when it relates to one.</param>
/// <param name="ReturnId">The return that caused it, when one did.</param>
/// <param name="Amount">What is going back.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="Reason">Why.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Speed">How quickly it was asked to be sent.</param>
/// <param name="RequiresApproval">Whether a second signature was needed.</param>
/// <param name="Reference">The gateway's refund id.</param>
/// <param name="InitiatedBy">Who raised it, or null when the platform did.</param>
/// <param name="InitiatedAt">When it was raised.</param>
/// <param name="ApprovedBy">Who signed it off.</param>
/// <param name="ApprovedAt">When.</param>
/// <param name="CompletedAt">When the gateway confirmed the money had gone.</param>
/// <param name="FailureReason">Why the gateway refused it, or why approval was withheld.</param>
internal sealed record RefundResponse(
    Guid Id,
    Guid PaymentId,
    Guid OrderId,
    Guid? SubOrderId,
    Guid? ReturnId,
    decimal Amount,
    string CurrencyCode,
    string Reason,
    string Status,
    string Speed,
    bool RequiresApproval,
    string? Reference,
    Guid? InitiatedBy,
    DateTimeOffset InitiatedAt,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason);

/// <summary>One collection in full, with everything that has happened to it.</summary>
/// <param name="Payment">The collection.</param>
/// <param name="Attempts">Every try, oldest first.</param>
/// <param name="Refunds">Every refund raised against it.</param>
/// <param name="AmountRefundable">What could still go back.</param>
/// <param name="CanRetry">Whether a shopper could still be sent back to the widget.</param>
/// <param name="FailureCode">The gateway's code from the last failure.</param>
/// <param name="FailureReason">What the shopper can be told about it.</param>
/// <param name="ReconciledAt">When reconciliation last agreed it with the gateway.</param>
/// <param name="SettlementId">The settlement report that paid it out, once one has.</param>
internal sealed record PaymentResponse(
    PaymentSummaryResponse Payment,
    IReadOnlyList<PaymentAttemptResponse> Attempts,
    IReadOnlyList<RefundResponse> Refunds,
    decimal AmountRefundable,
    bool CanRetry,
    string? FailureCode,
    string? FailureReason,
    DateTimeOffset? ReconciledAt,
    Guid? SettlementId);

/// <summary>
/// Where the money for an order stands, as the shopper is shown it.
/// </summary>
/// <remarks>
/// Deliberately far less than the admin view. A shopper needs to know whether their money went
/// through and whether they may try again; the gateway's error codes, the attempt history and the
/// instrument details are support's, not theirs.
/// </remarks>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="Status">Where the collection stands.</param>
/// <param name="Method">The rail it was taken on, once it is known.</param>
/// <param name="Amount">What is owed.</param>
/// <param name="AmountCaptured">What has been taken.</param>
/// <param name="AmountRefunded">What has gone back.</param>
/// <param name="CurrencyCode">ISO 4217 code the amounts are in.</param>
/// <param name="IsPaid">Whether the money is in.</param>
/// <param name="CanRetry">Whether the shopper may try again, within the window.</param>
/// <param name="FailureReason">What they can be told about a failure, in plain words.</param>
internal sealed record MyPaymentResponse(
    Guid OrderId,
    string OrderNumber,
    string Status,
    string Method,
    decimal Amount,
    decimal AmountCaptured,
    decimal AmountRefunded,
    string CurrencyCode,
    bool IsPaid,
    bool CanRetry,
    string? FailureReason);

/// <summary>What the storefront needs to open the checkout widget again.</summary>
/// <param name="OrderId">The order being paid for.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="Provider">The gateway.</param>
/// <param name="ProviderOrderId">Its handle on the collection.</param>
/// <param name="PublicKey">The publishable key. Never the secret.</param>
/// <param name="Amount">What to collect.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="CustomerName">Prefilled on the widget.</param>
/// <param name="CustomerEmail">Prefilled.</param>
/// <param name="CustomerMobile">Prefilled in E.164.</param>
internal sealed record PaymentInstructionResponse(
    Guid OrderId,
    string OrderNumber,
    string Provider,
    string ProviderOrderId,
    string PublicKey,
    decimal Amount,
    string CurrencyCode,
    string? CustomerName,
    string? CustomerEmail,
    string? CustomerMobile);

/// <summary>One stored webhook, as the operator's log shows it.</summary>
/// <param name="Id">The stored event.</param>
/// <param name="Provider">Which gateway sent it.</param>
/// <param name="ProviderEventId">Its own id for the event.</param>
/// <param name="EventType">What happened.</param>
/// <param name="SignatureValid">Whether the HMAC verified.</param>
/// <param name="Status">Where processing stands.</param>
/// <param name="Attempts">How many times processing has been tried.</param>
/// <param name="ProcessError">Why the last attempt failed.</param>
/// <param name="PaymentId">The collection it turned out to concern.</param>
/// <param name="ReceivedAt">When we received it.</param>
/// <param name="ProcessedAt">When it was applied.</param>
/// <param name="NextAttemptAt">When it will be tried again.</param>
internal sealed record GatewayEventSummaryResponse(
    Guid Id,
    string Provider,
    string ProviderEventId,
    string EventType,
    bool SignatureValid,
    string Status,
    int Attempts,
    string? ProcessError,
    Guid? PaymentId,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset? NextAttemptAt);

/// <summary>One stored webhook with the body exactly as it arrived.</summary>
/// <param name="Event">The event.</param>
/// <param name="Payload">The raw body. The evidence, never rewritten.</param>
internal sealed record GatewayEventResponse(GatewayEventSummaryResponse Event, string Payload);

/// <summary>One settlement report.</summary>
/// <param name="Id">The report.</param>
/// <param name="Provider">Which gateway paid it.</param>
/// <param name="ProviderSettlementId">Its id for the settlement.</param>
/// <param name="Amount">What reached the bank.</param>
/// <param name="Fees">What the gateway kept.</param>
/// <param name="Tax">The GST on those fees.</param>
/// <param name="CurrencyCode">ISO 4217 code the amounts are in.</param>
/// <param name="Utr">The bank reference.</param>
/// <param name="Status">The gateway's status word.</param>
/// <param name="SettledAt">When it settled.</param>
/// <param name="ImportedAt">When we imported it.</param>
/// <param name="EntryCount">How many lines it has.</param>
/// <param name="MatchedCount">How many agreed.</param>
/// <param name="MismatchCount">How many did not. Non-zero is what somebody has to look at.</param>
internal sealed record SettlementResponse(
    Guid Id,
    string Provider,
    string ProviderSettlementId,
    decimal Amount,
    decimal Fees,
    decimal Tax,
    string CurrencyCode,
    string? Utr,
    string Status,
    DateTimeOffset? SettledAt,
    DateTimeOffset ImportedAt,
    int EntryCount,
    int MatchedCount,
    int MismatchCount);

/// <summary>One line of a settlement report.</summary>
/// <param name="Id">The line.</param>
/// <param name="EntryType">What kind of movement it is.</param>
/// <param name="Reference">The gateway's payment or refund id.</param>
/// <param name="PaymentId">The collection it matched, when it matched one.</param>
/// <param name="Amount">The gross amount.</param>
/// <param name="Fee">The gateway's fee.</param>
/// <param name="Tax">The GST on it.</param>
/// <param name="Credit">What entered the merchant balance.</param>
/// <param name="Debit">What left it.</param>
/// <param name="MatchStatus">Whether it agreed with what this platform recorded.</param>
/// <param name="MismatchReason">What did not agree.</param>
/// <param name="OccurredAt">When the movement happened.</param>
internal sealed record SettlementEntryResponse(
    Guid Id,
    string EntryType,
    string? Reference,
    Guid? PaymentId,
    decimal Amount,
    decimal Fee,
    decimal Tax,
    decimal Credit,
    decimal Debit,
    string MatchStatus,
    string? MismatchReason,
    DateTimeOffset? OccurredAt);

/// <summary>Cash owed at one door, and where it has got to.</summary>
/// <param name="Id">The cash record.</param>
/// <param name="OrderId">The order.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="ShipmentId">The shipment it travels with, once there is one.</param>
/// <param name="Amount">What is owed at the door.</param>
/// <param name="CurrencyCode">ISO 4217 code the amounts are in.</param>
/// <param name="Status">Where the cash stands.</param>
/// <param name="CollectedAmount">What was actually taken.</param>
/// <param name="CollectedAt">When.</param>
/// <param name="RemittedAmount">What the courier handed over.</param>
/// <param name="RemittedAt">When.</param>
/// <param name="RemittanceReference">Their reference for it.</param>
/// <param name="Note">Why nothing is owed, when nothing is.</param>
internal sealed record CodCollectionResponse(
    Guid Id,
    Guid OrderId,
    Guid SubOrderId,
    Guid? VendorId,
    Guid? ShipmentId,
    decimal Amount,
    string CurrencyCode,
    string Status,
    decimal? CollectedAmount,
    DateTimeOffset? CollectedAt,
    decimal? RemittedAmount,
    DateTimeOffset? RemittedAt,
    string? RemittanceReference,
    string? Note);

/// <summary>
/// Turns the domain into the shapes above.
/// </summary>
/// <remarks>
/// One place, so two handlers cannot answer the same question with two different documents — and, on
/// this module in particular, so that what is <em>omitted</em> is decided once. Nothing here exposes
/// a gateway credential, a full instrument identifier, or a raw payload to a shopper.
/// </remarks>
internal static class PaymentProjection
{
    /// <summary>Projects a collection for a list.</summary>
    /// <param name="payment">The collection.</param>
    public static PaymentSummaryResponse ToSummary(Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        return new PaymentSummaryResponse(
            payment.Id,
            payment.OrderId,
            payment.OrderNumber,
            payment.CustomerId,
            payment.Provider,
            payment.Method.ToString(),
            payment.Status.ToString(),
            payment.Amount,
            payment.AmountCaptured,
            payment.AmountRefunded,
            payment.CurrencyCode,
            payment.ProviderPaymentId,
            payment.OpenedAt,
            payment.CapturedAt);
    }

    /// <summary>Projects a collection in full, for the support screen.</summary>
    /// <param name="payment">The collection, with its attempts and refunds loaded.</param>
    /// <param name="canRetry">Whether the retry window is still open.</param>
    public static PaymentResponse ToDetail(Payment payment, bool canRetry)
    {
        ArgumentNullException.ThrowIfNull(payment);

        return new PaymentResponse(
            ToSummary(payment),
            [.. payment.Attempts.OrderBy(attempt => attempt.AttemptedAt).Select(ToAttempt)],
            [.. payment.Refunds.OrderBy(refund => refund.InitiatedAt).Select(ToRefund)],
            payment.AmountRefundable,
            canRetry,
            payment.FailureCode,
            payment.FailureReason,
            payment.ReconciledAt,
            payment.SettlementId);
    }

    /// <summary>Projects one try.</summary>
    /// <param name="attempt">The attempt.</param>
    public static PaymentAttemptResponse ToAttempt(PaymentAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        return new PaymentAttemptResponse(
            attempt.Id,
            attempt.ProviderPaymentId,
            attempt.Status,
            attempt.Method.ToString(),
            attempt.Amount,
            attempt.Detail.Issuer,
            attempt.Detail.Last4,
            attempt.Detail.UpiHandle,
            attempt.ErrorCode,
            attempt.ErrorDescription,
            attempt.Source.ToString(),
            attempt.AttemptedAt);
    }

    /// <summary>Projects one refund.</summary>
    /// <param name="refund">The refund.</param>
    public static RefundResponse ToRefund(Refund refund)
    {
        ArgumentNullException.ThrowIfNull(refund);

        return new RefundResponse(
            refund.Id,
            refund.PaymentId,
            refund.OrderId,
            refund.SubOrderId,
            refund.ReturnId,
            refund.Amount,
            refund.CurrencyCode,
            refund.Reason,
            refund.Status.ToString(),
            refund.Speed.ToString(),
            refund.RequiresApproval,
            refund.ProviderRefundId,
            refund.InitiatedBy,
            refund.InitiatedAt,
            refund.ApprovedBy,
            refund.ApprovedAt,
            refund.CompletedAt,
            refund.FailureReason ?? refund.RejectedReason);
    }

    /// <summary>
    /// Projects a collection for the shopper who paid it.
    /// </summary>
    /// <remarks>
    /// The failure reason is the gateway's own description, which is written for a customer — "your
    /// card was declined by the issuing bank" — and the code beside it is deliberately not included.
    /// A shopper cannot act on <c>BAD_REQUEST_ERROR</c>, and support can read it from the admin.
    /// </remarks>
    /// <param name="payment">The collection.</param>
    /// <param name="canRetry">Whether the retry window is still open.</param>
    public static MyPaymentResponse ToMine(Payment payment, bool canRetry)
    {
        ArgumentNullException.ThrowIfNull(payment);

        return new MyPaymentResponse(
            payment.OrderId,
            payment.OrderNumber,
            payment.Status.ToString(),
            payment.Method.ToString(),
            payment.Amount,
            payment.AmountCaptured,
            payment.AmountRefunded,
            payment.CurrencyCode,
            PaymentLifecycle.IsSettled(payment.Status),
            canRetry,
            payment.FailureReason);
    }

    /// <summary>Projects one stored webhook for the operator's log.</summary>
    /// <param name="stored">The event.</param>
    public static GatewayEventSummaryResponse ToEvent(GatewayEvent stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        return new GatewayEventSummaryResponse(
            stored.Id,
            stored.Provider,
            stored.ProviderEventId,
            stored.EventType,
            stored.SignatureValid,
            stored.Status.ToString(),
            stored.Attempts,
            stored.ProcessError,
            stored.PaymentId,
            stored.ReceivedAt,
            stored.ProcessedAt,
            stored.NextAttemptAt);
    }

    /// <summary>Projects one settlement report.</summary>
    /// <param name="settlement">The report.</param>
    public static SettlementResponse ToSettlement(GatewaySettlement settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);

        return new SettlementResponse(
            settlement.Id,
            settlement.Provider,
            settlement.ProviderSettlementId,
            settlement.Amount,
            settlement.Fees,
            settlement.Tax,
            settlement.CurrencyCode,
            settlement.Utr,
            settlement.Status,
            settlement.SettledAt,
            settlement.ImportedAt,
            settlement.EntryCount,
            settlement.MatchedCount,
            settlement.MismatchCount);
    }

    /// <summary>Projects one line of a report.</summary>
    /// <param name="entry">The line.</param>
    public static SettlementEntryResponse ToSettlementEntry(GatewaySettlementEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new SettlementEntryResponse(
            entry.Id,
            entry.EntryType,
            entry.ProviderPaymentId,
            entry.PaymentId == Guid.Empty ? null : entry.PaymentId,
            entry.Amount,
            entry.Fee,
            entry.Tax,
            entry.Credit,
            entry.Debit,
            entry.MatchStatus.ToString(),
            entry.MismatchReason,
            entry.OccurredAt);
    }

    /// <summary>Projects one cash record.</summary>
    /// <param name="collection">The cash record.</param>
    public static CodCollectionResponse ToCod(CodCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        return new CodCollectionResponse(
            collection.Id,
            collection.OrderId,
            collection.SubOrderId,
            collection.VendorId,
            collection.ShipmentId,
            collection.Amount,
            collection.CurrencyCode,
            collection.Status.ToString(),
            collection.CollectedAmount,
            collection.CollectedAt,
            collection.RemittedAmount,
            collection.RemittedAt,
            collection.RemittanceReference,
            collection.Note);
    }
}
