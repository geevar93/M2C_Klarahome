using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialInventorySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateSequence(
                name: "goods_receipt_seq",
                schema: "inventory");

            migrationBuilder.CreateSequence(
                name: "purchase_order_seq",
                schema: "inventory");

            migrationBuilder.CreateSequence(
                name: "stock_take_seq",
                schema: "inventory");

            migrationBuilder.CreateTable(
                name: "goods_receipts",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    received_by = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_receipts", x => x.id);
                    table.CheckConstraint("ck_goods_receipts_status", "status IN ('Draft', 'Posted')");
                });

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    expected_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    subtotal_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    subtotal_currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    tax_total_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tax_total_currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    total_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total_currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_orders", x => x.id);
                    table.CheckConstraint("ck_purchase_orders_status", "status IN ('Draft', 'Submitted', 'PartiallyReceived', 'Received', 'Cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "stock_batches",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    manufactured_on = table.Column<DateOnly>(type: "date", nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_batches", x => x.id);
                    table.CheckConstraint("ck_stock_batches_dates", "manufactured_on IS NULL OR expires_on IS NULL OR expires_on >= manufactured_on");
                    table.CheckConstraint("ck_stock_batches_quantity", "quantity >= 0");
                });

            migrationBuilder.CreateTable(
                name: "stock_items",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quantity_on_hand = table.Column<int>(type: "integer", nullable: false),
                    quantity_reserved = table.Column<int>(type: "integer", nullable: false),
                    reorder_level = table.Column<int>(type: "integer", nullable: false),
                    reorder_quantity = table.Column<int>(type: "integer", nullable: false),
                    allow_backorder = table.Column<bool>(type: "boolean", nullable: false),
                    allow_preorder = table.Column<bool>(type: "boolean", nullable: false),
                    preorder_available_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    tracking_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    low_stock_notified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_items", x => x.id);
                    table.CheckConstraint("ck_stock_items_on_hand", "quantity_on_hand >= 0");
                    table.CheckConstraint("ck_stock_items_preorder_date", "allow_preorder OR preorder_available_at IS NULL");
                    table.CheckConstraint("ck_stock_items_reorder_level", "reorder_level >= 0");
                    table.CheckConstraint("ck_stock_items_reorder_quantity", "reorder_quantity >= 0");
                    table.CheckConstraint("ck_stock_items_reserved", "quantity_reserved >= 0");
                    table.CheckConstraint("ck_stock_items_reserved_within_hand", "allow_backorder OR allow_preorder OR quantity_reserved <= quantity_on_hand");
                    table.CheckConstraint("ck_stock_items_tracking_mode", "tracking_mode IN ('None', 'Batch', 'Serial')");
                });

            migrationBuilder.CreateTable(
                name: "stock_reservations",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    reference_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_reservations", x => x.id);
                    table.CheckConstraint("ck_stock_reservations_quantity", "quantity > 0");
                    table.CheckConstraint("ck_stock_reservations_reference_type", "reference_type IN ('cart', 'order')");
                    table.CheckConstraint("ck_stock_reservations_settled_at", "(status = 'Held') = (settled_at IS NULL)");
                    table.CheckConstraint("ck_stock_reservations_status", "status IN ('Held', 'Committed', 'Released', 'Expired')");
                });

            migrationBuilder.CreateTable(
                name: "stock_serials",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_number = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_serials", x => x.id);
                    table.CheckConstraint("ck_stock_serials_status", "status IN ('InStock', 'Reserved', 'Sold', 'Returned', 'Damaged')");
                });

            migrationBuilder.CreateTable(
                name: "stock_takes",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scheduled_for = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_takes", x => x.id);
                    table.CheckConstraint("ck_stock_takes_status", "status IN ('Draft', 'Counting', 'Submitted', 'Cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    gstin = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    payment_terms_days = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    address = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.id);
                    table.CheckConstraint("ck_suppliers_payment_terms", "payment_terms_days >= 0 AND payment_terms_days <= 365");
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    pincode = table.Column<string>(type: "character(6)", fixedLength: true, maxLength: 6, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    address = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouses", x => x.id);
                    table.CheckConstraint("ck_warehouses_pincode", "pincode ~ '^[1-9][0-9]{5}$'");
                    table.CheckConstraint("ck_warehouses_priority", "priority >= 0 AND priority <= 1000");
                });

            migrationBuilder.CreateTable(
                name: "goods_receipt_lines",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    goods_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_accepted = table.Column<int>(type: "integer", nullable: false),
                    quantity_rejected = table.Column<int>(type: "integer", nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    batch_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_receipt_lines", x => x.id);
                    table.CheckConstraint("ck_goods_receipt_lines_accepted", "quantity_accepted >= 0");
                    table.CheckConstraint("ck_goods_receipt_lines_rejected", "quantity_rejected >= 0");
                    table.CheckConstraint("ck_goods_receipt_lines_rejection_reason", "quantity_rejected = 0 OR rejection_reason IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_goods_receipt_lines_goods_receipts_goods_receipt_id",
                        column: x => x.goods_receipt_id,
                        principalSchema: "inventory",
                        principalTable: "goods_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    quantity_ordered = table.Column<int>(type: "integer", nullable: false),
                    quantity_received = table.Column<int>(type: "integer", nullable: false),
                    tax_rate = table.Column<decimal>(type: "numeric(7,4)", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    line_total_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    line_total_currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    unit_cost_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit_cost_currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_lines", x => x.id);
                    table.CheckConstraint("ck_purchase_order_lines_ordered", "quantity_ordered > 0");
                    table.CheckConstraint("ck_purchase_order_lines_received", "quantity_received >= 0 AND quantity_received <= quantity_ordered");
                    table.CheckConstraint("ck_purchase_order_lines_tax_rate", "tax_rate >= 0 AND tax_rate <= 100");
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalSchema: "inventory",
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_take_lines",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_take_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expected_quantity = table.Column<int>(type: "integer", nullable: false),
                    counted_quantity = table.Column<int>(type: "integer", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_take_lines", x => x.id);
                    table.CheckConstraint("ck_stock_take_lines_counted", "counted_quantity IS NULL OR counted_quantity >= 0");
                    table.CheckConstraint("ck_stock_take_lines_expected", "expected_quantity >= 0");
                    table.ForeignKey(
                        name: "fk_stock_take_lines_stock_takes_stock_take_id",
                        column: x => x.stock_take_id,
                        principalSchema: "inventory",
                        principalTable: "stock_takes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_goods_receipt_id",
                schema: "inventory",
                table: "goods_receipt_lines",
                column: "goods_receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_tenant_id",
                schema: "inventory",
                table: "goods_receipt_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_tenant_id_goods_receipt_id",
                schema: "inventory",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "goods_receipt_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_tenant_id_purchase_order_line_id",
                schema: "inventory",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "purchase_order_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_tenant_id",
                schema: "inventory",
                table: "goods_receipts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_tenant_id_number",
                schema: "inventory",
                table: "goods_receipts",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_tenant_id_purchase_order_id",
                schema: "inventory",
                table: "goods_receipts",
                columns: new[] { "tenant_id", "purchase_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_vendor_id",
                schema: "inventory",
                table: "goods_receipts",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_purchase_order_id",
                schema: "inventory",
                table: "purchase_order_lines",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_tenant_id",
                schema: "inventory",
                table: "purchase_order_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_tenant_id_listing_id",
                schema: "inventory",
                table: "purchase_order_lines",
                columns: new[] { "tenant_id", "listing_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_tenant_id_purchase_order_id",
                schema: "inventory",
                table: "purchase_order_lines",
                columns: new[] { "tenant_id", "purchase_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_tenant_id",
                schema: "inventory",
                table: "purchase_orders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_tenant_id_number",
                schema: "inventory",
                table: "purchase_orders",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_tenant_id_status_expected_at",
                schema: "inventory",
                table: "purchase_orders",
                columns: new[] { "tenant_id", "status", "expected_at" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_tenant_id_supplier_id",
                schema: "inventory",
                table: "purchase_orders",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_vendor_id",
                schema: "inventory",
                table: "purchase_orders",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_batches_tenant_id",
                schema: "inventory",
                table: "stock_batches",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_batches_tenant_id_expires_on",
                schema: "inventory",
                table: "stock_batches",
                columns: new[] { "tenant_id", "expires_on" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_batches_tenant_id_stock_item_id_batch_code",
                schema: "inventory",
                table: "stock_batches",
                columns: new[] { "tenant_id", "stock_item_id", "batch_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_items_tenant_id",
                schema: "inventory",
                table: "stock_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_items_tenant_id_listing_id",
                schema: "inventory",
                table: "stock_items",
                columns: new[] { "tenant_id", "listing_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_items_tenant_id_listing_id_warehouse_id",
                schema: "inventory",
                table: "stock_items",
                columns: new[] { "tenant_id", "listing_id", "warehouse_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_items_tenant_id_warehouse_id_sku",
                schema: "inventory",
                table: "stock_items",
                columns: new[] { "tenant_id", "warehouse_id", "sku" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_items_vendor_id",
                schema: "inventory",
                table: "stock_items",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_due",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_tenant_id",
                schema: "inventory",
                table: "stock_reservations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_tenant_id_reference_type_reference_id_st",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "reference_type", "reference_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_reservations_live",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "stock_item_id", "reference_type", "reference_id", "line_reference_id" },
                unique: true,
                filter: "status = 'Held'");

            migrationBuilder.CreateIndex(
                name: "ix_stock_serials_tenant_id",
                schema: "inventory",
                table: "stock_serials",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_serials_tenant_id_reference_id",
                schema: "inventory",
                table: "stock_serials",
                columns: new[] { "tenant_id", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_serials_tenant_id_stock_item_id_serial_number",
                schema: "inventory",
                table: "stock_serials",
                columns: new[] { "tenant_id", "stock_item_id", "serial_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_take_lines_stock_take_id",
                schema: "inventory",
                table: "stock_take_lines",
                column: "stock_take_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_take_lines_tenant_id",
                schema: "inventory",
                table: "stock_take_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_take_lines_tenant_id_stock_take_id_stock_item_id",
                schema: "inventory",
                table: "stock_take_lines",
                columns: new[] { "tenant_id", "stock_take_id", "stock_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_takes_tenant_id",
                schema: "inventory",
                table: "stock_takes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_takes_tenant_id_number",
                schema: "inventory",
                table: "stock_takes",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_takes_tenant_id_warehouse_id_status",
                schema: "inventory",
                table: "stock_takes",
                columns: new[] { "tenant_id", "warehouse_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_takes_vendor_id",
                schema: "inventory",
                table: "stock_takes",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_tenant_id",
                schema: "inventory",
                table: "suppliers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_tenant_id_code",
                schema: "inventory",
                table: "suppliers",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_vendor_id",
                schema: "inventory",
                table: "suppliers",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_tenant_id",
                schema: "inventory",
                table: "warehouses",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_tenant_id_code",
                schema: "inventory",
                table: "warehouses",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_tenant_id_vendor_id_priority",
                schema: "inventory",
                table: "warehouses",
                columns: new[] { "tenant_id", "vendor_id", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_vendor_id",
                schema: "inventory",
                table: "warehouses",
                column: "vendor_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "goods_receipt_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "purchase_order_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_batches",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_items",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_reservations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_serials",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_take_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "suppliers",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "warehouses",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "goods_receipts",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "purchase_orders",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_takes",
                schema: "inventory");

            migrationBuilder.DropSequence(
                name: "goods_receipt_seq",
                schema: "inventory");

            migrationBuilder.DropSequence(
                name: "purchase_order_seq",
                schema: "inventory");

            migrationBuilder.DropSequence(
                name: "stock_take_seq",
                schema: "inventory");
        }
    }
}
