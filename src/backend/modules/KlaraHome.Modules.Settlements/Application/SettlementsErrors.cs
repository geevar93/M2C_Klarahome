using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Settlements.Application;

/// <summary>
/// Every failure this module reports, with the stable code the frontend switches on
/// (docs/04-api-specification.md §1.2).
/// </summary>
/// <remarks>
/// Declared in one place so two handlers cannot answer the same situation with two different codes.
/// Every one of these is read by an operator rather than a shopper, so the wording says what is
/// wrong and what would fix it, rather than apologising.
/// </remarks>
internal static class SettlementsErrors
{
    /// <summary>The cycle, batch, ledger entry or seller does not exist, or is not this caller's.</summary>
    /// <remarks>
    /// One code for both cases, on purpose. A seller who could tell the difference between "no such
    /// batch" and "somebody else's batch" would have learnt that another seller was paid, and 404 is
    /// what docs/04-api-specification.md §1.2 requires for "not visible to this caller".
    /// </remarks>
    /// <param name="what">What was being looked for, in words a caller can read.</param>
    public static Error NotFound(string what)
        => Error.NotFound("SETTLEMENT_NOT_FOUND", $"That {what} does not exist.");

    /// <summary>The period has not finished, or its hold has not expired.</summary>
    /// <remarks>
    /// The hold is what makes a settlement safe to pay, so closing early is refused rather than
    /// warned about. The date is in the message because "why can't I close it" is otherwise a
    /// conversation.
    /// </remarks>
    /// <param name="closableFrom">When it may be closed.</param>
    public static Error CycleNotDue(DateTimeOffset closableFrom)
        => Error.Conflict(
            "SETTLEMENT_CYCLE_NOT_DUE",
            $"This period cannot be closed until {closableFrom:d MMMM yyyy}, when its return hold expires.");

    /// <summary>The cycle has already been closed.</summary>
    public static Error CycleClosed { get; } =
        Error.Conflict("SETTLEMENT_CYCLE_CLOSED", "That period has already been closed.");

    /// <summary>A payout batch was asked for with no cycles that can go in it.</summary>
    public static Error NothingToPay { get; } =
        Error.Validation(
            "PAYOUT_NOTHING_TO_PAY",
            "None of those settlement periods is closed and payable. Close a period first.");

    /// <summary>A cycle named in a batch is already in another one.</summary>
    /// <param name="reference">The batch that already has it.</param>
    public static Error CycleAlreadyBatched(string reference)
        => Error.Conflict(
            "PAYOUT_CYCLE_ALREADY_BATCHED",
            $"One of those periods is already in payout batch {reference}.");

    /// <summary>The batch is not in a state that allows what was asked.</summary>
    /// <param name="status">Where it actually is.</param>
    /// <param name="action">What was asked for.</param>
    public static Error BatchNotIn(string status, string action)
        => Error.Conflict(
            "PAYOUT_BATCH_STATE",
            $"That payout batch is {status.ToLowerInvariant()} and cannot be {action}.");

    /// <summary>
    /// The person approving the batch is the person who raised it.
    /// </summary>
    /// <remarks>
    /// A distinct code rather than a generic refusal, because the frontend shows a different screen
    /// for it: the batch is fine and the approver is wrong, and what the operator needs is a
    /// colleague rather than a correction.
    /// </remarks>
    public static Error SelfApproval { get; } =
        Error.Forbidden(
            "PAYOUT_SELF_APPROVAL",
            "A payout batch must be approved by somebody other than the person who raised it.");

    /// <summary>The caller may not take this transition, though the transition exists.</summary>
    /// <param name="action">What was asked for.</param>
    public static Error NotPermitted(string action)
        => Error.Forbidden("PAYOUT_NOT_PERMITTED", $"You do not have permission to {action} a payout batch.");

    /// <summary>
    /// No payout rail is configured, so nothing can be sent.
    /// </summary>
    /// <remarks>
    /// A 503 with a named code rather than a 500, and the distinction matters: nothing is broken. The
    /// deployment has no payout credentials, the batch is still correct, and filling three
    /// configuration values in is the whole of the fix.
    /// </remarks>
    public static Error ProviderUnavailable { get; } =
        Error.Unavailable(
            "PAYOUT_PROVIDER_UNAVAILABLE",
            "No payout provider is configured, so money cannot be sent yet.");

    /// <summary>The gateway refused, or could not be reached.</summary>
    /// <param name="detail">What it said, where it said anything worth repeating.</param>
    public static Error ProviderFailed(string? detail = null)
        => Error.Unavailable(
            "PAYOUT_PROVIDER_FAILED",
            string.IsNullOrWhiteSpace(detail)
                ? "The payout provider could not be reached. The transfer has not been sent."
                : detail);

    /// <summary>An adjustment was asked for with no amount, or a negative one.</summary>
    public static Error AdjustmentAmount { get; } =
        Error.Validation(
            "SETTLEMENT_ADJUSTMENT_AMOUNT",
            "An adjustment must be for a positive amount. Its direction says which way it moves.");

    /// <summary>An adjustment was asked for without saying which way it moves.</summary>
    public static Error AdjustmentDirection { get; } =
        Error.Validation(
            "SETTLEMENT_ADJUSTMENT_DIRECTION",
            "An adjustment must say whether it credits or debits the seller.");

    /// <summary>An adjustment was asked for without a reason.</summary>
    /// <remarks>
    /// Refused rather than defaulted. An adjustment is the one entry a human writes into a seller's
    /// account, and an unexplained one is the entry that becomes an argument six months later.
    /// </remarks>
    public static Error AdjustmentReason { get; } =
        Error.Validation(
            "SETTLEMENT_ADJUSTMENT_REASON",
            "Say why the adjustment is being made. It appears on the seller's statement.");

    /// <summary>A vendor-scoped caller asked for something only the platform may ask for.</summary>
    public static Error VendorForbidden { get; } =
        Error.Forbidden(
            "SETTLEMENT_VENDOR_FORBIDDEN",
            "A seller account cannot perform this action on a settlement.");

    /// <summary>The export would be larger than this deployment will build in one go.</summary>
    /// <param name="maximum">How many rows it will build.</param>
    public static Error ExportTooLarge(int maximum)
        => Error.Validation(
            "SETTLEMENT_EXPORT_TOO_LARGE",
            $"That range has more than {maximum} rows. Narrow the dates and export again.");
}
