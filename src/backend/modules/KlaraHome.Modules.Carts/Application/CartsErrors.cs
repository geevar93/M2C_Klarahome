using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Carts.Application;

/// <summary>
/// Every failure this module reports, with the stable code the frontend switches on
/// (docs/04-api-specification.md §1.2).
/// </summary>
/// <remarks>
/// Declared in one place so two handlers cannot answer the same situation with two different codes —
/// which is how a frontend ends up switching on message text. Several codes are deliberately the
/// ones Inventory and Pricing already use, so a cart that forwards a refusal does not have to
/// translate it.
/// </remarks>
internal static class CartsErrors
{
    /// <summary>The basket, line or session does not exist, or is not this caller's.</summary>
    /// <param name="what">What was being looked for, in words a caller can read.</param>
    public static Error NotFound(string what)
        => Error.NotFound("CART_NOT_FOUND", $"That {what} does not exist.");

    /// <summary>The basket has been converted, abandoned or expired and can no longer be edited.</summary>
    public static Error CartClosed { get; } =
        Error.Conflict("CART_CLOSED", "That basket has been closed. Start a new one.");

    /// <summary>The basket has nothing in it.</summary>
    public static Error CartEmpty { get; } =
        Error.Validation("CART_EMPTY", "Your basket is empty.");

    /// <summary>The basket already holds as many distinct offers as it may.</summary>
    /// <param name="limit">The ceiling.</param>
    public static Error TooManyLines(int limit)
        => Error.Validation("CART_TOO_MANY_LINES", $"A basket can hold at most {limit} different items.");

    /// <summary>The offer named is not one the catalogue knows about, or is no longer purchasable.</summary>
    public static Error UnknownListing { get; } =
        Error.Validation("CART_UNKNOWN_LISTING", "That item is no longer available.");

    /// <summary>Checkout requires an account.</summary>
    public static Error SignInRequired { get; } =
        Error.Unauthorized("CHECKOUT_SIGN_IN_REQUIRED", "Sign in to complete your order.");

    /// <summary>The basket has an issue the shopper has to resolve before paying.</summary>
    public static Error CartNotReady { get; } =
        Error.Validation("CART_NOT_READY", "Some items in your basket need your attention before you can pay.");

    /// <summary>The checkout session has been placed, abandoned or has expired.</summary>
    public static Error CheckoutClosed { get; } =
        Error.Conflict("CHECKOUT_CLOSED", "That checkout is no longer open. Start again from your basket.");

    /// <summary>A step was attempted before the one it depends on.</summary>
    /// <param name="missing">What has not been chosen yet.</param>
    public static Error CheckoutIncomplete(string missing)
        => Error.Validation("CHECKOUT_INCOMPLETE", $"Choose {missing} before continuing.");

    /// <summary>The address named is not one of this shopper's.</summary>
    public static Error UnknownAddress { get; } =
        Error.NotFound("CHECKOUT_UNKNOWN_ADDRESS", "That address is not on your account.");

    /// <summary>No seller in the basket delivers to the chosen address.</summary>
    public static Error NotServiceable { get; } =
        Error.Validation(
            "CHECKOUT_NOT_SERVICEABLE",
            "None of the sellers in your basket deliver to that address.");

    /// <summary>
    /// The store does not deliver to that address (ADR-018).
    /// </summary>
    /// <remarks>
    /// Kept separate from <see cref="NotServiceable"/> on purpose. This is the store's own trading
    /// decision and carries the operator's own words; that one is a fact about what a courier will
    /// carry. A shopper reading "no courier goes there" about an address the store simply has not
    /// opened yet would be told something true and misleading.
    /// </remarks>
    /// <param name="message">What the operator wants an out-of-area shopper told.</param>
    public static Error NotCovered(string? message)
        => Error.Validation(
            "DELIVERY_AREA_NOT_COVERED",
            string.IsNullOrWhiteSpace(message)
                ? "We do not deliver to that area yet."
                : message);

    /// <summary>No courier will carry a parcel to that PIN code.</summary>
    public static Error PincodeNotServiceable { get; } =
        Error.Validation(
            "PINCODE_NOT_SERVICEABLE",
            "No courier currently delivers to that PIN code.");

    /// <summary>The delivery choice named is not one that was offered.</summary>
    public static Error UnknownShippingOption { get; } =
        Error.Validation("CHECKOUT_UNKNOWN_SHIPPING_OPTION", "That delivery option is not available.");

    /// <summary>Cash on delivery is not available for this basket, and why.</summary>
    /// <param name="reason">The reason, in words a shopper can act on.</param>
    public static Error CodUnavailable(string reason)
        => Error.Validation("CHECKOUT_COD_UNAVAILABLE", reason);

    /// <summary>The payment method named is not one this store offers.</summary>
    public static Error UnknownPaymentMethod { get; } =
        Error.Validation("CHECKOUT_UNKNOWN_PAYMENT_METHOD", "That payment method is not offered.");

    /// <summary>The request carried no idempotency key.</summary>
    public static Error IdempotencyKeyRequired { get; } =
        Error.Malformed(
            "IDEMPOTENCY_KEY_REQUIRED",
            "Placing an order requires an Idempotency-Key header.");

    /// <summary>The key has already been used against a different basket.</summary>
    public static Error IdempotencyKeyReused { get; } =
        Error.Conflict(
            "IDEMPOTENCY_KEY_REUSED",
            "That request key has already been used for a different order.");

    /// <summary>A request carrying the same key is still running.</summary>
    public static Error PlacementInProgress { get; } =
        Error.Conflict("ORDER_PLACEMENT_IN_PROGRESS", "Your order is already being placed. Please wait.");

    /// <summary>
    /// There was not enough stock to hold when the order was placed.
    /// </summary>
    /// <remarks>
    /// The code is the one docs/04-api-specification.md §1.2 already names for a cart, and the one
    /// Inventory raises, so a shopper sees one message whichever layer refused.
    /// </remarks>
    public static Error OutOfStock { get; } =
        Error.Conflict("CART_ITEM_OUT_OF_STOCK", "Some items sold out while you were checking out.");

    /// <summary>The Ordering module is not installed in this deployment.</summary>
    /// <remarks>
    /// The honest answer between Step 13 and Step 14: checkout is complete and there is nothing to
    /// hand the agreed basket to. A 503 that names the reason beats a missing-service exception.
    /// </remarks>
    public static Error OrderingUnavailable { get; } =
        Error.Unavailable(
            "ORDERING_UNAVAILABLE",
            "Orders cannot be placed on this deployment yet.");
}
