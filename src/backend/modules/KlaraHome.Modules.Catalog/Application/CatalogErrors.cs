using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Catalog.Application;

/// <summary>
/// Every failure this module reports, with the stable code the frontend switches on
/// (docs/04-api-specification.md §1.2).
/// </summary>
/// <remarks>
/// Declared in one place so two handlers cannot answer the same situation with two different
/// codes — which is how a frontend ends up switching on message text.
/// </remarks>
internal static class CatalogErrors
{
    /// <summary>The row does not exist, or is outside the caller's scope.</summary>
    /// <param name="what">What was being looked for, in words a caller can read.</param>
    public static Error NotFound(string what)
        => Error.NotFound("CATALOG_NOT_FOUND", $"That {what} does not exist.");

    /// <summary>A slug, code or SKU is already taken.</summary>
    /// <param name="field">Which one.</param>
    public static Error Duplicate(string field)
        => Error.Conflict("CATALOG_DUPLICATE", $"Another record already uses that {field}.");

    /// <summary>The life cycle does not allow the requested move.</summary>
    /// <param name="from">Where the record is.</param>
    /// <param name="to">Where the caller tried to take it.</param>
    public static Error InvalidTransition(object from, object to)
        => Error.Conflict("CATALOG_INVALID_TRANSITION", $"That cannot go from {from} to {to}.");

    /// <summary>A publishing rule has not been met.</summary>
    /// <param name="because">Which rule, in words shown to the operator.</param>
    public static Error NotPublishable(string because)
        => Error.Validation("CATALOG_NOT_PUBLISHABLE", because);

    /// <summary>The mandatory India disclosures are incomplete.</summary>
    /// <param name="gaps">What is missing.</param>
    public static Error ComplianceIncomplete(IReadOnlyList<string> gaps)
        => Error.Validation(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["compliance"] = gaps },
            "CATALOG_COMPLIANCE_INCOMPLETE",
            "This cannot be published until its mandatory disclosures are complete.");

    /// <summary>A vendor caller tried to act on somebody else's product or offer.</summary>
    public static Error OutOfScope { get; } =
        Error.Validation("CATALOG_SCOPE", "You can only do that within your own organisation.");

    /// <summary>A seller who is not trading cannot have a live offer.</summary>
    public static Error VendorInactive { get; } =
        Error.Validation("VENDOR_INACTIVE", "That seller is not currently trading.");

    /// <summary>Another variant of this product already has this combination of options.</summary>
    public static Error DuplicateCombination { get; } =
        Error.Conflict(
            "CATALOG_DUPLICATE_COMBINATION",
            "Another variant of this product already has that combination of options.");

    /// <summary>An attribute that is not a closed list was offered as a variant axis.</summary>
    /// <param name="code">The attribute's code.</param>
    public static Error NotAVariantAxis(string code)
        => Error.Validation(
            "CATALOG_NOT_A_VARIANT_AXIS",
            $"Attribute '{code}' is not a variant axis. Only select and multiselect attributes can be.");

    /// <summary>The selling price is above the MRP, which is illegal in India.</summary>
    public static Error PriceAboveMrp { get; } =
        Error.Validation("CATALOG_PRICE_ABOVE_MRP", "The selling price cannot be above the MRP.");

    /// <summary>A category cannot be moved under one of its own descendants.</summary>
    public static Error CategoryCycle { get; } =
        Error.Validation("CATALOG_CATEGORY_CYCLE", "A category cannot be moved beneath itself.");

    /// <summary>The tree is already as deep as this platform allows.</summary>
    /// <param name="maxDepth">The limit.</param>
    public static Error CategoryTooDeep(int maxDepth)
        => Error.Validation(
            "CATALOG_CATEGORY_TOO_DEEP",
            $"The category tree is limited to {maxDepth} levels.");

    /// <summary>Something still points at the row the caller is trying to remove.</summary>
    /// <param name="what">What still points at it.</param>
    public static Error StillInUse(string what)
        => Error.Conflict("CATALOG_IN_USE", $"That cannot be removed while {what} still use it.");

    /// <summary>The uploaded import file was empty, unreadable or too large.</summary>
    /// <param name="why">What is wrong with it.</param>
    public static Error BadImportFile(string why)
        => Error.Validation("CATALOG_IMPORT_REJECTED", why);

    /// <summary>Object storage is not configured, so an import has nowhere to be put.</summary>
    public static Error StorageUnavailable { get; } =
        Error.Unavailable(
            "CATALOG_STORAGE_UNAVAILABLE",
            "File storage is not configured, so bulk import and export are unavailable.");

    /// <summary>A bulk status change named no products.</summary>
    public static Error BulkStatusEmpty { get; } =
        Error.Validation("CATALOG_BULK_STATUS_EMPTY", "Select at least one product.");

    /// <summary>A bulk status change named more products than one request may move.</summary>
    /// <param name="maximum">The cap.</param>
    public static Error BulkStatusTooLarge(int maximum)
        => Error.Validation(
            "CATALOG_BULK_STATUS_TOO_LARGE",
            $"One request may move at most {maximum} products. Use the bulk import for more.");
}
