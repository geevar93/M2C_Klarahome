namespace KlaraHome.Modules.Inventory.Application;

/// <summary>Query helpers shared by this module's list endpoints.</summary>
internal static class InventoryQueries
{
    /// <summary>
    /// Escapes a caller's search term for <c>ILIKE</c>.
    /// </summary>
    /// <remarks>
    /// Without it, a search for "50%" matches every row, and a search for "_" matches every row of
    /// length one. The backslash is escaped first, or escaping the wildcards would corrupt it.
    /// </remarks>
    /// <param name="term">What the caller typed.</param>
    public static string EscapeLike(string term)
    {
        ArgumentNullException.ThrowIfNull(term);

        return term.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }
}
