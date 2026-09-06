namespace KlaraHome.Modules.Reviews.Infrastructure;

/// <summary>
/// Turns an account's display name into the name a public page shows.
/// </summary>
/// <remarks>
/// <para>
/// A review is a public document (docs/07-security-compliance.md §4) and this module stores the
/// shortened form rather than the full one. That is the deliberate order of operations: shortening
/// at read time would mean the full name sat in this schema being one bad projection away from a
/// product page, and there is nothing this module ever needs it for.
/// </para>
/// <para>
/// The rule is the one every marketplace converges on — given name, then the initial of whatever
/// follows. It is enough for a shopper to see that reviews come from different people, and not
/// enough to identify one of them by name in a search engine.
/// </para>
/// </remarks>
internal static class DisplayNames
{
    /// <summary>What a review with no resolvable author is signed.</summary>
    /// <remarks>
    /// A signed-in customer always has a display name, so this is the fallback for the account that
    /// has since been deleted — the review stays, and it stops naming somebody who is gone.
    /// </remarks>
    public const string Anonymous = "A customer";

    /// <summary>Shortens a display name for publication.</summary>
    /// <param name="displayName">The account's display name.</param>
    public static string Shorten(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Anonymous;
        }

        var parts = displayName
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length switch
        {
            0 => Anonymous,
            1 => parts[0],

            // The initial of the last part rather than of the second. "Priya Ramesh Kumar" becomes
            // "Priya K.", which is what the person is called; taking the second part would make it
            // "Priya R." and quietly rename them.
            _ => $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.",
        };
    }
}
