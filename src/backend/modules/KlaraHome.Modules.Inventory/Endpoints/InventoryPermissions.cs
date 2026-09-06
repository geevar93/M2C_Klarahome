namespace KlaraHome.Modules.Inventory.Endpoints;

/// <summary>
/// The permissions this module's endpoints declare.
/// </summary>
/// <remarks>
/// Declared here and mirrored in the Identity module's permission catalogue, which is what the admin
/// UI lists and what a role grants. The duplication is deliberate and is what the module boundary
/// costs: a module may not reference another module, so the two lists are kept in step by a test
/// that asserts every permission an endpoint asks for appears in the catalogue — the same
/// arrangement the Media, Vendors and Catalog modules use.
/// </remarks>
internal static class InventoryPermissions
{
    /// <summary>List stock rows, read one, and read its ledger, holds, lots and serials.</summary>
    public const string StockRead = "inventory.stock.read";

    /// <summary>
    /// Move stock by hand and set a row's replenishment policy.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="StockRead"/> because writing a correction is how an operator makes
    /// stock appear from nowhere, and it is the one action in this module a supervisor signs off.
    /// </remarks>
    public const string StockAdjust = "inventory.stock.adjust";

    /// <summary>Open, rename and close stock locations.</summary>
    public const string WarehouseManage = "inventory.warehouse.manage";

    /// <summary>Keep suppliers, raise purchase orders and book goods in.</summary>
    public const string PurchasingManage = "inventory.purchasing.manage";

    /// <summary>Schedule a stock take, enter counts and submit the variances.</summary>
    public const string StockTakeManage = "inventory.stock-take.manage";
}
