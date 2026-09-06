using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Carts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCartsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "carts");

            migrationBuilder.CreateTable(
                name: "carts",
                schema: "carts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    anonymous_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    coupon_code = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    line_count = table.Column<int>(type: "integer", nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    converted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    converted_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    abandoned_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    reminder_count = table.Column<int>(type: "integer", nullable: false),
                    last_reminder_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carts", x => x.id);
                    table.CheckConstraint("ck_carts_line_count", "line_count >= 0");
                    table.CheckConstraint("ck_carts_owner", "customer_id IS NOT NULL OR anonymous_token_hash IS NOT NULL");
                    table.CheckConstraint("ck_carts_reminder_count", "reminder_count >= 0");
                    table.CheckConstraint("ck_carts_status", "status IN ('Active', 'Converted', 'Abandoned', 'Expired')");
                });

            migrationBuilder.CreateTable(
                name: "checkout_sessions",
                schema: "carts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cart_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    shipping_address = table.Column<string>(type: "jsonb", nullable: true),
                    billing_address = table.Column<string>(type: "jsonb", nullable: true),
                    place_of_supply_state_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gstin = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    payment_method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quote_snapshot = table.Column<string>(type: "jsonb", nullable: true),
                    shipping_total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    grand_total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    placed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checkout_sessions", x => x.id);
                    table.CheckConstraint("ck_checkout_sessions_payment", "payment_method IN ('Prepaid', 'CashOnDelivery')");
                    table.CheckConstraint("ck_checkout_sessions_status", "status IN ('Draft', 'AddressSet', 'ShippingSet', 'PaymentSet', 'Placing', 'Placed', 'Abandoned', 'Expired')");
                    table.CheckConstraint("ck_checkout_sessions_totals", "shipping_total >= 0 AND grand_total >= 0");
                });

            migrationBuilder.CreateTable(
                name: "cart_lines",
                schema: "carts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cart_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    saved_for_later = table.Column<bool>(type: "boolean", nullable: false),
                    unit_price_at_add = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    priced_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cart_lines", x => x.id);
                    table.CheckConstraint("ck_cart_lines_quantity", "quantity >= 1");
                    table.CheckConstraint("ck_cart_lines_unit_price", "unit_price_at_add >= 0");
                    table.ForeignKey(
                        name: "fk_cart_lines_carts_cart_id",
                        column: x => x.cart_id,
                        principalSchema: "carts",
                        principalTable: "carts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "checkout_placements",
                schema: "carts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    checkout_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    response = table.Column<string>(type: "jsonb", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checkout_placements", x => x.id);
                    table.CheckConstraint("ck_checkout_placements_status", "status IN ('InProgress', 'Succeeded', 'Failed')");
                    table.ForeignKey(
                        name: "fk_checkout_placements_checkout_sessions_checkout_session_id",
                        column: x => x.checkout_session_id,
                        principalSchema: "carts",
                        principalTable: "checkout_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "checkout_shipments",
                schema: "carts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    checkout_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    option_code = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    service_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    carrier = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    dispatch_sla_hours = table.Column<int>(type: "integer", nullable: false),
                    promised_min_days = table.Column<int>(type: "integer", nullable: false),
                    promised_max_days = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checkout_shipments", x => x.id);
                    table.CheckConstraint("ck_checkout_shipments_amount", "amount >= 0 AND tax_amount >= 0");
                    table.CheckConstraint("ck_checkout_shipments_promise", "promised_min_days >= 0 AND promised_max_days >= promised_min_days");
                    table.ForeignKey(
                        name: "fk_checkout_shipments_checkout_sessions_checkout_session_id",
                        column: x => x.checkout_session_id,
                        principalSchema: "carts",
                        principalTable: "checkout_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cart_lines_cart_id",
                schema: "carts",
                table: "cart_lines",
                column: "cart_id");

            migrationBuilder.CreateIndex(
                name: "ix_cart_lines_tenant_id",
                schema: "carts",
                table: "cart_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_cart_lines_tenant_id_cart_id_listing_id",
                schema: "carts",
                table: "cart_lines",
                columns: new[] { "tenant_id", "cart_id", "listing_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cart_lines_tenant_id_vendor_id",
                schema: "carts",
                table: "cart_lines",
                columns: new[] { "tenant_id", "vendor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_carts_tenant_id",
                schema: "carts",
                table: "carts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_carts_tenant_id_anonymous_token_hash",
                schema: "carts",
                table: "carts",
                columns: new[] { "tenant_id", "anonymous_token_hash" },
                unique: true,
                filter: "anonymous_token_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_carts_tenant_id_customer_id",
                schema: "carts",
                table: "carts",
                columns: new[] { "tenant_id", "customer_id" },
                unique: true,
                filter: "customer_id IS NOT NULL AND status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_carts_tenant_id_status_abandoned_at",
                schema: "carts",
                table: "carts",
                columns: new[] { "tenant_id", "status", "abandoned_at" });

            migrationBuilder.CreateIndex(
                name: "ix_carts_tenant_id_status_last_activity_at",
                schema: "carts",
                table: "carts",
                columns: new[] { "tenant_id", "status", "last_activity_at" });

            migrationBuilder.CreateIndex(
                name: "ix_checkout_placements_checkout_session_id",
                schema: "carts",
                table: "checkout_placements",
                column: "checkout_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_checkout_placements_tenant_id",
                schema: "carts",
                table: "checkout_placements",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_checkout_placements_tenant_id_idempotency_key",
                schema: "carts",
                table: "checkout_placements",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_checkout_sessions_tenant_id",
                schema: "carts",
                table: "checkout_sessions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_checkout_sessions_tenant_id_cart_id",
                schema: "carts",
                table: "checkout_sessions",
                columns: new[] { "tenant_id", "cart_id" },
                unique: true,
                filter: "status IN ('Draft', 'AddressSet', 'ShippingSet', 'PaymentSet', 'Placing')");

            migrationBuilder.CreateIndex(
                name: "ix_checkout_sessions_tenant_id_customer_id_status",
                schema: "carts",
                table: "checkout_sessions",
                columns: new[] { "tenant_id", "customer_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_checkout_sessions_tenant_id_status_expires_at",
                schema: "carts",
                table: "checkout_sessions",
                columns: new[] { "tenant_id", "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_checkout_shipments_checkout_session_id",
                schema: "carts",
                table: "checkout_shipments",
                column: "checkout_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_checkout_shipments_tenant_id",
                schema: "carts",
                table: "checkout_shipments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_checkout_shipments_tenant_id_checkout_session_id_vendor_id",
                schema: "carts",
                table: "checkout_shipments",
                columns: new[] { "tenant_id", "checkout_session_id", "vendor_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cart_lines",
                schema: "carts");

            migrationBuilder.DropTable(
                name: "checkout_placements",
                schema: "carts");

            migrationBuilder.DropTable(
                name: "checkout_shipments",
                schema: "carts");

            migrationBuilder.DropTable(
                name: "carts",
                schema: "carts");

            migrationBuilder.DropTable(
                name: "checkout_sessions",
                schema: "carts");
        }
    }
}
