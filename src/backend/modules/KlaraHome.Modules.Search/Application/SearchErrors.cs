using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Search.Application;

/// <summary>
/// Every failure this module reports, with the stable code the frontend switches on
/// (docs/04-api-specification.md §1.2).
/// </summary>
/// <remarks>
/// Declared in one place so two handlers cannot answer the same situation with two different codes.
/// Notably short, and it should be: a search that matches nothing is a successful search with an
/// empty page, never an error, and the day somebody turns "no results" into a 404 is the day the
/// storefront starts showing an error screen to a shopper who merely typed a word this store does
/// not stock.
/// </remarks>
internal static class SearchErrors
{
    /// <summary>The query was shorter than the store's minimum.</summary>
    /// <remarks>
    /// A refusal rather than an empty page, because the storefront must be able to tell "keep
    /// typing" from "nothing matched", and an empty result set says the second.
    /// </remarks>
    /// <param name="minimum">How many characters are needed.</param>
    public static Error QueryTooShort(int minimum)
        => Error.Validation(
            "SEARCH_QUERY_TOO_SHORT",
            $"Type at least {minimum} characters to search.");

    /// <summary>The sort key is not one this platform serves.</summary>
    /// <param name="allowed">The ones that are.</param>
    public static Error UnknownSort(IReadOnlyList<string> allowed)
        => Error.Validation(
            "SEARCH_UNKNOWN_SORT",
            $"That is not a sort this store offers. Use one of: {string.Join(", ", allowed)}.");

    /// <summary>The page cursor could not be read.</summary>
    /// <remarks>
    /// A client error rather than an exception. A cursor is opaque and a caller may have kept a
    /// stale one; the answer is to say so and let them start again from the first page.
    /// </remarks>
    public static Error InvalidCursor { get; } =
        Error.Malformed("SEARCH_INVALID_CURSOR", "That page cursor is not valid. Start again from the first page.");

    /// <summary>The click handle could not be read, or names a query that is no longer held.</summary>
    /// <remarks>
    /// The log is retained for a year and partitioned by month, so a handle from a bookmarked page
    /// eventually names a partition that has been detached. It is not worth an error a shopper could
    /// see, which is why the endpoint that takes it answers 204 either way and this is only ever
    /// reported for a handle that is malformed.
    /// </remarks>
    public static Error InvalidQueryToken { get; } =
        Error.Malformed("SEARCH_INVALID_QUERY_TOKEN", "That search handle is not valid.");

    /// <summary>The synonym, stop word or indexed variant does not exist.</summary>
    /// <param name="what">What was being looked for, in words a caller can read.</param>
    public static Error NotFound(string what)
        => Error.NotFound("SEARCH_NOT_FOUND", $"That {what} does not exist.");

    /// <summary>A synonym already exists for that word.</summary>
    /// <remarks>
    /// Refused rather than merged. Two rules for "sofa" would be two answers to one question, and
    /// which applied would depend on the order the loader happened to read them — so the second
    /// attempt is told to edit the first.
    /// </remarks>
    /// <param name="term">The word.</param>
    public static Error DuplicateTerm(string term)
        => Error.Conflict(
            "SEARCH_SYNONYM_EXISTS",
            $"There is already a synonym rule for '{term}'. Edit that one instead.");

    /// <summary>That word is already in the stop list.</summary>
    /// <param name="word">The word.</param>
    public static Error DuplicateStopWord(string word)
        => Error.Conflict("SEARCH_STOP_WORD_EXISTS", $"'{word}' is already ignored.");

    /// <summary>
    /// A synonym term or expansion was not a single word.
    /// </summary>
    /// <remarks>
    /// A phrase synonym needs a phrase operator and a different index, and half-supporting one — by
    /// matching only its first word — would be worse than refusing it: the rule would appear to work
    /// and would quietly mean something else. Recorded in the parking lot as the thing to build if
    /// merchandising asks for it.
    /// </remarks>
    /// <param name="value">What was supplied.</param>
    public static Error NotSingleWord(string value)
        => Error.Validation(
            "SEARCH_SYNONYM_NOT_A_WORD",
            $"'{value}' must be a single word of letters or digits. Phrase synonyms are not supported.");

    /// <summary>A synonym rule was saved with nothing to expand to.</summary>
    public static Error NoExpansions { get; } =
        Error.Validation(
            "SEARCH_SYNONYM_NO_EXPANSIONS",
            "A synonym rule needs at least one word to expand to.");
}
