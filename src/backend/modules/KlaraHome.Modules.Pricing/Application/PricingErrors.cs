using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Pricing.Application;

/// <summary>
/// Every failure this module reports, with the stable code the frontend switches on
/// (docs/04-api-specification.md §1.2).
/// </summary>
/// <remarks>
/// Declared in one place so two handlers cannot answer the same situation with two different codes —
/// which is how a frontend ends up switching on message text.
/// </remarks>
internal static class PricingErrors
{
    /// <summary>The row does not exist, or is outside the caller's scope.</summary>
    /// <param name="what">What was being looked for, in words a caller can read.</param>
    public static Error NotFound(string what)
        => Error.NotFound("PRICING_NOT_FOUND", $"That {what} does not exist.");

    /// <summary>A code is already taken.</summary>
    /// <param name="field">Which one.</param>
    public static Error Duplicate(string field)
        => Error.Conflict("PRICING_DUPLICATE", $"Another record already uses that {field}.");

    /// <summary>A vendor caller tried to act on somebody else's price list.</summary>
    public static Error OutOfScope { get; } =
        Error.Validation("PRICING_SCOPE", "You can only do that within your own organisation.");

    /// <summary>
    /// The coupon the shopper typed did nothing.
    /// </summary>
    /// <remarks>
    /// The code is the one docs/04-api-specification.md §1.2 already names, so a cart that forwards
    /// this refusal does not have to translate it. The message is the evaluator's own explanation
    /// rather than a generic one, because "this offer is for a first order" is actionable and
    /// "invalid coupon" is not.
    /// </remarks>
    /// <param name="reason">Why it did not apply.</param>
    public static Error CouponInvalid(string reason) => Error.Validation("COUPON_INVALID", reason);

    /// <summary>A quote was asked for with no lines in it.</summary>
    public static Error EmptyQuote { get; } =
        Error.Validation("PRICING_EMPTY_QUOTE", "There is nothing to price.");

    /// <summary>Every offer in the request was one the catalogue does not know about.</summary>
    public static Error UnknownListing { get; } =
        Error.Validation("PRICING_UNKNOWN_LISTING", "That offer does not exist in the catalogue.");

    /// <summary>A price list item was set for an offer the catalogue does not know about.</summary>
    /// <param name="listingId">The offer that could not be resolved.</param>
    public static Error UnknownListingItem(Guid listingId)
        => Error.Validation("PRICING_UNKNOWN_LISTING", $"Offer {listingId} does not exist in the catalogue.");

    /// <summary>A batch of price-list items was larger than this deployment will take in one request.</summary>
    /// <param name="maximum">The limit.</param>
    public static Error TooManyItems(int maximum)
        => Error.Validation(
            "PRICING_TOO_MANY_ITEMS",
            $"A price update is limited to {maximum} rows. Send the file in sections.");

    /// <summary>
    /// A tax rate was recorded for a code and start date that already has one.
    /// </summary>
    /// <remarks>
    /// Its own code rather than the generic duplicate, because the fix is specific: a rate change
    /// closes the old row and opens a new one from the following day, and an operator who has hit
    /// this is trying to edit history instead.
    /// </remarks>
    public static Error DuplicateTaxRate { get; } =
        Error.Conflict(
            "PRICING_DUPLICATE_TAX_RATE",
            "A rate for that HSN code already starts on that date. Close it and open a new one instead.");

    /// <summary>Store credit is switched off in this deployment.</summary>
    public static Error WalletDisabled { get; } =
        Error.NotFound("FEATURE_DISABLED", "Store credit is not enabled on this store.");

    /// <summary>An adjustment would have taken a wallet below zero.</summary>
    public static Error InsufficientCredit { get; } =
        Error.Conflict("PRICING_INSUFFICIENT_CREDIT", "There is not enough store credit for that.");

    /// <summary>An adjustment of nothing is a no-op that would leave a meaningless statement line.</summary>
    public static Error EmptyAdjustment { get; } =
        Error.Validation("PRICING_EMPTY_ADJUSTMENT", "An adjustment has to change something.");
}
