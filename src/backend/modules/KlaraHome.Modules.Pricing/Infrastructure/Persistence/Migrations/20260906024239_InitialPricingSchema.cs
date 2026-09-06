using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Pricing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPricingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pricing");

            migrationBuilder.CreateTable(
                name: "price_lists",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_lists", x => x.id);
                    table.CheckConstraint("ck_price_lists_priority", "priority >= 0 AND priority <= 1000");
                    table.CheckConstraint("ck_price_lists_type", "type IN ('Base', 'Sale', 'Scheduled')");
                    table.CheckConstraint("ck_price_lists_window", "ends_at IS NULL OR starts_at IS NULL OR ends_at > starts_at");
                });

            migrationBuilder.CreateTable(
                name: "promotion_redemptions",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    promotion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    redeemed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promotion_redemptions", x => x.id);
                    table.CheckConstraint("ck_promotion_redemptions_amount", "discount_amount >= 0");
                    table.CheckConstraint("ck_promotion_redemptions_status", "status IN ('Redeemed', 'Reversed')");
                });

            migrationBuilder.CreateTable(
                name: "promotions",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    applies_to = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    value = table.Column<decimal>(type: "numeric", nullable: false),
                    stacking = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    usage_limit_total = table.Column<int>(type: "integer", nullable: true),
                    usage_limit_per_customer = table.Column<int>(type: "integer", nullable: true),
                    usage_count = table.Column<int>(type: "integer", nullable: false),
                    min_order_value = table.Column<decimal>(type: "numeric", nullable: false),
                    max_discount = table.Column<decimal>(type: "numeric", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    conditions = table.Column<string>(type: "jsonb", nullable: false),
                    scope = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promotions", x => x.id);
                    table.CheckConstraint("ck_promotions_applies_to", "applies_to IN ('Line', 'Order', 'Shipping')");
                    table.CheckConstraint("ck_promotions_max_discount", "max_discount IS NULL OR max_discount > 0");
                    table.CheckConstraint("ck_promotions_min_order_value", "min_order_value >= 0");
                    table.CheckConstraint("ck_promotions_priority", "priority >= 0 AND priority <= 1000");
                    table.CheckConstraint("ck_promotions_stacking", "stacking IN ('Exclusive', 'Stackable')");
                    table.CheckConstraint("ck_promotions_type", "type IN ('Percentage', 'Fixed', 'FreeShipping', 'Bogo', 'Bundle', 'Tiered')");
                    table.CheckConstraint("ck_promotions_usage_count", "usage_count >= 0");
                    table.CheckConstraint("ck_promotions_usage_limits", "(usage_limit_total IS NULL OR usage_limit_total > 0) AND (usage_limit_per_customer IS NULL OR usage_limit_per_customer > 0)");
                    table.CheckConstraint("ck_promotions_value", "value >= 0");
                    table.CheckConstraint("ck_promotions_window", "ends_at IS NULL OR ends_at > starts_at");
                });

            migrationBuilder.CreateTable(
                name: "tax_rates",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hsn_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    rate = table.Column<decimal>(type: "numeric(7,4)", nullable: false),
                    cess_rate = table.Column<decimal>(type: "numeric(7,4)", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_rates", x => x.id);
                    table.CheckConstraint("ck_tax_rates_cess", "cess_rate >= 0 AND cess_rate <= 100");
                    table.CheckConstraint("ck_tax_rates_hsn", "hsn_code ~ '^[0-9]{4,8}$'");
                    table.CheckConstraint("ck_tax_rates_rate", "rate >= 0 AND rate <= 100");
                    table.CheckConstraint("ck_tax_rates_window", "effective_to IS NULL OR effective_to >= effective_from");
                });

            migrationBuilder.CreateTable(
                name: "wallets",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    balance_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    balance_currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallets", x => x.id);
                    table.CheckConstraint("ck_wallets_balance", "balance_amount >= 0");
                });

            migrationBuilder.CreateTable(
                name: "price_list_items",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_quantity = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    price_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    price_currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_list_items", x => x.id);
                    table.CheckConstraint("ck_price_list_items_min_quantity", "min_quantity >= 1");
                    table.CheckConstraint("ck_price_list_items_price", "price_amount >= 0");
                    table.ForeignKey(
                        name: "fk_price_list_items_price_lists_price_list_id",
                        column: x => x.price_list_id,
                        principalSchema: "pricing",
                        principalTable: "price_lists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wallet_transactions",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    reference_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    amount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    amount_currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    balance_after_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    balance_after_currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_transactions", x => x.id);
                    table.CheckConstraint("ck_wallet_transactions_amount", "amount_amount > 0");
                    table.CheckConstraint("ck_wallet_transactions_balance", "balance_after_amount >= 0");
                    table.CheckConstraint("ck_wallet_transactions_type", "type IN ('Credit', 'Debit', 'Expiry', 'Reversal')");
                    table.ForeignKey(
                        name: "fk_wallet_transactions_wallets_wallet_id",
                        column: x => x.wallet_id,
                        principalSchema: "pricing",
                        principalTable: "wallets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_price_list_items_price_list_id",
                schema: "pricing",
                table: "price_list_items",
                column: "price_list_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_list_items_tenant_id",
                schema: "pricing",
                table: "price_list_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_list_items_tenant_id_listing_id",
                schema: "pricing",
                table: "price_list_items",
                columns: new[] { "tenant_id", "listing_id" });

            migrationBuilder.CreateIndex(
                name: "ix_price_list_items_tenant_id_price_list_id_listing_id_min_qua",
                schema: "pricing",
                table: "price_list_items",
                columns: new[] { "tenant_id", "price_list_id", "listing_id", "min_quantity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_price_lists_tenant_id",
                schema: "pricing",
                table: "price_lists",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_lists_tenant_id_code",
                schema: "pricing",
                table: "price_lists",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_price_lists_tenant_id_is_active_priority",
                schema: "pricing",
                table: "price_lists",
                columns: new[] { "tenant_id", "is_active", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_price_lists_vendor_id",
                schema: "pricing",
                table: "price_lists",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_promotion_redemptions_tenant_id",
                schema: "pricing",
                table: "promotion_redemptions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_promotion_redemptions_tenant_id_customer_id_promotion_id",
                schema: "pricing",
                table: "promotion_redemptions",
                columns: new[] { "tenant_id", "customer_id", "promotion_id" });

            migrationBuilder.CreateIndex(
                name: "ix_promotion_redemptions_tenant_id_order_id",
                schema: "pricing",
                table: "promotion_redemptions",
                columns: new[] { "tenant_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_promotion_redemptions_tenant_id_promotion_id_order_id",
                schema: "pricing",
                table: "promotion_redemptions",
                columns: new[] { "tenant_id", "promotion_id", "order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_promotions_tenant_id",
                schema: "pricing",
                table: "promotions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_promotions_tenant_id_code",
                schema: "pricing",
                table: "promotions",
                columns: new[] { "tenant_id", "code" },
                unique: true,
                filter: "code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_promotions_tenant_id_is_active_priority",
                schema: "pricing",
                table: "promotions",
                columns: new[] { "tenant_id", "is_active", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_rates_tenant_id",
                schema: "pricing",
                table: "tax_rates",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_rates_tenant_id_hsn_code_effective_from",
                schema: "pricing",
                table: "tax_rates",
                columns: new[] { "tenant_id", "hsn_code", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_rates_tenant_id_hsn_code_is_active",
                schema: "pricing",
                table: "tax_rates",
                columns: new[] { "tenant_id", "hsn_code", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_transactions_tenant_id",
                schema: "pricing",
                table: "wallet_transactions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_transactions_tenant_id_wallet_id_occurred_at",
                schema: "pricing",
                table: "wallet_transactions",
                columns: new[] { "tenant_id", "wallet_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_transactions_tenant_id_wallet_id_reference_type_refe",
                schema: "pricing",
                table: "wallet_transactions",
                columns: new[] { "tenant_id", "wallet_id", "reference_type", "reference_id", "type" },
                unique: true,
                filter: "reference_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_transactions_wallet_id",
                schema: "pricing",
                table: "wallet_transactions",
                column: "wallet_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallets_tenant_id",
                schema: "pricing",
                table: "wallets",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallets_tenant_id_customer_id",
                schema: "pricing",
                table: "wallets",
                columns: new[] { "tenant_id", "customer_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "price_list_items",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "promotion_redemptions",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "promotions",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "tax_rates",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "wallet_transactions",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "price_lists",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "wallets",
                schema: "pricing");
        }
    }
}
