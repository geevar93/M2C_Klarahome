using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Inventory.Domain;
using KlaraHome.Modules.Inventory.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// The Inventory module's data access.
/// </summary>
/// <remarks>
/// <para>
/// Nearly every table here is <see cref="KlaraHome.SharedKernel.Domain.IVendorScoped"/> with a
/// nullable seller, and the nullability is load-bearing: a warehouse, a supplier or a stock row may
/// belong to a seller or to the platform, and the global vendor filter then shows a vendor caller
/// their own rows and the platform's shared ones.
/// </para>
/// <para>
/// The line tables — purchase-order lines, receipt lines, take lines — are deliberately <em>not</em>
/// vendor-scoped. They are reached only through their document, which is, so scoping them again
/// would add a column and an index to enforce something already enforced one level up.
/// </para>
/// </remarks>
/// <param name="options">Provider options supplied by DI or by the design-time factory.</param>
/// <param name="tenantContext">The ambient tenant.</param>
/// <param name="callerContext">The current caller, read by the vendor query filter.</param>
internal sealed class InventoryDbContext(
    DbContextOptions<InventoryDbContext> options,
    ITenantContext tenantContext,
    ICallerContext? callerContext = null)
    : KlaraHomeDbContext(options, tenantContext, callerContext)
{
    /// <inheritdoc />
    public override string Schema => InventoryModule.SchemaName;

    /// <summary>Places stock is held.</summary>
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    /// <summary>The stock of one offer at one location. The aggregate root of this module.</summary>
    public DbSet<StockItem> StockItems => Set<StockItem>();

    /// <summary>Every movement, ever. The record of truth behind both quantity caches.</summary>
    public DbSet<StockLedgerEntry> LedgerEntries => Set<StockLedgerEntry>();

    /// <summary>Time-limited holds taken by carts and orders.</summary>
    public DbSet<StockReservation> Reservations => Set<StockReservation>();

    /// <summary>Who stock is bought from.</summary>
    public DbSet<Supplier> Suppliers => Set<Supplier>();

    /// <summary>Orders placed on suppliers.</summary>
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

    /// <summary>Their lines.</summary>
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();

    /// <summary>What actually turned up.</summary>
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();

    /// <summary>Its lines.</summary>
    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();

    /// <summary>Physical recounts.</summary>
    public DbSet<StockTake> StockTakes => Set<StockTake>();

    /// <summary>Their counted rows.</summary>
    public DbSet<StockTakeLine> StockTakeLines => Set<StockTakeLine>();

    /// <summary>Lots, for a batch-tracked item.</summary>
    public DbSet<StockBatch> Batches => Set<StockBatch>();

    /// <summary>Individually identified units, for a serial-tracked item.</summary>
    public DbSet<StockSerial> Serials => Set<StockSerial>();

    /// <summary>
    /// The sequence behind a purchase-order number.
    /// </summary>
    /// <remarks>
    /// A database sequence rather than a count of rows, for the same reason the vendor code and the
    /// SKU use one: a count is a race. The gap a rolled-back transaction leaves costs nothing —
    /// this is a document reference, not a statutory invoice number.
    /// </remarks>
    public const string PurchaseOrderSequenceName = "purchase_order_seq";

    /// <summary>The sequence behind a goods-receipt number.</summary>
    public const string GoodsReceiptSequenceName = "goods_receipt_seq";

    /// <summary>The sequence behind a stock-take number.</summary>
    public const string StockTakeSequenceName = "stock_take_seq";

    /// <inheritdoc />
    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasSequence<long>(PurchaseOrderSequenceName, InventoryModule.SchemaName)
            .StartsAt(1)
            .IncrementsBy(1);

        modelBuilder.HasSequence<long>(GoodsReceiptSequenceName, InventoryModule.SchemaName)
            .StartsAt(1)
            .IncrementsBy(1);

        modelBuilder.HasSequence<long>(StockTakeSequenceName, InventoryModule.SchemaName)
            .StartsAt(1)
            .IncrementsBy(1);

        modelBuilder.ApplyConfiguration(new WarehouseConfiguration());
        modelBuilder.ApplyConfiguration(new StockItemConfiguration());
        modelBuilder.ApplyConfiguration(new StockLedgerEntryConfiguration());
        modelBuilder.ApplyConfiguration(new StockReservationConfiguration());
        modelBuilder.ApplyConfiguration(new SupplierConfiguration());
        modelBuilder.ApplyConfiguration(new PurchaseOrderConfiguration());
        modelBuilder.ApplyConfiguration(new PurchaseOrderLineConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptLineConfiguration());
        modelBuilder.ApplyConfiguration(new StockTakeConfiguration());
        modelBuilder.ApplyConfiguration(new StockTakeLineConfiguration());
        modelBuilder.ApplyConfiguration(new StockBatchConfiguration());
        modelBuilder.ApplyConfiguration(new StockSerialConfiguration());
    }
}
