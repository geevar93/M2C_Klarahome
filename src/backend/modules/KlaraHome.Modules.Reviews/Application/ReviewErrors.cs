using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Reviews.Application;

/// <summary>
/// Every failure this module reports, with the stable code the frontend switches on
/// (docs/04-api-specification.md §1.2).
/// </summary>
/// <remarks>
/// <para>
/// Unusually for this platform, most of these are read by a <em>shopper</em> rather than by an
/// operator, and that changes what a good message is. Somebody who has just typed four hundred words
/// and been refused needs to be told which of the two rules they met — they have not bought it, or
/// they have already reviewed it — because those have completely different next steps and a single
/// "cannot review" would send half of them to support.
/// </para>
/// <para>
/// <see cref="NotPurchased"/> is deliberately not a 403. A refusal that distinguished "you did not
/// buy this" from "this line does not exist" would tell an attacker which order line ids are real;
/// both answer the same way, and the message says what the rule is rather than what the caller got
/// wrong.
/// </para>
/// </remarks>
internal static class ReviewErrors
{
    /// <summary>The review, question, list or subscription does not exist.</summary>
    /// <param name="what">What was being looked for, in words a caller can read.</param>
    public static Error NotFound(string what)
        => Error.NotFound("REVIEWS_NOT_FOUND", $"That {what} does not exist.");

    /// <summary>
    /// There is no delivered purchase behind this review.
    /// </summary>
    /// <remarks>
    /// The one rule the whole module is built around, and the acceptance criterion the step is
    /// judged on. It covers four situations on purpose — no such line, somebody else's line, a
    /// cancelled line, and a line still in transit — because telling them apart would leak which
    /// order lines exist and would not help the honest caller, who is being told the rule either way.
    /// </remarks>
    public static Error NotPurchased { get; } =
        Error.Validation(
            "REVIEW_PURCHASE_REQUIRED",
            "You can only review something you bought and received.");

    /// <summary>That purchase has already been reviewed.</summary>
    /// <remarks>
    /// A conflict rather than a validation failure: the request is well formed and the rule is about
    /// the state of the world, which is what tells the storefront to offer "edit your review" instead
    /// of highlighting a field.
    /// </remarks>
    public static Error AlreadyReviewed { get; } =
        Error.Conflict(
            "REVIEW_ALREADY_EXISTS",
            "You have already reviewed this purchase. You can edit the review you wrote.");

    /// <summary>The score is not one to five.</summary>
    public static Error InvalidRating { get; } =
        Error.Validation("REVIEW_INVALID_RATING", "A rating is a whole number from 1 to 5.");

    /// <summary>Somebody is trying to change a review they did not write.</summary>
    public static Error NotAuthor { get; } =
        Error.Forbidden("REVIEW_NOT_AUTHOR", "You can only change a review you wrote.");

    /// <summary>A refusal was submitted with no reason.</summary>
    /// <remarks>
    /// Required, because a refusal with no reason cannot be explained to the shopper who wrote the
    /// review and cannot be reviewed by the next moderator to open the queue.
    /// </remarks>
    public static Error ReasonRequired { get; } =
        Error.Validation("REVIEW_REASON_REQUIRED", "Say why the review is being refused.");

    /// <summary>A seller is trying to reply to a review of somebody else's sale.</summary>
    public static Error NotYourReview { get; } =
        Error.Forbidden("REVIEW_NOT_YOUR_SALE", "You can only reply to a review of your own sale.");

    /// <summary>Somebody tried to vote on their own review.</summary>
    /// <remarks>
    /// Refused rather than silently ignored. It is a small thing, and a helpfulness score a reviewer
    /// can raise by clicking is not a score at all.
    /// </remarks>
    public static Error CannotVoteOwn { get; } =
        Error.Validation("REVIEW_CANNOT_VOTE_OWN", "You cannot vote on your own review.");

    /// <summary>The list already holds as much as it will.</summary>
    /// <param name="limit">How many items a list holds.</param>
    public static Error WishlistFull(int limit)
        => Error.Validation("WISHLIST_FULL", $"A list holds at most {limit} items.");

    /// <summary>Somebody tried to delete the list every customer must have.</summary>
    public static Error CannotDeleteDefaultList { get; } =
        Error.Validation(
            "WISHLIST_DEFAULT_PROTECTED",
            "Your saved-items list cannot be deleted. Empty it instead.");

    /// <summary>That share link is not one this store issued, or it has been revoked.</summary>
    public static Error UnknownShareToken { get; } =
        Error.NotFound("WISHLIST_SHARE_UNKNOWN", "That link is no longer shared.");

    /// <summary>The same thing is already being watched.</summary>
    public static Error AlreadySubscribed { get; } =
        Error.Conflict("SUBSCRIPTION_EXISTS", "You are already waiting to hear about this.");

    /// <summary>A subscription was submitted with nowhere to send the alert.</summary>
    /// <remarks>
    /// The check exists because the anonymous case is real: somebody who is not signed in may still
    /// ask to be told, and the row is worthless without an address. A signed-in shopper never sees
    /// this — their account carries the address.
    /// </remarks>
    public static Error NoContact { get; } =
        Error.Validation(
            "SUBSCRIPTION_NO_CONTACT",
            "Sign in or give an email address so we know where to send the alert.");

    /// <summary>The named target price is not a price.</summary>
    public static Error InvalidTargetPrice { get; } =
        Error.Validation(
            "SUBSCRIPTION_INVALID_TARGET",
            "A target price must be above zero and below what it costs now.");

    /// <summary>The variant is not one the catalogue has.</summary>
    /// <remarks>
    /// A not-found rather than a validation failure. The caller named something that does not exist,
    /// which is the same answer the product page would give for the same id.
    /// </remarks>
    public static Error UnknownVariant { get; } =
        Error.NotFound("REVIEWS_UNKNOWN_VARIANT", "That product is no longer available.");

    /// <summary>The product is not one the catalogue has.</summary>
    public static Error UnknownProduct { get; } =
        Error.NotFound("REVIEWS_UNKNOWN_PRODUCT", "That product is no longer available.");

    /// <summary>Somebody has already reported this and their complaint is still open.</summary>
    /// <remarks>
    /// Refused rather than accepted-and-deduplicated, because the report count is what a moderator
    /// triages on: one person reporting the same review five times must not outrank five people
    /// reporting five different ones.
    /// </remarks>
    public static Error AlreadyReported { get; } =
        Error.Conflict("REPORT_ALREADY_OPEN", "You have already reported this. We are looking at it.");

    /// <summary>The caller must be signed in to do this.</summary>
    /// <remarks>
    /// Reviews, questions, wishlists and votes all need an account, and every one of them says so
    /// with this. The endpoints require authentication in any case; this is what a handler answers if
    /// it is ever reached without a caller — a background dispatch, or a misconfigured route.
    /// </remarks>
    public static Error SignInRequired { get; } =
        Error.Unauthorized("REVIEWS_SIGN_IN_REQUIRED", "Sign in to do that.");
}
