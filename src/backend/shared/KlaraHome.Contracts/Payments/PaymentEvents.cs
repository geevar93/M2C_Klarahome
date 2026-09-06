using KlaraHome.Contracts.IntegrationEvents;

namespace KlaraHome.Contracts.Payments;

/// <summary>
/// Money was collected against an order (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// Published <em>after</em> the order has been confirmed through <see cref="Orders.IOrderPaymentSync"/>,
/// never instead of it. The confirmation is synchronous because stock and invoices depend on it; this
/// event is for the modules that only want to know it happened — Notifications sends the receipt,
/// Settlements opens the seller's entry, and Reporting counts the money.
/// </para>
/// <para>
/// It carries the captured amount rather than the order total. They are the same figure on a healthy
/// order and are deliberately not assumed to be: a partial capture is a real gateway state, and a
/// consumer that reported the order total for it would be reporting money nobody has.
/// </para>
/// </remarks>
/// <param name="PaymentId">The collection.</param>
/// <param name="OrderId">The order it was collected for.</param>
/// <param name="OrderNumber">Its number, which is what a shopper quotes.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Provider">The gateway, or <c>internal_cod</c> for cash.</param>
/// <param name="Method">The rail it was paid on, as the gateway reports it.</param>
/// <param name="Reference">The gateway's payment id.</param>
/// <param name="Amount">What was captured.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="CapturedAt">When the gateway captured it.</param>
public sealed record PaymentCaptured(
    Guid PaymentId,
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    string Provider,
    string Method,
    string? Reference,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset CapturedAt) : IntegrationEvent;

/// <summary>
/// A collection was refused, or never completed.
/// </summary>
/// <remarks>
/// Notifications tells the shopper their payment did not go through and gives them the retry link;
/// Reporting counts the drop-off. The order is not dead — it is retryable until the window closes,
/// which is what makes this an event worth sending rather than a silent state.
/// </remarks>
/// <param name="PaymentId">The collection that failed.</param>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="FailureCode">The gateway's own code, verbatim.</param>
/// <param name="Reason">What the shopper can be told.</param>
/// <param name="FailedAt">When it failed.</param>
public sealed record PaymentFailed(
    Guid PaymentId,
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    string? FailureCode,
    string? Reason,
    DateTimeOffset FailedAt) : IntegrationEvent;

/// <summary>
/// Money went back to where it came from.
/// </summary>
/// <remarks>
/// Raised when the gateway confirms the refund, never when it is requested: a refund a shopper has
/// been told about and the gateway later refused is worse than one they were told about late.
/// Settlements reverses the seller's entry on it, and Returns closes the RMA.
/// </remarks>
/// <param name="RefundId">The refund.</param>
/// <param name="PaymentId">The collection it came out of.</param>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="SubOrderId">The seller's part it relates to, when it relates to one.</param>
/// <param name="ReturnId">The return that caused it, when a return did.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Amount">What went back.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="Reference">The gateway's refund id.</param>
/// <param name="CompletedAt">When the gateway processed it.</param>
public sealed record RefundProcessed(
    Guid RefundId,
    Guid PaymentId,
    Guid OrderId,
    string OrderNumber,
    Guid? SubOrderId,
    Guid? ReturnId,
    Guid CustomerId,
    decimal Amount,
    string CurrencyCode,
    string? Reference,
    DateTimeOffset CompletedAt) : IntegrationEvent;

/// <summary>
/// Cash was taken at the door, or remitted by the courier.
/// </summary>
/// <remarks>
/// The cash-on-delivery half of <see cref="PaymentCaptured"/>, and separate from it because the two
/// facts arrive days apart: the money exists when the courier collects it and is the platform's when
/// they remit it, and a settlement that paid a seller on the first would be paying out of its own
/// pocket.
/// </remarks>
/// <param name="CollectionId">The collection record.</param>
/// <param name="OrderId">The order.</param>
/// <param name="SubOrderId">The seller's part the cash was taken against.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="Amount">What was taken.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="IsRemitted">Whether the courier has since handed it over.</param>
/// <param name="OccurredAt">When the reported step happened.</param>
public sealed record CodCashRecorded(
    Guid CollectionId,
    Guid OrderId,
    Guid SubOrderId,
    Guid? VendorId,
    decimal Amount,
    string CurrencyCode,
    bool IsRemitted,
    DateTimeOffset OccurredAt) : IntegrationEvent;

/// <summary>
/// The gateway's books and ours do not agree.
/// </summary>
/// <remarks>
/// <para>
/// The alert half of reconciliation (docs/08-integrations.md §1). It is published rather than logged
/// because somebody has to act on it: a capture the gateway has no record of, a settled amount that
/// is not what we recorded, or a settlement line pointing at a payment that does not exist here are
/// all money questions, and a log line is not an inbox.
/// </para>
/// <para>
/// Nothing is repaired automatically. A reconciliation that silently rewrites our figures to match
/// the gateway's would destroy the very evidence that a discrepancy existed.
/// </para>
/// </remarks>
/// <param name="Kind">What disagreed: <c>missing-at-gateway</c>, <c>amount</c>, <c>unmatched-entry</c>, <c>status</c>.</param>
/// <param name="PaymentId">The collection concerned, when one could be resolved.</param>
/// <param name="OrderId">The order concerned, when one could be resolved.</param>
/// <param name="SettlementId">The settlement report the mismatch was found in, when it was.</param>
/// <param name="Reference">The gateway identifier the mismatch is about.</param>
/// <param name="ExpectedAmount">What this platform recorded.</param>
/// <param name="ActualAmount">What the gateway reports.</param>
/// <param name="CurrencyCode">ISO 4217 code both amounts are in.</param>
/// <param name="Detail">A sentence an operator can act on.</param>
/// <param name="DetectedAt">When the sweep found it.</param>
public sealed record PaymentMismatchDetected(
    string Kind,
    Guid? PaymentId,
    Guid? OrderId,
    Guid? SettlementId,
    string? Reference,
    decimal? ExpectedAmount,
    decimal? ActualAmount,
    string CurrencyCode,
    string Detail,
    DateTimeOffset DetectedAt) : IntegrationEvent;
