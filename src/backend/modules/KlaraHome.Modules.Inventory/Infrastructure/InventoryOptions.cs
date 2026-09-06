using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Modules.Inventory.Infrastructure;

/// <summary>
/// How this deployment runs its stock.
/// </summary>
/// <remarks>
/// Configuration rather than store settings, on the same split the Catalog and Vendors modules
/// draw: the platform's own resource limits and background-loop wiring live here, where a
/// shopkeeper cannot change them from an admin screen. Per-item commercial choices — the reorder
/// level, whether to backorder — are on the stock item itself, because they are a seller's decision
/// and differ per shelf.
/// </remarks>
internal sealed class InventoryOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Inventory";

    /// <summary>The prefix of a generated purchase-order number — <c>PO-000017</c>.</summary>
    [RegularExpression("^[A-Z][A-Z0-9]{1,7}$")]
    public string PurchaseOrderPrefix { get; set; } = "PO";

    /// <summary>The prefix of a generated goods-receipt number — <c>GRN-000017</c>.</summary>
    [RegularExpression("^[A-Z][A-Z0-9]{1,7}$")]
    public string GoodsReceiptPrefix { get; set; } = "GRN";

    /// <summary>The prefix of a generated stock-take number — <c>STK-000017</c>.</summary>
    [RegularExpression("^[A-Z][A-Z0-9]{1,7}$")]
    public string StockTakePrefix { get; set; } = "STK";

    /// <summary>
    /// How long a checkout hold lasts before the sweeper takes it back, in minutes.
    /// </summary>
    /// <remarks>
    /// Fifteen minutes is long enough for a shopper to finish a UPI payment and short enough that an
    /// abandoned basket does not keep the last unit off sale for an afternoon. Callers may ask for
    /// less; they may not ask for more than <see cref="MaxReservationMinutes"/>.
    /// </remarks>
    [Range(1, 1440)]
    public int DefaultReservationMinutes { get; set; } = 15;

    /// <summary>The longest hold this deployment will grant, in minutes.</summary>
    [Range(1, 10_080)]
    public int MaxReservationMinutes { get; set; } = 120;

    /// <summary>Whether the worker in this host sweeps expired holds.</summary>
    /// <remarks>
    /// False in the API and true in the worker, exactly as the catalogue job runner and the
    /// notification dispatcher are configured. Every API replica running the same loop would
    /// multiply the polling and contend for the same rows for no gain.
    /// </remarks>
    public bool ReservationSweeperEnabled { get; set; }

    /// <summary>Seconds between sweeps of the expired-hold queue.</summary>
    [Range(5, 3600)]
    public int SweepIntervalSeconds { get; set; } = 60;

    /// <summary>How many expired holds one sweep takes at a time.</summary>
    [Range(1, 1000)]
    public int SweepBatchSize { get; set; } = 100;

    /// <summary>Whether the worker in this host reconciles the quantity caches against the ledger.</summary>
    /// <remarks>
    /// The nightly job docs/03-database-design.md §4.5 requires. It asserts rather than repairs: a
    /// cache that has drifted from the ledger is a defect, and silently correcting it would destroy
    /// the evidence of how it drifted.
    /// </remarks>
    public bool ReconciliationEnabled { get; set; }

    /// <summary>Hours between reconciliation passes.</summary>
    [Range(1, 168)]
    public int ReconciliationIntervalHours { get; set; } = 24;

    /// <summary>
    /// Whether opening a stock item is refused when the offer behind it is not in the catalogue.
    /// </summary>
    /// <remarks>
    /// On. A stock row keyed on a listing nobody can buy is a count that will never be reconciled
    /// against anything, and it is the shape a typo in an import file takes.
    /// </remarks>
    public bool RequireKnownListing { get; set; } = true;

    /// <summary>The most stock rows one stock take may put on a single sheet.</summary>
    [Range(1, 100_000)]
    public int MaxStockTakeLines { get; set; } = 5_000;
}
