using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialOrdersSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "orders");

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoice_number = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    series = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    financial_year = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    place_of_supply_state_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    is_intra_state = table.Column<bool>(type: "boolean", nullable: false),
                    taxable_value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    cgst = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    sgst = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    igst = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    cess = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    irn = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    qr_payload = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.CheckConstraint("ck_invoices_amounts", "taxable_value >= 0 AND cgst >= 0 AND sgst >= 0 AND igst >= 0 AND cess >= 0 AND total >= 0");
                    table.CheckConstraint("ck_invoices_status", "status IN ('Issued', 'Cancelled')");
                    table.CheckConstraint("ck_invoices_tax_split", "(igst = 0) OR (cgst = 0 AND sgst = 0)");
                    table.CheckConstraint("ck_invoices_vendor", "vendor_id IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "number_sequences",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scope_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    financial_year = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    next_value = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_sequences", x => x.id);
                    table.CheckConstraint("ck_number_sequences_kind", "kind IN ('order', 'invoice')");
                    table.CheckConstraint("ck_number_sequences_next", "next_value >= 1");
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cart_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checkout_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    shipping_address = table.Column<string>(type: "jsonb", nullable: false),
                    billing_address = table.Column<string>(type: "jsonb", nullable: false),
                    place_of_supply_state_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    payment_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    items_total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    discount_total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    shipping_total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tax_total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    cod_fee = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    wallet_applied = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    rounding_adjustment = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    grand_total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    amount_payable = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    coupon_code = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    placed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.CheckConstraint("ck_orders_channel", "channel IN ('web', 'app', 'admin')");
                    table.CheckConstraint("ck_orders_payment_method", "payment_method IN ('Prepaid', 'CashOnDelivery')");
                    table.CheckConstraint("ck_orders_payment_status", "payment_status IN ('Pending', 'Authorized', 'Paid', 'Failed', 'PartiallyRefunded', 'Refunded')");
                    table.CheckConstraint("ck_orders_status", "status IN ('PendingPayment', 'InProgress', 'Completed', 'Cancelled')");
                    table.CheckConstraint("ck_orders_totals", "items_total >= 0 AND discount_total >= 0 AND shipping_total >= 0 AND tax_total >= 0 AND cod_fee >= 0 AND wallet_applied >= 0 AND grand_total >= 0 AND amount_payable >= 0");
                });

            migrationBuilder.CreateTable(
                name: "order_events",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    from_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    to_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    actor_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: true),
                    is_customer_visible = table.Column<bool>(type: "boolean", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_events", x => x.id);
                    table.CheckConstraint("ck_order_events_type", "type IN ('placed', 'status-changed', 'cancelled', 'invoiced', 'payment', 'note')");
                    table.ForeignKey(
                        name: "fk_order_events_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sub_orders",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vendor_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    vendor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    vendor_gstin = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    sub_order_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    items_total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    discount_total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    shipping_total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    shipping_tax = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tax_total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    taxable_value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    shipping_option_code = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    carrier = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    promised_min_days = table.Column<int>(type: "integer", nullable: false),
                    promised_max_days = table.Column<int>(type: "integer", nullable: false),
                    dispatch_due_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    return_window_ends_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    cancelled_by = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    dispatch_sla_hours = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sub_orders", x => x.id);
                    table.CheckConstraint("ck_sub_orders_cancelled_by", "cancelled_by IS NULL OR cancelled_by IN ('Customer', 'Vendor', 'Platform', 'System')");
                    table.CheckConstraint("ck_sub_orders_promise", "promised_min_days >= 0 AND promised_max_days >= promised_min_days AND dispatch_sla_hours >= 0");
                    table.CheckConstraint("ck_sub_orders_status", "status IN ('PendingPayment', 'PaymentFailed', 'Confirmed', 'Processing', 'Packed', 'Shipped', 'OutForDelivery', 'DeliveryFailed', 'RtoInitiated', 'RtoDelivered', 'Delivered', 'ReturnRequested', 'ReturnInProgress', 'Returned', 'Completed', 'Cancelled')");
                    table.CheckConstraint("ck_sub_orders_totals", "items_total >= 0 AND discount_total >= 0 AND shipping_total >= 0 AND tax_total >= 0 AND taxable_value >= 0 AND total >= 0");
                    table.CheckConstraint("ck_sub_orders_vendor", "vendor_id IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_sub_orders_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_lines",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_mrp = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    taxable_value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    gst_rate = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    cgst = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    sgst = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    igst = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    cess = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    commission_rate = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    commission_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    commission_plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    quantity_cancelled = table.Column<int>(type: "integer", nullable: false),
                    quantity_returned = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_lines", x => x.id);
                    table.CheckConstraint("ck_order_lines_money", "unit_mrp >= 0 AND unit_price >= 0 AND discount_amount >= 0 AND taxable_value >= 0 AND line_total >= 0 AND commission_amount >= 0");
                    table.CheckConstraint("ck_order_lines_quantities", "quantity >= 1 AND quantity_cancelled >= 0 AND quantity_returned >= 0 AND quantity_cancelled <= quantity AND quantity_returned <= quantity");
                    table.CheckConstraint("ck_order_lines_status", "status IN ('Active', 'PartiallyCancelled', 'Cancelled', 'PartiallyReturned', 'Returned')");
                    table.CheckConstraint("ck_order_lines_vendor", "vendor_id IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_order_lines_sub_orders_sub_order_id",
                        column: x => x.sub_order_id,
                        principalSchema: "orders",
                        principalTable: "sub_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id",
                schema: "orders",
                table: "invoices",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id_order_id",
                schema: "orders",
                table: "invoices",
                columns: new[] { "tenant_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id_sub_order_id",
                schema: "orders",
                table: "invoices",
                columns: new[] { "tenant_id", "sub_order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id_vendor_id_financial_year_invoice_number",
                schema: "orders",
                table: "invoices",
                columns: new[] { "tenant_id", "vendor_id", "financial_year", "invoice_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id_vendor_id_issued_at",
                schema: "orders",
                table: "invoices",
                columns: new[] { "tenant_id", "vendor_id", "issued_at" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_vendor_id",
                schema: "orders",
                table: "invoices",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_number_sequences_tenant_id",
                schema: "orders",
                table: "number_sequences",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_number_sequences_tenant_id_kind_scope_key_financial_year",
                schema: "orders",
                table: "number_sequences",
                columns: new[] { "tenant_id", "kind", "scope_key", "financial_year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_events_order_id",
                schema: "orders",
                table: "order_events",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_events_tenant_id",
                schema: "orders",
                table: "order_events",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_events_tenant_id_order_id_occurred_at",
                schema: "orders",
                table: "order_events",
                columns: new[] { "tenant_id", "order_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_order_events_tenant_id_sub_order_id_occurred_at",
                schema: "orders",
                table: "order_events",
                columns: new[] { "tenant_id", "sub_order_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_sub_order_id",
                schema: "orders",
                table: "order_lines",
                column: "sub_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_tenant_id",
                schema: "orders",
                table: "order_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_tenant_id_listing_id",
                schema: "orders",
                table: "order_lines",
                columns: new[] { "tenant_id", "listing_id" });

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_vendor_id",
                schema: "orders",
                table: "order_lines",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id",
                schema: "orders",
                table: "orders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_cart_id",
                schema: "orders",
                table: "orders",
                columns: new[] { "tenant_id", "cart_id" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_customer_id_placed_at",
                schema: "orders",
                table: "orders",
                columns: new[] { "tenant_id", "customer_id", "placed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_order_number",
                schema: "orders",
                table: "orders",
                columns: new[] { "tenant_id", "order_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_status_placed_at",
                schema: "orders",
                table: "orders",
                columns: new[] { "tenant_id", "status", "placed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sub_orders_order_id",
                schema: "orders",
                table: "sub_orders",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_sub_orders_tenant_id",
                schema: "orders",
                table: "sub_orders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sub_orders_tenant_id_order_id_vendor_id",
                schema: "orders",
                table: "sub_orders",
                columns: new[] { "tenant_id", "order_id", "vendor_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sub_orders_tenant_id_status_dispatch_due_at",
                schema: "orders",
                table: "sub_orders",
                columns: new[] { "tenant_id", "status", "dispatch_due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sub_orders_tenant_id_status_return_window_ends_at",
                schema: "orders",
                table: "sub_orders",
                columns: new[] { "tenant_id", "status", "return_window_ends_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sub_orders_tenant_id_sub_order_number",
                schema: "orders",
                table: "sub_orders",
                columns: new[] { "tenant_id", "sub_order_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sub_orders_tenant_id_vendor_id_status_created_at",
                schema: "orders",
                table: "sub_orders",
                columns: new[] { "tenant_id", "vendor_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sub_orders_vendor_id",
                schema: "orders",
                table: "sub_orders",
                column: "vendor_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoices",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "number_sequences",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_events",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_lines",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "sub_orders",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "orders");
        }
    }
}
