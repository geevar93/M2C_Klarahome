using KlaraHome.Contracts.IntegrationEvents;

namespace KlaraHome.Contracts.Settlements;

/// <summary>
/// A seller's settlement period was closed and its net figure fixed
/// (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// The moment "what we owe this seller" stops being a moving number. Notifications sends the
/// statement, Reporting counts the platform's margin on it, and a payout batch may only be built
/// from cycles that have reached this point — which is what stops money leaving against a period
/// that is still accruing.
/// </remarks>
/// <param name="CycleId">The cycle.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="PeriodStart">The first instant the period covers.</param>
/// <param name="PeriodEnd">The first instant it does not.</param>
/// <param name="GrossSales">What the seller supplied in the period, inclusive of tax.</param>
/// <param name="TotalCommission">What the platform charged, inclusive of the GST on it.</param>
/// <param name="TotalFees">Gateway, platform and freight charges together.</param>
/// <param name="TotalRefunds">What came back off the period, inclusive of tax.</param>
/// <param name="Tcs">Tax collected at source under section 52 of the CGST Act.</param>
/// <param name="Tds">Tax deducted at source under section 194-O of the Income-tax Act.</param>
/// <param name="OpeningBalance">What was carried in from the previous period.</param>
/// <param name="NetPayable">What the seller is owed for the period, after everything.</param>
/// <param name="CurrencyCode">ISO 4217 code every amount is in.</param>
/// <param name="ClosedAt">When it was closed.</param>
public sealed record SettlementCycleClosed(
    Guid CycleId,
    Guid VendorId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    decimal GrossSales,
    decimal TotalCommission,
    decimal TotalFees,
    decimal TotalRefunds,
    decimal Tcs,
    decimal Tds,
    decimal OpeningBalance,
    decimal NetPayable,
    string CurrencyCode,
    DateTimeOffset ClosedAt) : IntegrationEvent;

/// <summary>
/// Money reached a seller's bank account.
/// </summary>
/// <remarks>
/// Raised when the gateway confirms the transfer, never when it is requested — the same rule
/// <c>RefundProcessed</c> follows, and for the same reason: a seller told they have been paid and a
/// transfer the gateway later reverses is worse than being told a day late.
/// </remarks>
/// <param name="PayoutItemId">The line of the batch.</param>
/// <param name="PayoutBatchId">The batch it was paid in.</param>
/// <param name="BatchReference">The batch's human-readable reference.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="CycleId">The settlement cycle it discharges.</param>
/// <param name="Amount">What was sent.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="ProviderPayoutId">The gateway's id for the transfer.</param>
/// <param name="Utr">The bank's unique transaction reference, once the gateway reports one.</param>
/// <param name="CompletedAt">When the gateway settled it.</param>
public sealed record PayoutCompleted(
    Guid PayoutItemId,
    Guid PayoutBatchId,
    string BatchReference,
    Guid VendorId,
    Guid? CycleId,
    decimal Amount,
    string CurrencyCode,
    string? ProviderPayoutId,
    string? Utr,
    DateTimeOffset CompletedAt) : IntegrationEvent;

/// <summary>
/// A payout did not reach a seller.
/// </summary>
/// <remarks>
/// Separate from <see cref="PayoutCompleted"/> rather than a status field on it, because the two
/// have different audiences: a completion is a receipt, and a failure is somebody's work. The
/// seller's cycle stays unpaid and the item can be retried once the reason is fixed — a wrong IFSC
/// is corrected in the seller's own record, not in the batch.
/// </remarks>
/// <param name="PayoutItemId">The line of the batch.</param>
/// <param name="PayoutBatchId">The batch.</param>
/// <param name="BatchReference">The batch's human-readable reference.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="CycleId">The settlement cycle that is still owed.</param>
/// <param name="Amount">What was attempted.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="Reason">Why it failed, in the gateway's words where it gave any.</param>
/// <param name="FailedAt">When it failed.</param>
public sealed record PayoutFailed(
    Guid PayoutItemId,
    Guid PayoutBatchId,
    string BatchReference,
    Guid VendorId,
    Guid? CycleId,
    decimal Amount,
    string CurrencyCode,
    string? Reason,
    DateTimeOffset FailedAt) : IntegrationEvent;
