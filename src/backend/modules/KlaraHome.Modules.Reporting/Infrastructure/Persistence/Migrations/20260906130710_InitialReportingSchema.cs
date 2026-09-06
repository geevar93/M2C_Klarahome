using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialReportingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reporting");

            migrationBuilder.CreateTable(
                name: "fact_funnel_events",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cart_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    item_count = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fact_funnel_events", x => x.id);
                    table.CheckConstraint("ck_fact_funnel_events_step", "step IN ('CartAbandoned', 'CartConverted', 'OrderPlaced', 'OrderPaid', 'OrderConfirmed')");
                });

            migrationBuilder.CreateTable(
                name: "fact_inventory_ageing",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_on = table.Column<DateOnly>(type: "date", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quantity_on_hand = table.Column<int>(type: "integer", nullable: false),
                    quantity_reserved = table.Column<int>(type: "integer", nullable: false),
                    last_inbound_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_outbound_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    age_days = table.Column<int>(type: "integer", nullable: true),
                    age_bucket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fact_inventory_ageing", x => x.id);
                    table.CheckConstraint("ck_fact_inventory_ageing_counts", "quantity_on_hand >= 0 AND quantity_reserved >= 0 AND (age_days IS NULL OR age_days >= 0)");
                });

            migrationBuilder.CreateTable(
                name: "fact_order_lines",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sub_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: true),
                    brand_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    product_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    payment_method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_cod = table.Column<bool>(type: "boolean", nullable: false),
                    cancelled_quantity = table.Column<int>(type: "integer", nullable: false),
                    cancelled_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    returned_quantity = table.Column<int>(type: "integer", nullable: false),
                    returned_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    commission_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    confirmed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fact_order_lines", x => x.id);
                    table.CheckConstraint("ck_fact_order_lines_amounts", "line_total >= 0 AND cancelled_amount >= 0 AND returned_amount >= 0 AND cancelled_amount + returned_amount <= line_total");
                    table.CheckConstraint("ck_fact_order_lines_quantities", "quantity >= 0 AND cancelled_quantity >= 0 AND returned_quantity >= 0 AND cancelled_quantity + returned_quantity <= quantity");
                });

            migrationBuilder.CreateTable(
                name: "fact_orders",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_cod = table.Column<bool>(type: "boolean", nullable: false),
                    grand_total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    amount_payable = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false),
                    vendor_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    placed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    placed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fact_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fact_payments",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fact_payments", x => x.id);
                    table.CheckConstraint("ck_fact_payments_amount", "amount >= 0");
                    table.CheckConstraint("ck_fact_payments_kind", "kind IN ('Captured', 'Refunded', 'CodCollected', 'Failed')");
                });

            migrationBuilder.CreateTable(
                name: "fact_return_lines",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    disposition = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    requested_on = table.Column<DateOnly>(type: "date", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fact_return_lines", x => x.id);
                    table.CheckConstraint("ck_fact_return_lines_amounts", "quantity > 0 AND amount >= 0");
                });

            migrationBuilder.CreateTable(
                name: "fact_settlements",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period_start = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    period_end = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    gross_sales = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    commission = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    fees = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tcs = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tds = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    refunds = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    net_payable = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false),
                    order_count = table.Column<int>(type: "integer", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fact_settlements", x => x.id);
                    table.CheckConstraint("ck_fact_settlements_period", "period_end > period_start");
                });

            migrationBuilder.CreateTable(
                name: "report_runs",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    report_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    period_end = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: true),
                    storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    byte_size = table.Column<long>(type: "bigint", nullable: true),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_report_runs", x => x.id);
                    table.CheckConstraint("ck_report_runs_completeness", "status <> 'Completed' OR (storage_key IS NOT NULL AND row_count IS NOT NULL)");
                    table.CheckConstraint("ck_report_runs_format", "format IN ('Csv')");
                    table.CheckConstraint("ck_report_runs_period", "period_end > period_start");
                    table.CheckConstraint("ck_report_runs_status", "status IN ('Running', 'Completed', 'Failed')");
                });

            migrationBuilder.CreateTable(
                name: "report_schedules",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    hour_utc = table.Column<int>(type: "integer", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: true),
                    day_of_month = table.Column<int>(type: "integer", nullable: true),
                    recipients = table.Column<string[]>(type: "text[]", nullable: false),
                    format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_schedules", x => x.id);
                    table.CheckConstraint("ck_report_schedules_days", "(day_of_week IS NULL OR day_of_week BETWEEN 1 AND 7) AND (day_of_month IS NULL OR day_of_month BETWEEN 1 AND 31) AND (frequency <> 'Weekly' OR day_of_month IS NULL) AND (frequency <> 'Monthly' OR day_of_week IS NULL) AND (frequency <> 'Daily' OR (day_of_week IS NULL AND day_of_month IS NULL))");
                    table.CheckConstraint("ck_report_schedules_format", "format IN ('Csv')");
                    table.CheckConstraint("ck_report_schedules_frequency", "frequency IN ('Daily', 'Weekly', 'Monthly')");
                    table.CheckConstraint("ck_report_schedules_hour", "hour_utc BETWEEN 0 AND 23");
                });

            migrationBuilder.CreateIndex(
                name: "ix_fact_funnel_events_tenant_id",
                schema: "reporting",
                table: "fact_funnel_events",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_fact_funnel_events_tenant_id_occurred_on_step",
                schema: "reporting",
                table: "fact_funnel_events",
                columns: new[] { "tenant_id", "occurred_on", "step" });

            migrationBuilder.CreateIndex(
                name: "ux_fact_funnel_events_event",
                schema: "reporting",
                table: "fact_funnel_events",
                columns: new[] { "tenant_id", "source_event_id", "step" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fact_inventory_ageing_tenant_id",
                schema: "reporting",
                table: "fact_inventory_ageing",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_fact_inventory_ageing_tenant_id_snapshot_on_age_bucket",
                schema: "reporting",
                table: "fact_inventory_ageing",
                columns: new[] { "tenant_id", "snapshot_on", "age_bucket" });

            migrationBuilder.CreateIndex(
                name: "ix_fact_inventory_ageing_vendor_id",
                schema: "reporting",
                table: "fact_inventory_ageing",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ux_fact_inventory_ageing_line",
                schema: "reporting",
                table: "fact_inventory_ageing",
                columns: new[] { "tenant_id", "snapshot_on", "listing_id", "warehouse_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fact_order_lines_sub_order_id",
                schema: "reporting",
                table: "fact_order_lines",
                column: "sub_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_fact_order_lines_tenant_id",
                schema: "reporting",
                table: "fact_order_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_fact_order_lines_tenant_id_confirmed_on",
                schema: "reporting",
                table: "fact_order_lines",
                columns: new[] { "tenant_id", "confirmed_on" });

            migrationBuilder.CreateIndex(
                name: "ix_fact_order_lines_tenant_id_confirmed_on_category_id",
                schema: "reporting",
                table: "fact_order_lines",
                columns: new[] { "tenant_id", "confirmed_on", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fact_order_lines_tenant_id_confirmed_on_vendor_id",
                schema: "reporting",
                table: "fact_order_lines",
                columns: new[] { "tenant_id", "confirmed_on", "vendor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fact_order_lines_tenant_id_sku_confirmed_on",
                schema: "reporting",
                table: "fact_order_lines",
                columns: new[] { "tenant_id", "sku", "confirmed_on" });

            migrationBuilder.CreateIndex(
                name: "ix_fact_order_lines_vendor_id",
                schema: "reporting",
                table: "fact_order_lines",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ux_fact_order_lines_line",
                schema: "reporting",
                table: "fact_order_lines",
                columns: new[] { "tenant_id", "order_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fact_orders_tenant_id",
                schema: "reporting",
                table: "fact_orders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_fact_orders_tenant_id_placed_on",
                schema: "reporting",
                table: "fact_orders",
                columns: new[] { "tenant_id", "placed_on" });

            migrationBuilder.CreateIndex(
                name: "ix_fact_orders_tenant_id_placed_on_is_cod",
                schema: "reporting",
                table: "fact_orders",
                columns: new[] { "tenant_id", "placed_on", "is_cod" });

            migrationBuilder.CreateIndex(
                name: "ux_fact_orders_order",
                schema: "reporting",
                table: "fact_orders",
                columns: new[] { "tenant_id", "order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fact_payments_order_id",
                schema: "reporting",
                table: "fact_payments",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_fact_payments_tenant_id",
                schema: "reporting",
                table: "fact_payments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_fact_payments_tenant_id_occurred_on_kind",
                schema: "reporting",
                table: "fact_payments",
                columns: new[] { "tenant_id", "occurred_on", "kind" });

            migrationBuilder.CreateIndex(
                name: "ux_fact_payments_event",
                schema: "reporting",
                table: "fact_payments",
                columns: new[] { "tenant_id", "source_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fact_return_lines_return_id",
                schema: "reporting",
                table: "fact_return_lines",
                column: "return_id");

            migrationBuilder.CreateIndex(
                name: "ix_fact_return_lines_tenant_id",
                schema: "reporting",
                table: "fact_return_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_fact_return_lines_tenant_id_requested_on_reason_code",
                schema: "reporting",
                table: "fact_return_lines",
                columns: new[] { "tenant_id", "requested_on", "reason_code" });

            migrationBuilder.CreateIndex(
                name: "ix_fact_return_lines_vendor_id",
                schema: "reporting",
                table: "fact_return_lines",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ux_fact_return_lines_line",
                schema: "reporting",
                table: "fact_return_lines",
                columns: new[] { "tenant_id", "return_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fact_settlements_tenant_id",
                schema: "reporting",
                table: "fact_settlements",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_fact_settlements_tenant_id_closed_on_vendor_id",
                schema: "reporting",
                table: "fact_settlements",
                columns: new[] { "tenant_id", "closed_on", "vendor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fact_settlements_vendor_id",
                schema: "reporting",
                table: "fact_settlements",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ux_fact_settlements_period",
                schema: "reporting",
                table: "fact_settlements",
                columns: new[] { "tenant_id", "period_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_report_runs_schedule_id_started_at",
                schema: "reporting",
                table: "report_runs",
                columns: new[] { "schedule_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_report_runs_tenant_id",
                schema: "reporting",
                table: "report_runs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_report_runs_tenant_id_started_at",
                schema: "reporting",
                table: "report_runs",
                columns: new[] { "tenant_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_report_schedules_due",
                schema: "reporting",
                table: "report_schedules",
                column: "next_run_at",
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_report_schedules_tenant_id",
                schema: "reporting",
                table: "report_schedules",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_report_schedules_tenant_id_report_key",
                schema: "reporting",
                table: "report_schedules",
                columns: new[] { "tenant_id", "report_key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fact_funnel_events",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "fact_inventory_ageing",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "fact_order_lines",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "fact_orders",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "fact_payments",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "fact_return_lines",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "fact_settlements",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "report_runs",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "report_schedules",
                schema: "reporting");
        }
    }
}
