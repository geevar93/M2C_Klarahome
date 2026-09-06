using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Settlements.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSettlementsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "settlements");

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    direction = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    taxable_value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    reference_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sub_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    settlement_cycle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payout_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_amounts", "amount >= 0 AND taxable_value >= 0");
                    table.CheckConstraint("ck_ledger_entries_direction", "direction IN ('Credit', 'Debit')");
                    table.CheckConstraint("ck_ledger_entries_reference", "reference_type IN ('sub-order', 'credit-note', 'cycle', 'payout', 'manual')");
                    table.CheckConstraint("ck_ledger_entries_type", "entry_type IN ('sale', 'commission', 'platform_tax', 'platform_fee', 'payment_fee', 'shipping_fee', 'refund', 'refund_commission_reversal', 'tcs', 'tds', 'adjustment', 'payout')");
                });

            migrationBuilder.CreateTable(
                name: "number_sequences",
                schema: "settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scope_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    next_value = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_sequences", x => x.id);
                    table.CheckConstraint("ck_number_sequences_kind", "kind IN ('payout')");
                    table.CheckConstraint("ck_number_sequences_next", "next_value >= 1");
                });

            migrationBuilder.CreateTable(
                name: "payout_batches",
                schema: "settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    vendor_count = table.Column<int>(type: "integer", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    provider_batch_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    cancelled_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payout_batches", x => x.id);
                    table.CheckConstraint("ck_payout_batches_amounts", "total_amount >= 0 AND vendor_count >= 0");
                    table.CheckConstraint("ck_payout_batches_approval", "(approved_by IS NULL AND approved_at IS NULL) OR (approved_by IS NOT NULL AND approved_at IS NOT NULL)");
                    table.CheckConstraint("ck_payout_batches_self_approval", "approved_by IS NULL OR requested_by IS NULL OR approved_by <> requested_by");
                    table.CheckConstraint("ck_payout_batches_status", "status IN ('Draft', 'Approved', 'Processing', 'Completed', 'PartiallyFailed', 'Failed', 'Cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "settlement_cycles",
                schema: "settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period_start = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    period_end = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    opening_balance = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    gross_sales = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    taxable_sales = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total_commission = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total_fees = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total_refunds = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    taxable_refunds = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total_adjustments = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total_payouts = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tcs = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tds = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    net_payable = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    entry_count = table.Column<int>(type: "integer", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    payout_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settlement_cycles", x => x.id);
                    table.CheckConstraint("ck_settlement_cycles_amounts", "gross_sales >= 0 AND taxable_sales >= 0 AND total_commission >= 0 AND total_fees >= 0 AND total_refunds >= 0 AND taxable_refunds >= 0 AND tcs >= 0 AND tds >= 0");
                    table.CheckConstraint("ck_settlement_cycles_closed", "(status = 'Open' AND closed_at IS NULL) OR (status <> 'Open' AND closed_at IS NOT NULL)");
                    table.CheckConstraint("ck_settlement_cycles_period", "period_end > period_start");
                    table.CheckConstraint("ck_settlement_cycles_status", "status IN ('Open', 'Closed', 'Paid')");
                });

            migrationBuilder.CreateTable(
                name: "payout_items",
                schema: "settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payout_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vendor_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    vendor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    settlement_cycle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    destination_account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    destination_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    provider_payout_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    provider_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    utr = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
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
                    table.PrimaryKey("pk_payout_items", x => x.id);
                    table.CheckConstraint("ck_payout_items_amount", "amount >= 0");
                    table.CheckConstraint("ck_payout_items_completed", "status <> 'Completed' OR provider_payout_id IS NOT NULL");
                    table.CheckConstraint("ck_payout_items_status", "status IN ('Pending', 'Processing', 'Completed', 'Failed', 'Skipped')");
                    table.ForeignKey(
                        name: "fk_payout_items_payout_batches_payout_batch_id",
                        column: x => x.payout_batch_id,
                        principalSchema: "settlements",
                        principalTable: "payout_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_tenant_id",
                schema: "settlements",
                table: "ledger_entries",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_tenant_id_payout_batch_id",
                schema: "settlements",
                table: "ledger_entries",
                columns: new[] { "tenant_id", "payout_batch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_tenant_id_settlement_cycle_id",
                schema: "settlements",
                table: "ledger_entries",
                columns: new[] { "tenant_id", "settlement_cycle_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_tenant_id_source_key",
                schema: "settlements",
                table: "ledger_entries",
                columns: new[] { "tenant_id", "source_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_tenant_id_sub_order_id",
                schema: "settlements",
                table: "ledger_entries",
                columns: new[] { "tenant_id", "sub_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_unsettled",
                schema: "settlements",
                table: "ledger_entries",
                columns: new[] { "tenant_id", "vendor_id", "occurred_at" },
                filter: "settlement_cycle_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_vendor_id",
                schema: "settlements",
                table: "ledger_entries",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_number_sequences_tenant_id",
                schema: "settlements",
                table: "number_sequences",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_number_sequences_tenant_id_kind_scope_key",
                schema: "settlements",
                table: "number_sequences",
                columns: new[] { "tenant_id", "kind", "scope_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payout_batches_tenant_id",
                schema: "settlements",
                table: "payout_batches",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payout_batches_tenant_id_reference",
                schema: "settlements",
                table: "payout_batches",
                columns: new[] { "tenant_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payout_batches_tenant_id_status_requested_at",
                schema: "settlements",
                table: "payout_batches",
                columns: new[] { "tenant_id", "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payout_items_payout_batch_id",
                schema: "settlements",
                table: "payout_items",
                column: "payout_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_payout_items_tenant_id",
                schema: "settlements",
                table: "payout_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payout_items_tenant_id_payout_batch_id_vendor_id",
                schema: "settlements",
                table: "payout_items",
                columns: new[] { "tenant_id", "payout_batch_id", "vendor_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payout_items_tenant_id_settlement_cycle_id",
                schema: "settlements",
                table: "payout_items",
                columns: new[] { "tenant_id", "settlement_cycle_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payout_items_tenant_id_status_sent_at",
                schema: "settlements",
                table: "payout_items",
                columns: new[] { "tenant_id", "status", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payout_items_tenant_id_vendor_id_created_at",
                schema: "settlements",
                table: "payout_items",
                columns: new[] { "tenant_id", "vendor_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payout_items_vendor_id",
                schema: "settlements",
                table: "payout_items",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_settlement_cycles_tenant_id",
                schema: "settlements",
                table: "settlement_cycles",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_settlement_cycles_tenant_id_payout_batch_id",
                schema: "settlements",
                table: "settlement_cycles",
                columns: new[] { "tenant_id", "payout_batch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_settlement_cycles_tenant_id_status_period_end",
                schema: "settlements",
                table: "settlement_cycles",
                columns: new[] { "tenant_id", "status", "period_end" });

            migrationBuilder.CreateIndex(
                name: "ix_settlement_cycles_tenant_id_vendor_id_period_start",
                schema: "settlements",
                table: "settlement_cycles",
                columns: new[] { "tenant_id", "vendor_id", "period_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_settlement_cycles_vendor_id",
                schema: "settlements",
                table: "settlement_cycles",
                column: "vendor_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "settlements");

            migrationBuilder.DropTable(
                name: "number_sequences",
                schema: "settlements");

            migrationBuilder.DropTable(
                name: "payout_items",
                schema: "settlements");

            migrationBuilder.DropTable(
                name: "settlement_cycles",
                schema: "settlements");

            migrationBuilder.DropTable(
                name: "payout_batches",
                schema: "settlements");
        }
    }
}
