using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Orders.Application;

/// <summary>
/// Every failure this module reports, with the stable code the frontend switches on
/// (docs/04-api-specification.md §1.2).
/// </summary>
/// <remarks>
/// Declared in one place so two handlers cannot answer the same situation with two different codes —
/// which is how a frontend ends up switching on message text. Several codes are deliberately the
/// ones Cart, Inventory and Pricing already use, so an order that forwards a refusal does not have to
/// translate it.
/// </remarks>
internal static class OrdersErrors
{
    /// <summary>The order, sub-order or invoice does not exist, or is not this caller's.</summary>
    /// <remarks>
    /// One code for both cases, on purpose. Telling a caller that somebody else's order id is real
    /// is a disclosure, and 404 is what docs/04-api-specification.md §1.2 requires for "not visible
    /// to this caller".
    /// </remarks>
    /// <param name="what">What was being looked for, in words a caller can read.</param>
    public static Error NotFound(string what)
        => Error.NotFound("ORDER_NOT_FOUND", $"That {what} does not exist.");

    /// <summary>The transition asked for is not one the machine has.</summary>
    /// <param name="from">Where the sub-order is.</param>
    /// <param name="to">Where the caller wanted it.</param>
    public static Error InvalidTransition(object from, object to)
        => Error.Conflict(
            "ORDER_INVALID_TRANSITION",
            $"An order cannot go from {from} to {to}.");

    /// <summary>The machine has the edge, but not for this caller.</summary>
    /// <param name="to">Where the caller wanted it.</param>
    public static Error TransitionNotPermitted(object to)
        => Error.Forbidden(
            "ORDER_TRANSITION_NOT_PERMITTED",
            $"You are not allowed to move this order to {to}.");

    /// <summary>The status named is not one this platform has.</summary>
    public static Error UnknownStatus { get; } =
        Error.Validation("ORDER_UNKNOWN_STATUS", "That is not an order status.");

    /// <summary>
    /// The order has gone too far to be cancelled by whoever is asking.
    /// </summary>
    /// <remarks>
    /// The code docs/04-api-specification.md §1.2 already names, so the storefront has one branch for
    /// it whether the refusal came from the shopper's own window closing or from the state machine.
    /// </remarks>
    public static Error NotCancellable { get; } =
        Error.Conflict(
            "ORDER_NOT_CANCELLABLE",
            "This order has gone too far to be cancelled. Contact support if you still need to stop it.");

    /// <summary>Every unit named has already been cancelled.</summary>
    public static Error NothingToCancel { get; } =
        Error.Validation("ORDER_NOTHING_TO_CANCEL", "There is nothing left to cancel on this order.");

    /// <summary>Operations cancelled something after dispatch without saying why.</summary>
    public static Error CancellationReasonRequired { get; } =
        Error.Validation(
            "ORDER_CANCELLATION_REASON_REQUIRED",
            "Cancelling a dispatched order needs a reason.");

    /// <summary>A line named in a partial cancellation is not on the sub-order.</summary>
    public static Error UnknownLine { get; } =
        Error.Validation("ORDER_UNKNOWN_LINE", "That item is not on this order.");

    /// <summary>The quote handed to the placement had no lines the catalogue still recognises.</summary>
    public static Error NothingToOrder { get; } =
        Error.Validation("ORDER_NOTHING_TO_ORDER", "There is nothing in this order.");

    /// <summary>The shopper the checkout named is not one this platform has, or cannot transact.</summary>
    public static Error UnknownCustomer { get; } =
        Error.Validation("ORDER_UNKNOWN_CUSTOMER", "That account cannot place orders.");

    /// <summary>A seller in the basket stopped trading between the review screen and the order.</summary>
    /// <param name="vendor">The seller, in words a shopper can read.</param>
    public static Error VendorInactive(string vendor)
        => Error.Validation("VENDOR_INACTIVE", $"{vendor} is no longer taking orders.");

    /// <summary>
    /// A sub-order has already been invoiced.
    /// </summary>
    /// <remarks>
    /// A conflict rather than a silent success, because the two callers mean different things: the
    /// automatic issue on dispatch is idempotent and never reaches this, and an operator pressing
    /// "raise invoice" on an already-invoiced sub-order needs to be told it exists rather than given
    /// a second number.
    /// </remarks>
    public static Error AlreadyInvoiced { get; } =
        Error.Conflict("ORDER_ALREADY_INVOICED", "A tax invoice has already been raised for this order.");

    /// <summary>An invoice was asked for on a sub-order that is not far enough along to have one.</summary>
    public static Error NotInvoiceable { get; } =
        Error.Conflict(
            "ORDER_NOT_INVOICEABLE",
            "A tax invoice can only be raised once the order is confirmed.");

    /// <summary>The invoice exists but its PDF does not.</summary>
    public static Error InvoiceFileMissing { get; } =
        Error.NotFound("ORDER_INVOICE_FILE_MISSING", "That invoice has no document to download yet.");

    /// <summary>
    /// A re-render of an invoice that has no document failed too.
    /// </summary>
    /// <remarks>
    /// Reported rather than swallowed, because the two callers differ. The automatic issue at
    /// dispatch swallows a rendering failure so a parcel is not held up by a storage outage; an
    /// operator who has pressed "raise invoice" precisely to repair that missing document is asking
    /// about the document, and answering them with a success would be answering the wrong question.
    /// </remarks>
    public static Error InvoiceRenderFailed { get; } =
        Error.Unavailable(
            "ORDER_INVOICE_RENDER_FAILED",
            "The invoice document could not be produced. Try again once document storage is back.");

    /// <summary>The Payments module is not installed in this deployment.</summary>
    /// <remarks>
    /// The honest answer between Step 14 and Step 15: the order can be created and there is nothing
    /// to collect the money with. A 503 that names the reason beats an order nobody can pay for.
    /// Cash on delivery does not go through the gateway and is unaffected.
    /// </remarks>
    public static Error PaymentsUnavailable { get; } =
        Error.Unavailable(
            "PAYMENTS_UNAVAILABLE",
            "Card and UPI payments are not available on this deployment yet.");
}
