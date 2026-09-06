using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Inventory.Application;

/// <summary>
/// Every failure this module reports, with the stable code the frontend switches on
/// (docs/04-api-specification.md §1.2).
/// </summary>
/// <remarks>
/// Declared in one place so two handlers cannot answer the same situation with two different codes —
/// which is how a frontend ends up switching on message text.
/// </remarks>
internal static class InventoryErrors
{
    /// <summary>The row does not exist, or is outside the caller's scope.</summary>
    /// <param name="what">What was being looked for, in words a caller can read.</param>
    public static Error NotFound(string what)
        => Error.NotFound("INVENTORY_NOT_FOUND", $"That {what} does not exist.");

    /// <summary>A code or number is already taken.</summary>
    /// <param name="field">Which one.</param>
    public static Error Duplicate(string field)
        => Error.Conflict("INVENTORY_DUPLICATE", $"Another record already uses that {field}.");

    /// <summary>The life cycle does not allow the requested move.</summary>
    /// <param name="from">Where the document is.</param>
    /// <param name="to">Where the caller tried to take it.</param>
    public static Error InvalidTransition(object from, object to)
        => Error.Conflict("INVENTORY_INVALID_TRANSITION", $"That cannot go from {from} to {to}.");

    /// <summary>A vendor caller tried to act on somebody else's stock, warehouse or document.</summary>
    public static Error OutOfScope { get; } =
        Error.Validation("INVENTORY_SCOPE", "You can only do that within your own organisation.");

    /// <summary>
    /// The movement would have taken the location below empty.
    /// </summary>
    /// <remarks>
    /// This is what a refused conditional update means for an on-hand movement. It is a business
    /// answer, not a fault: the operator asked to remove more than is there.
    /// </remarks>
    public static Error InsufficientStock { get; } =
        Error.Conflict(
            "INVENTORY_INSUFFICIENT_STOCK",
            "There is not enough stock at that location for this movement.");

    /// <summary>
    /// There was not enough unheld stock to take the requested hold.
    /// </summary>
    /// <remarks>
    /// The code is the one docs/04-api-specification.md §1.2 already names for a cart, so a cart
    /// that forwards this refusal does not have to translate it.
    /// </remarks>
    public static Error OutOfStock { get; } =
        Error.Conflict("CART_ITEM_OUT_OF_STOCK", "There is not enough stock to reserve that quantity.");

    /// <summary>The offer named is not one the catalogue knows about.</summary>
    public static Error UnknownListing { get; } =
        Error.Validation("INVENTORY_UNKNOWN_LISTING", "That offer does not exist in the catalogue.");

    /// <summary>Stock cannot move through a location that has been closed.</summary>
    public static Error WarehouseInactive { get; } =
        Error.Validation("INVENTORY_WAREHOUSE_INACTIVE", "That location is closed to stock movement.");

    /// <summary>A purchase order cannot be raised on a supplier who is no longer trading.</summary>
    public static Error SupplierInactive { get; } =
        Error.Validation("INVENTORY_SUPPLIER_INACTIVE", "That supplier is no longer active.");

    /// <summary>The document has been sent or posted and can no longer be edited.</summary>
    public static Error DocumentFrozen { get; } =
        Error.Conflict(
            "INVENTORY_DOCUMENT_FROZEN",
            "That document has already been issued and can no longer be changed.");

    /// <summary>Goods were booked in against a line that has already been fully received.</summary>
    /// <param name="sku">Which line.</param>
    public static Error NothingOutstanding(string sku)
        => Error.Validation(
            "INVENTORY_NOTHING_OUTSTANDING",
            $"Everything ordered for {sku} has already been received.");

    /// <summary>Something still points at the row the caller is trying to remove.</summary>
    /// <param name="what">What still points at it.</param>
    public static Error StillInUse(string what)
        => Error.Conflict("INVENTORY_IN_USE", $"That cannot be removed while {what} still use it.");

    /// <summary>A stock take was scoped to more rows than this deployment will count in one sheet.</summary>
    /// <param name="maximum">The limit.</param>
    public static Error StockTakeTooLarge(int maximum)
        => Error.Validation(
            "INVENTORY_STOCK_TAKE_TOO_LARGE",
            $"A stock take is limited to {maximum} lines. Count the location in sections.");

    /// <summary>An adjustment of zero units is a no-op that would leave a meaningless ledger row.</summary>
    public static Error EmptyMovement { get; } =
        Error.Validation("INVENTORY_EMPTY_MOVEMENT", "A movement has to change something.");

    /// <summary>A transfer was asked for between one location and itself.</summary>
    public static Error SameWarehouse { get; } =
        Error.Validation("INVENTORY_SAME_WAREHOUSE", "A transfer needs two different locations.");
}
