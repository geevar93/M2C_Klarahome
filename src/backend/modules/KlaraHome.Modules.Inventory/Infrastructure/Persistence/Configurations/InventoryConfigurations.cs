using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KlaraHome.Modules.Inventory.Infrastructure.Persistence.Configurations;

/// <summary>The <c>CHECK</c> lists, written once so a column and its constraint cannot drift apart.</summary>
internal static class InventoryCheckConstraints
{
    /// <summary>The values <c>stock_items.tracking_mode</c> accepts.</summary>
    public const string TrackingModes = "tracking_mode IN ('None', 'Batch', 'Serial')";

    /// <summary>The values <c>stock_ledger_entries.reason</c> accepts.</summary>
    public const string MovementReasons =
        "reason IN ('Purchase', 'Sale', 'Reservation', 'Release', 'Return', 'Adjustment', 'Damage', "
        + "'TransferIn', 'TransferOut', 'Correction')";

    /// <summary>The values <c>stock_reservations.status</c> accepts.</summary>
    public const string ReservationStatuses = "status IN ('Held', 'Committed', 'Released', 'Expired')";

    /// <summary>The values <c>purchase_orders.status</c> accepts.</summary>
    public const string PurchaseOrderStatuses =
        "status IN ('Draft', 'Submitted', 'PartiallyReceived', 'Received', 'Cancelled')";

    /// <summary>The values <c>goods_receipts.status</c> accepts.</summary>
    public const string GoodsReceiptStatuses = "status IN ('Draft', 'Posted')";

    /// <summary>The values <c>stock_takes.status</c> accepts.</summary>
    public const string StockTakeStatuses = "status IN ('Draft', 'Counting', 'Submitted', 'Cancelled')";

    /// <summary>The values <c>stock_serials.status</c> accepts.</summary>
    public const string SerialStatuses =
        "status IN ('InStock', 'Reserved', 'Sold', 'Returned', 'Damaged')";

    /// <summary>The values <c>stock_reservations.reference_type</c> accepts.</summary>
    public const string ReservationReferenceTypes = "reference_type IN ('cart', 'order')";
}

/// <summary>Maps <see cref="Warehouse"/> to <c>inventory.warehouses</c>.</summary>
internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("warehouses", table =>
        {
            table.HasCheckConstraint(
                "ck_warehouses_priority",
                $"priority >= 0 AND priority <= {Warehouse.MaxPriority}");

            // Six digits, no leading zero. India's PIN codes are the reason shipping can price a
            // leg at all, and a malformed one fails at the aggregator rather than here.
            table.HasCheckConstraint("ck_warehouses_pincode", "pincode ~ '^[1-9][0-9]{5}$'");
        });

        builder.HasKey(warehouse => warehouse.Id);
        builder.Property(warehouse => warehouse.Id).ValueGeneratedNever();

        builder.Property(warehouse => warehouse.Code).HasMaxLength(32);
        builder.Property(warehouse => warehouse.Name).HasMaxLength(160);
        builder.Property(warehouse => warehouse.Pincode).HasMaxLength(6).IsFixedLength();

        builder.OwnsOne(warehouse => warehouse.Address, address => address.ToJson());

        // The code is what an operator quotes and what an import file keys on, so it has to be
        // unique. Hard-deleted rather than soft, so no filter is needed.
        builder.HasIndex(warehouse => new { warehouse.TenantId, warehouse.Code }).IsUnique();

        // The allocation walk: the active locations of one seller, cheapest first.
        builder.HasIndex(warehouse => new { warehouse.TenantId, warehouse.VendorId, warehouse.Priority });

        builder.Ignore(warehouse => warehouse.DomainEvents);
    }
}

/// <summary>Maps <see cref="StockItem"/> to <c>inventory.stock_items</c>.</summary>
internal sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_items", table =>
        {
            table.HasCheckConstraint(
                "ck_stock_items_tracking_mode",
                InventoryCheckConstraints.TrackingModes);

            // The invariants of docs/03-database-design.md §4.5, in the only place that cannot be
            // bypassed by a code path that forgot them.
            table.HasCheckConstraint("ck_stock_items_on_hand", "quantity_on_hand >= 0");
            table.HasCheckConstraint("ck_stock_items_reserved", "quantity_reserved >= 0");
            table.HasCheckConstraint("ck_stock_items_reorder_level", "reorder_level >= 0");
            table.HasCheckConstraint("ck_stock_items_reorder_quantity", "reorder_quantity >= 0");

            // Reserved may exceed on hand only where the seller has said they will backorder or
            // pre-order. Without the exception this constraint would make those two flags a lie;
            // without the constraint, an oversell would be a row nobody could see was wrong.
            table.HasCheckConstraint(
                "ck_stock_items_reserved_within_hand",
                "allow_backorder OR allow_preorder OR quantity_reserved <= quantity_on_hand");

            // A pre-order date belongs to an item that is on pre-order, and nowhere else.
            table.HasCheckConstraint(
                "ck_stock_items_preorder_date",
                "allow_preorder OR preorder_available_at IS NULL");
        });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();

        builder.Property(item => item.Sku).HasMaxLength(64);
        builder.Property(item => item.TrackingMode).HasConversion<string>().HasMaxLength(16);

        // One stock row per offer per location. This is the uniqueness the whole module rests on:
        // two rows for the same pair would split the count, and every availability answer would be
        // wrong by whichever half the query happened to find.
        builder
            .HasIndex(item => new { item.TenantId, item.ListingId, item.WarehouseId })
            .IsUnique();

        // "What is available for this offer" aggregates across warehouses, and it is the hottest
        // read in the module.
        builder.HasIndex(item => new { item.TenantId, item.ListingId });

        // The low-stock queue, and the stock screen's default filter.
        builder.HasIndex(item => new { item.TenantId, item.WarehouseId, item.Sku });

        builder.Ignore(item => item.DomainEvents);
        builder.Ignore(item => item.QuantityAvailable);
        builder.Ignore(item => item.IsAvailable);
        builder.Ignore(item => item.IsLow);
    }
}

/// <summary>
/// Maps <see cref="StockLedgerEntry"/> to <c>inventory.stock_ledger_entries</c>.
/// </summary>
/// <remarks>
/// The table is created by hand and excluded from migrations: it is
/// <c>PARTITION BY RANGE (occurred_at)</c> (docs/03-database-design.md §4.5), and a partitioned
/// table is created partitioned or not at all. The mapping still describes every column, because
/// this is what the ledger service and the admin surface query through.
/// </remarks>
internal sealed class StockLedgerEntryConfiguration : IEntityTypeConfiguration<StockLedgerEntry>
{
    public void Configure(EntityTypeBuilder<StockLedgerEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_ledger_entries", table => table.ExcludeFromMigrations());

        // Composite, and in this order: PostgreSQL requires the partition key to be part of every
        // unique constraint on a partitioned table.
        builder.HasKey(entry => new { entry.OccurredAt, entry.Id });

        builder.Property(entry => entry.Id).ValueGeneratedNever();

        builder.Property(entry => entry.Reason).HasConversion<string>().HasMaxLength(16);
        builder.Property(entry => entry.ReferenceType).HasMaxLength(32);
        builder.Property(entry => entry.Note).HasMaxLength(StockLedgerEntry.MaxNoteLength);

        builder.Ignore(entry => entry.DomainEvents);
    }
}

/// <summary>Maps <see cref="StockReservation"/> to <c>inventory.stock_reservations</c>.</summary>
internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_reservations", table =>
        {
            table.HasCheckConstraint("ck_stock_reservations_quantity", "quantity > 0");
            table.HasCheckConstraint(
                "ck_stock_reservations_status",
                InventoryCheckConstraints.ReservationStatuses);
            table.HasCheckConstraint(
                "ck_stock_reservations_reference_type",
                InventoryCheckConstraints.ReservationReferenceTypes);

            // A settled hold has a settlement time and a live one does not. Without this the two
            // states blur, and "how long do holds actually last" stops being answerable.
            table.HasCheckConstraint(
                "ck_stock_reservations_settled_at",
                "(status = 'Held') = (settled_at IS NULL)");
        });

        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.Id).ValueGeneratedNever();

        builder.Property(reservation => reservation.ReferenceType).HasMaxLength(32);
        builder.Property(reservation => reservation.Status).HasConversion<string>().HasMaxLength(16);

        // One live hold per cart line. This is what makes HoldAsync idempotent under a retry: the
        // second attempt collides rather than taking the stock twice. Filtered, because the same
        // line legitimately has many settled holds behind it.
        builder
            .HasIndex(reservation => new
            {
                reservation.TenantId,
                reservation.StockItemId,
                reservation.ReferenceType,
                reservation.ReferenceId,
                reservation.LineReferenceId,
            })
            .IsUnique()
            .HasFilter("status = 'Held'")
            .HasDatabaseName("ux_stock_reservations_live");

        // The settle path: every live hold of one cart or order.
        builder.HasIndex(reservation => new
        {
            reservation.TenantId,
            reservation.ReferenceType,
            reservation.ReferenceId,
            reservation.Status,
        });

        // The sweeper's claim query, and the only index it needs: the oldest lapsed holds first.
        builder
            .HasIndex(reservation => new { reservation.Status, reservation.ExpiresAt })
            .HasDatabaseName("ix_stock_reservations_due");

        builder.Ignore(reservation => reservation.DomainEvents);
        builder.Ignore(reservation => reservation.IsHeld);
    }
}

/// <summary>Maps <see cref="Supplier"/> to <c>inventory.suppliers</c>.</summary>
internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("suppliers", table => table.HasCheckConstraint(
            "ck_suppliers_payment_terms",
            $"payment_terms_days >= 0 AND payment_terms_days <= {Supplier.MaxPaymentTermsDays}"));

        builder.HasKey(supplier => supplier.Id);
        builder.Property(supplier => supplier.Id).ValueGeneratedNever();

        builder.Property(supplier => supplier.Code).HasMaxLength(32);
        builder.Property(supplier => supplier.Name).HasMaxLength(200);
        builder.Property(supplier => supplier.ContactName).HasMaxLength(160);
        builder.Property(supplier => supplier.Email).HasMaxLength(320);
        builder.Property(supplier => supplier.Phone).HasMaxLength(20);
        builder.Property(supplier => supplier.Gstin).HasMaxLength(15);

        builder.OwnsOne(supplier => supplier.Address, address => address.ToJson());

        builder.HasIndex(supplier => new { supplier.TenantId, supplier.Code }).IsUnique();

        builder.Ignore(supplier => supplier.DomainEvents);
    }
}

/// <summary>Maps <see cref="PurchaseOrder"/> to <c>inventory.purchase_orders</c>.</summary>
internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("purchase_orders", table => table.HasCheckConstraint(
            "ck_purchase_orders_status",
            InventoryCheckConstraints.PurchaseOrderStatuses));

        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();

        builder.Property(order => order.Number).HasMaxLength(32);
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(24);
        builder.Property(order => order.Notes).HasMaxLength(2000);

        builder.HasMoney(order => order.Subtotal);
        builder.HasMoney(order => order.TaxTotal);
        builder.HasMoney(order => order.Total);

        // The aggregate boundary: lines are loaded and saved with their document and never on
        // their own, which is what keeps the rolled-up totals honest.
        builder
            .HasMany(order => order.Lines)
            .WithOne()
            .HasForeignKey(line => line.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(PurchaseOrder.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(order => new { order.TenantId, order.Number }).IsUnique();

        // The buyer's queue: what is still outstanding, oldest first.
        builder.HasIndex(order => new { order.TenantId, order.Status, order.ExpectedAt });

        builder.HasIndex(order => new { order.TenantId, order.SupplierId });

        builder.Ignore(order => order.DomainEvents);
        builder.Ignore(order => order.IsEditable);
        builder.Ignore(order => order.IsReceivable);
    }
}

/// <summary>Maps <see cref="PurchaseOrderLine"/> to <c>inventory.purchase_order_lines</c>.</summary>
internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("purchase_order_lines", table =>
        {
            table.HasCheckConstraint("ck_purchase_order_lines_ordered", "quantity_ordered > 0");
            table.HasCheckConstraint(
                "ck_purchase_order_lines_received",
                "quantity_received >= 0 AND quantity_received <= quantity_ordered");
            table.HasCheckConstraint("ck_purchase_order_lines_tax_rate", "tax_rate >= 0 AND tax_rate <= 100");
        });

        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.Sku).HasMaxLength(64);
        builder.Property(line => line.Description).HasMaxLength(300);
        builder.Property(line => line.TaxRate).HasColumnType("numeric(7,4)");

        builder.HasMoney(line => line.UnitCost);
        builder.HasMoney(line => line.LineTotal);

        builder.HasIndex(line => new { line.TenantId, line.PurchaseOrderId });
        builder.HasIndex(line => new { line.TenantId, line.ListingId });

        builder.Ignore(line => line.DomainEvents);
        builder.Ignore(line => line.TaxAmount);
        builder.Ignore(line => line.IsFullyReceived);
        builder.Ignore(line => line.QuantityOutstanding);
    }
}

/// <summary>Maps <see cref="GoodsReceipt"/> to <c>inventory.goods_receipts</c>.</summary>
internal sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("goods_receipts", table => table.HasCheckConstraint(
            "ck_goods_receipts_status",
            InventoryCheckConstraints.GoodsReceiptStatuses));

        builder.HasKey(receipt => receipt.Id);
        builder.Property(receipt => receipt.Id).ValueGeneratedNever();

        builder.Property(receipt => receipt.Number).HasMaxLength(32);
        builder.Property(receipt => receipt.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(receipt => receipt.Notes).HasMaxLength(2000);

        builder
            .HasMany(receipt => receipt.Lines)
            .WithOne()
            .HasForeignKey(line => line.GoodsReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(GoodsReceipt.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(receipt => new { receipt.TenantId, receipt.Number }).IsUnique();
        builder.HasIndex(receipt => new { receipt.TenantId, receipt.PurchaseOrderId });

        builder.Ignore(receipt => receipt.DomainEvents);
    }
}

/// <summary>Maps <see cref="GoodsReceiptLine"/> to <c>inventory.goods_receipt_lines</c>.</summary>
internal sealed class GoodsReceiptLineConfiguration : IEntityTypeConfiguration<GoodsReceiptLine>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("goods_receipt_lines", table =>
        {
            table.HasCheckConstraint("ck_goods_receipt_lines_accepted", "quantity_accepted >= 0");
            table.HasCheckConstraint("ck_goods_receipt_lines_rejected", "quantity_rejected >= 0");

            // A refusal has to say why. A rejected quantity with no reason is a number nobody can
            // take back to the supplier.
            table.HasCheckConstraint(
                "ck_goods_receipt_lines_rejection_reason",
                "quantity_rejected = 0 OR rejection_reason IS NOT NULL");
        });

        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.RejectionReason).HasMaxLength(500);
        builder.Property(line => line.BatchCode).HasMaxLength(64);

        builder.HasIndex(line => new { line.TenantId, line.GoodsReceiptId });
        builder.HasIndex(line => new { line.TenantId, line.PurchaseOrderLineId });

        builder.Ignore(line => line.DomainEvents);
    }
}

/// <summary>Maps <see cref="StockTake"/> to <c>inventory.stock_takes</c>.</summary>
internal sealed class StockTakeConfiguration : IEntityTypeConfiguration<StockTake>
{
    public void Configure(EntityTypeBuilder<StockTake> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_takes", table => table.HasCheckConstraint(
            "ck_stock_takes_status",
            InventoryCheckConstraints.StockTakeStatuses));

        builder.HasKey(take => take.Id);
        builder.Property(take => take.Id).ValueGeneratedNever();

        builder.Property(take => take.Number).HasMaxLength(32);
        builder.Property(take => take.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(take => take.Notes).HasMaxLength(2000);

        builder
            .HasMany(take => take.Lines)
            .WithOne()
            .HasForeignKey(line => line.StockTakeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(StockTake.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(take => new { take.TenantId, take.Number }).IsUnique();
        builder.HasIndex(take => new { take.TenantId, take.WarehouseId, take.Status });

        builder.Ignore(take => take.DomainEvents);
        builder.Ignore(take => take.IsOpen);
    }
}

/// <summary>Maps <see cref="StockTakeLine"/> to <c>inventory.stock_take_lines</c>.</summary>
internal sealed class StockTakeLineConfiguration : IEntityTypeConfiguration<StockTakeLine>
{
    public void Configure(EntityTypeBuilder<StockTakeLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_take_lines", table =>
        {
            table.HasCheckConstraint("ck_stock_take_lines_expected", "expected_quantity >= 0");
            table.HasCheckConstraint(
                "ck_stock_take_lines_counted",
                "counted_quantity IS NULL OR counted_quantity >= 0");
        });

        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.Sku).HasMaxLength(64);
        builder.Property(line => line.Note).HasMaxLength(500);

        // One row per stock item per sheet. Counting the same shelf twice on one sheet produces
        // two variances against one book figure, and the second correction would undo the first.
        builder
            .HasIndex(line => new { line.TenantId, line.StockTakeId, line.StockItemId })
            .IsUnique();

        builder.Ignore(line => line.DomainEvents);
        builder.Ignore(line => line.Variance);
    }
}

/// <summary>Maps <see cref="StockBatch"/> to <c>inventory.stock_batches</c>.</summary>
internal sealed class StockBatchConfiguration : IEntityTypeConfiguration<StockBatch>
{
    public void Configure(EntityTypeBuilder<StockBatch> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_batches", table =>
        {
            table.HasCheckConstraint("ck_stock_batches_quantity", "quantity >= 0");
            table.HasCheckConstraint(
                "ck_stock_batches_dates",
                "manufactured_on IS NULL OR expires_on IS NULL OR expires_on >= manufactured_on");
        });

        builder.HasKey(batch => batch.Id);
        builder.Property(batch => batch.Id).ValueGeneratedNever();

        builder.Property(batch => batch.BatchCode).HasMaxLength(64);

        builder
            .HasIndex(batch => new { batch.TenantId, batch.StockItemId, batch.BatchCode })
            .IsUnique();

        // The expiry sweep a pharmacy-style category needs: what goes out of date next.
        builder.HasIndex(batch => new { batch.TenantId, batch.ExpiresOn });

        builder.Ignore(batch => batch.DomainEvents);
    }
}

/// <summary>Maps <see cref="StockSerial"/> to <c>inventory.stock_serials</c>.</summary>
internal sealed class StockSerialConfiguration : IEntityTypeConfiguration<StockSerial>
{
    public void Configure(EntityTypeBuilder<StockSerial> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_serials", table => table.HasCheckConstraint(
            "ck_stock_serials_status",
            InventoryCheckConstraints.SerialStatuses));

        builder.HasKey(serial => serial.Id);
        builder.Property(serial => serial.Id).ValueGeneratedNever();

        builder.Property(serial => serial.SerialNumber).HasMaxLength(96);
        builder.Property(serial => serial.Status).HasConversion<string>().HasMaxLength(16);

        builder
            .HasIndex(serial => new { serial.TenantId, serial.StockItemId, serial.SerialNumber })
            .IsUnique();

        // "Which unit went out on that order" — the question serial tracking exists to answer.
        builder.HasIndex(serial => new { serial.TenantId, serial.ReferenceId });

        builder.Ignore(serial => serial.DomainEvents);
    }
}
