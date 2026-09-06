using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Returns.Application;

/// <summary>
/// Every failure this module reports, with the stable code the frontend switches on
/// (docs/04-api-specification.md §1.2).
/// </summary>
/// <remarks>
/// Declared in one place so two handlers cannot answer the same situation with two different codes.
/// Most of these are read by a shopper who has already been disappointed once, so the wording says
/// what they can do rather than what the platform refused.
/// </remarks>
internal static class ReturnsErrors
{
    /// <summary>The return, reason code or credit note does not exist, or is not this caller's.</summary>
    /// <remarks>
    /// One code for both cases, on purpose. Telling a shopper that somebody else's RMA number is real
    /// is a disclosure, and 404 is what docs/04-api-specification.md §1.2 requires for "not visible
    /// to this caller".
    /// </remarks>
    /// <param name="what">What was being looked for, in words a caller can read.</param>
    public static Error NotFound(string what)
        => Error.NotFound("RETURN_NOT_FOUND", $"That {what} does not exist.");

    /// <summary>The return window on this order has closed.</summary>
    /// <remarks>
    /// Distinct from every other refusal because it is the only one a shopper can do nothing about,
    /// and because a storefront shows a different screen for it. The date is in the message so
    /// support does not have to look it up.
    /// </remarks>
    /// <param name="closedOn">When it closed.</param>
    public static Error WindowClosed(DateTimeOffset? closedOn)
        => Error.Conflict(
            "RETURN_WINDOW_CLOSED",
            closedOn is { } date
                ? $"The return window for this order closed on {date:d MMMM yyyy}."
                : "The return window for this order has closed.");

    /// <summary>The seller's part has not been delivered, so there is nothing to send back yet.</summary>
    /// <param name="status">Where it actually is.</param>
    public static Error NotDelivered(string status)
        => Error.Conflict(
            "RETURN_NOT_DELIVERED",
            $"That order is {status}. You can only return something once it has been delivered.");

    /// <summary>The product was sold as non-returnable, and said so on the listing.</summary>
    /// <param name="name">What it is called.</param>
    public static Error NotReturnable(string name)
        => Error.Conflict("RETURN_ITEM_NOT_RETURNABLE", $"{name} cannot be returned.");

    /// <summary>More units were asked for than are left to send back.</summary>
    /// <param name="name">What it is called.</param>
    /// <param name="available">How many are left.</param>
    public static Error TooManyUnits(string name, int available)
        => Error.Validation(
            "RETURN_QUANTITY_UNAVAILABLE",
            available <= 0
                ? $"There are no units of {name} left to return."
                : $"Only {available} unit(s) of {name} can still be returned.");

    /// <summary>The request named no units at all.</summary>
    public static Error NothingToReturn { get; } =
        Error.Validation("RETURN_NO_LINES", "Choose at least one item to return.");

    /// <summary>The reason code is not one this store offers.</summary>
    public static Error UnknownReason { get; } =
        Error.Validation("RETURN_REASON_UNKNOWN", "Choose a reason from the list.");

    /// <summary>The reason needs a photograph and none was attached.</summary>
    public static Error EvidenceRequired { get; } =
        Error.Validation(
            "RETURN_EVIDENCE_REQUIRED",
            "Attach a photograph of the item so we can look into it.");

    /// <summary>More photographs were attached than the store accepts.</summary>
    /// <param name="maximum">How many it accepts.</param>
    public static Error TooMuchEvidence(int maximum)
        => Error.Validation(
            "RETURN_EVIDENCE_LIMIT",
            $"Attach no more than {maximum} photograph(s).");

    /// <summary>A photograph was named that this shopper cannot use.</summary>
    /// <remarks>
    /// A file id is a guessable-looking value and a return is a place a caller can attach one, so an
    /// id the media library does not know is refused rather than stored — an RMA carrying a
    /// reference to somebody else's private file would be a disclosure waiting for a screen to
    /// render it.
    /// </remarks>
    public static Error EvidenceUnknown { get; } =
        Error.Validation("RETURN_EVIDENCE_UNKNOWN", "One of the attached files could not be found.");

    /// <summary>Replacements are not offered, or not for this reason.</summary>
    public static Error ReplacementUnavailable { get; } =
        Error.Conflict(
            "RETURN_REPLACEMENT_UNAVAILABLE",
            "A replacement is not available for this item. Ask for a refund instead.");

    /// <summary>There is already an open return covering these units.</summary>
    /// <param name="returnNumber">The one that already exists.</param>
    public static Error AlreadyOpen(string returnNumber)
        => Error.Conflict(
            "RETURN_ALREADY_OPEN",
            $"Return {returnNumber} is already open for this order. Follow that one instead.");

    /// <summary>The machine does not have that edge, or this caller may not take it.</summary>
    /// <param name="from">Where it is.</param>
    /// <param name="to">Where it was being moved to.</param>
    public static Error InvalidTransition(object from, object to)
        => Error.Conflict(
            "RETURN_INVALID_TRANSITION",
            $"A return that is {from} cannot become {to}.");

    /// <summary>The return has finished and nothing further can be done to it.</summary>
    /// <param name="status">Where it ended.</param>
    public static Error AlreadyClosed(object status)
        => Error.Conflict("RETURN_CLOSED", $"That return is {status} and can no longer be changed.");

    /// <summary>A cancellation was asked for after the courier already had the goods.</summary>
    public static Error TooLateToCancel { get; } =
        Error.Conflict(
            "RETURN_NOT_CANCELLABLE",
            "The courier already has your parcel, so this return can no longer be cancelled.");

    /// <summary>Quality control was asked for on a return whose goods have not arrived.</summary>
    public static Error NotReceived { get; } =
        Error.Conflict(
            "RETURN_NOT_RECEIVED",
            "Book the parcel in before recording a quality check against it.");

    /// <summary>A refund was asked for on a return that has not passed quality control.</summary>
    public static Error NotRefundable { get; } =
        Error.Conflict(
            "RETURN_NOT_REFUNDABLE",
            "This return has not passed its quality check, so no refund is due from it yet.");

    /// <summary>A refund was asked for that has already been paid.</summary>
    public static Error AlreadyRefunded { get; } =
        Error.Conflict("RETURN_ALREADY_REFUNDED", "This return has already been refunded.");

    /// <summary>More was asked for than the return is worth.</summary>
    /// <param name="maximum">What it is worth.</param>
    public static Error RefundTooLarge(decimal maximum)
        => Error.Validation(
            "RETURN_REFUND_TOO_LARGE",
            $"This return is worth at most {maximum:0.00}. Refund that or less.");

    /// <summary>There is no money left to give back.</summary>
    /// <remarks>
    /// The honest answer for a cash-on-delivery parcel refused at the door, and for an order already
    /// refunded in full. It is deliberately not a failure of the return: the goods still came back
    /// and the credit note is still raised.
    /// </remarks>
    public static Error NothingRefundable { get; } =
        Error.Conflict(
            "RETURN_NOTHING_REFUNDABLE",
            "No money was collected for this order, so there is nothing to refund.");

    /// <summary>A refund to store credit was asked for on a deployment that has the wallet off.</summary>
    public static Error WalletUnavailable { get; } =
        Error.Unavailable(
            "RETURN_WALLET_UNAVAILABLE",
            "Store credit is not available on this store. Choose the original payment method.");

    /// <summary>A reverse pickup could not be booked.</summary>
    /// <param name="detail">What went wrong, in words safe to show an operator.</param>
    public static Error PickupFailed(string? detail = null)
        => Error.Unavailable(
            "RETURN_PICKUP_FAILED",
            detail is { Length: > 0 }
                ? detail
                : "The courier could not be reached. The return is unchanged; try again, or book the "
                  + "collection by hand.");

    /// <summary>The order could not be read over the seam.</summary>
    /// <param name="detail">What the ordering module said.</param>
    public static Error OrderUnavailable(string? detail = null)
        => Error.Unavailable(
            "RETURN_ORDER_UNAVAILABLE",
            detail is { Length: > 0 } ? detail : "That order could not be read.");

    /// <summary>A reason code was created with one that already exists.</summary>
    public static Error DuplicateReason { get; } =
        Error.Conflict("RETURN_REASON_DUPLICATE", "A reason with that code already exists.");

    /// <summary>The caller may not act on another seller's return.</summary>
    public static Error NotYours { get; } =
        Error.Forbidden("RETURN_NOT_YOURS", "That return belongs to another seller.");
}
