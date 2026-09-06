using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Payments.Application;

/// <summary>
/// Every failure this module reports, with the stable code the frontend switches on
/// (docs/04-api-specification.md §1.2).
/// </summary>
/// <remarks>
/// Declared in one place so two handlers cannot answer the same situation with two different codes.
/// The wording is deliberately plain: several of these are read by a shopper standing at a checkout
/// with their card in their hand, and "the gateway returned a non-success status" is not something
/// anybody can act on.
/// </remarks>
internal static class PaymentsErrors
{
    /// <summary>The payment, refund, event or settlement does not exist, or is not this caller's.</summary>
    /// <remarks>
    /// One code for both cases, on purpose. Telling a caller that somebody else's payment id is real
    /// is a disclosure, and 404 is what docs/04-api-specification.md §1.2 requires for "not visible
    /// to this caller".
    /// </remarks>
    /// <param name="what">What was being looked for, in words a caller can read.</param>
    public static Error NotFound(string what)
        => Error.NotFound("PAYMENT_NOT_FOUND", $"That {what} does not exist.");

    /// <summary>No provider is configured, so nothing can be collected.</summary>
    /// <remarks>
    /// The state a fresh deployment is in before its Razorpay credentials are filled in. Deliberately
    /// a 503 that names the reason: an operator reading it knows exactly which three values are
    /// missing, where a null reference would tell them only that something threw.
    /// </remarks>
    public static Error ProviderUnavailable { get; } =
        Error.Unavailable(
            "PAYMENT_PROVIDER_UNAVAILABLE",
            "Online payment is not available on this deployment. The gateway credentials are not configured.");

    /// <summary>The gateway did not answer, or answered with a failure.</summary>
    /// <param name="detail">What went wrong, in words safe to show a shopper.</param>
    public static Error ProviderFailed(string? detail = null)
        => Error.Unavailable(
            "PAYMENT_PROVIDER_FAILED",
            detail is { Length: > 0 }
                ? detail
                : "The payment provider could not be reached. Your order is safe; please try again.");

    /// <summary>The order cannot be paid for: it is cash on delivery, or already settled.</summary>
    public static Error NotPayable { get; } =
        Error.Conflict("PAYMENT_NOT_PAYABLE", "There is nothing to pay for this order.");

    /// <summary>The order has already been paid.</summary>
    public static Error AlreadyCaptured { get; } =
        Error.Conflict("PAYMENT_ALREADY_CAPTURED", "This order has already been paid for.");

    /// <summary>The window in which a failed payment may be retried has closed.</summary>
    public static Error RetryWindowClosed { get; } =
        Error.Conflict(
            "PAYMENT_RETRY_WINDOW_CLOSED",
            "This order can no longer be paid for online. Please contact support.");

    /// <summary>The browser handed back a handshake that does not verify.</summary>
    /// <remarks>
    /// Malformed rather than unauthorised, because the caller <em>is</em> authenticated — what failed
    /// is the gateway's own signature over the two identifiers, which means the payload was altered
    /// between the widget and us and must not be recorded as an attempt.
    /// </remarks>
    public static Error InvalidCheckoutSignature { get; } =
        Error.Malformed(
            "PAYMENT_SIGNATURE_INVALID",
            "That payment confirmation could not be verified.");

    /// <summary>The refund asks for more than is left of the captured money.</summary>
    /// <param name="refundable">What could still go back.</param>
    public static Error RefundExceedsCaptured(decimal refundable)
        => Error.Validation(
            "REFUND_EXCEEDS_CAPTURED",
            $"Only {refundable:0.00} of this payment is still refundable.");

    /// <summary>A refund was raised against money that was never taken.</summary>
    public static Error NothingToRefund { get; } =
        Error.Conflict("REFUND_NOTHING_CAPTURED", "No money has been collected against this order.");

    /// <summary>The refund is not waiting for a signature.</summary>
    public static Error RefundNotPending { get; } =
        Error.Conflict("REFUND_NOT_PENDING", "That refund is not waiting for approval.");

    /// <summary>
    /// The same person cannot raise a refund and approve it.
    /// </summary>
    /// <remarks>
    /// The maker-checker control of docs/07-security-compliance.md §4, refused at the handler and
    /// again by a check constraint on the table. A control that exists in only one of those two
    /// places is one a future handler can forget about.
    /// </remarks>
    public static Error SelfApproval { get; } =
        Error.Forbidden(
            "REFUND_SELF_APPROVAL",
            "A refund must be approved by somebody other than the person who raised it.");

    /// <summary>The gateway event is not in a state that can be replayed.</summary>
    public static Error EventNotReplayable { get; } =
        Error.Conflict(
            "GATEWAY_EVENT_NOT_REPLAYABLE",
            "Only a failed or dead-lettered event can be replayed.");

    /// <summary>The cash was already collected, remitted or waived.</summary>
    public static Error CodNotOpen { get; } =
        Error.Conflict("COD_COLLECTION_NOT_OPEN", "That cash collection has already been settled.");

    /// <summary>The captured amount does not match what the order asked for.</summary>
    /// <remarks>
    /// The refusal docs/07-security-compliance.md §4 requires: an order is confirmed against a figure
    /// re-fetched from the gateway and compared to the order total, and a short capture is a
    /// discrepancy for a human rather than an order to confirm anyway.
    /// </remarks>
    /// <param name="expected">What the order asked for.</param>
    /// <param name="actual">What the gateway says it took.</param>
    public static Error AmountMismatch(decimal expected, decimal actual)
        => Error.Conflict(
            "PAYMENT_AMOUNT_MISMATCH",
            $"The gateway captured {actual:0.00} against an order for {expected:0.00}.");

    /// <summary>The order this payment belongs to could not be read.</summary>
    /// <param name="detail">What the ordering module said.</param>
    public static Error OrderUnavailable(string? detail = null)
        => Error.Unavailable(
            "PAYMENT_ORDER_UNAVAILABLE",
            detail is { Length: > 0 } ? detail : "The order could not be read.");
}
