using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Settlements.Infrastructure.Payouts;

/// <summary>The payout adapters this platform knows, as they are stored.</summary>
/// <remarks>
/// Strings rather than an enum because the value is written onto every batch and read by an operator,
/// and because a future rail is a new adapter and a new constant rather than a schema change. The
/// same arrangement the courier adapters take (ADR-018), and for the same reason: a batch sent
/// through one provider must still be readable after the default has changed to another.
/// </remarks>
internal static class PayoutProviders
{
    /// <summary>
    /// Razorpay Route: a transfer to the seller's linked account at the gateway.
    /// </summary>
    /// <remarks>
    /// The recommended arrangement in docs/08-integrations.md §1, because the money never leaves the
    /// gateway's custody until the seller's own settlement — which is what makes the deferred-transfer
    /// model match a settlement ledger and its statutory deductions.
    /// </remarks>
    public const string Route = "route";

    /// <summary>
    /// RazorpayX: a direct bank payout to the seller's fund account.
    /// </summary>
    /// <remarks>
    /// The fallback where Route is not enabled on the merchant account. It moves real money out of a
    /// real balance, so it needs that balance funded — which is the practical difference an operator
    /// has to know about before choosing it.
    /// </remarks>
    public const string X = "x";

    /// <summary>No payout rail at all. What a deployment with no credentials gets.</summary>
    public const string None = "none";
}

/// <summary>What to send, and to whom.</summary>
/// <param name="PayoutItemId">Our own id for the transfer, sent as a note so the gateway echoes it back.</param>
/// <param name="Reference">The batch reference, which is what appears on the gateway's dashboard.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="DestinationAccountId">
/// The account at the gateway the money goes to: a Route linked account, or an X fund account.
/// </param>
/// <param name="Amount">What to send, in the store currency.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="Narration">What the seller sees on their bank statement.</param>
internal sealed record PayoutRequest(
    Guid PayoutItemId,
    string Reference,
    Guid VendorId,
    string DestinationAccountId,
    decimal Amount,
    string CurrencyCode,
    string Narration);

/// <summary>
/// A transfer as the gateway currently describes it.
/// </summary>
/// <remarks>
/// The shape every route into this module reduces to: a send returns one of these, and so does a
/// re-fetch by the reconciliation sweep. Having a single shape is what lets the code that applies a
/// transfer's outcome be written once rather than twice.
/// </remarks>
/// <param name="ProviderPayoutId">The gateway's id for the transfer.</param>
/// <param name="Status">Its own status word, kept verbatim for the record.</param>
/// <param name="IsProcessed">Whether the money has actually reached the seller.</param>
/// <param name="IsFailed">Whether the gateway refused it, or the bank returned it.</param>
/// <param name="Utr">The bank's unique transaction reference, once there is one.</param>
/// <param name="Error">Why it failed, in the gateway's words.</param>
/// <param name="OccurredAt">When the gateway says it happened.</param>
internal sealed record ProviderPayout(
    string ProviderPayoutId,
    string Status,
    bool IsProcessed,
    bool IsFailed,
    string? Utr,
    string? Error,
    DateTimeOffset? OccurredAt);

/// <summary>
/// The rail money to a seller travels on (docs/08-integrations.md §1).
/// </summary>
/// <remarks>
/// <para>
/// An interface this platform owns rather than a gateway SDK, for the reason ADR-008 gives about
/// payments: a redistributed product must be able to change payout rails without a rewrite, and the
/// two rails Razorpay itself offers already differ enough — one transfers within the gateway, the
/// other moves money out of a funded balance — that even a single-vendor build needs the seam.
/// </para>
/// <para>
/// <see cref="IsConfigured"/> is what lets this module ship before its credentials exist. An adapter
/// that says false is not an error state: batches are still built, the ledger still says what is
/// owed, and the send refuses with a named error instead of pretending money moved.
/// </para>
/// <para>
/// Every method returns a <see cref="Result"/> rather than throwing. A gateway refusing one seller's
/// transfer is an outcome the batch records and carries on from, not an exception that abandons the
/// other four hundred.
/// </para>
/// </remarks>
internal interface IPayoutProvider
{
    /// <summary>Which adapter this is: one of <see cref="PayoutProviders"/>.</summary>
    string Name { get; }

    /// <summary>Whether this adapter has everything it needs to reach the gateway.</summary>
    bool IsConfigured { get; }

    /// <summary>Sends one seller's money.</summary>
    /// <param name="request">What to send, and where.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<ProviderPayout>> SendAsync(PayoutRequest request, CancellationToken cancellationToken);

    /// <summary>Asks the gateway what became of a transfer it was given earlier.</summary>
    /// <remarks>
    /// The reconciliation path. A bank transfer is not instant and a webhook about one can be lost, so
    /// this is the floor under the answer rather than the way it is usually learnt.
    /// </remarks>
    /// <param name="providerPayoutId">The gateway's id for the transfer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<ProviderPayout>> FetchAsync(string providerPayoutId, CancellationToken cancellationToken);
}
