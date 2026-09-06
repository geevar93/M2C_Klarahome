using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPaymentsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.CreateTable(
                name: "cod_collections",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    collected_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    collected_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    collected_by = table.Column<Guid>(type: "uuid", nullable: true),
                    remitted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    remitted_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    remittance_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cod_collections", x => x.id);
                    table.CheckConstraint("ck_cod_collections_amount", "amount >= 0");
                    table.CheckConstraint("ck_cod_collections_status", "status IN ('Pending', 'Collected', 'Remitted', 'Waived', 'WrittenOff')");
                });

            migrationBuilder.CreateTable(
                name: "gateway_events",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    signature_valid = table.Column<bool>(type: "boolean", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    process_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gateway_events", x => x.id);
                    table.CheckConstraint("ck_gateway_events_status", "status IN ('Pending', 'Processed', 'Ignored', 'Failed', 'DeadLettered')");
                });

            migrationBuilder.CreateTable(
                name: "gateway_settlements",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_settlement_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    fees = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tax = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    utr = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    entry_count = table.Column<int>(type: "integer", nullable: false),
                    matched_count = table.Column<int>(type: "integer", nullable: false),
                    mismatch_count = table.Column<int>(type: "integer", nullable: false),
                    raw = table.Column<string>(type: "jsonb", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gateway_settlements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider_order_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    provider_payment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    amount_captured = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    amount_refunded = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    receipt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    authorized_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reconciled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amounts", "amount >= 0 AND amount_captured >= 0 AND amount_refunded >= 0 AND amount_refunded <= amount_captured + 1");
                    table.CheckConstraint("ck_payments_method", "method IN ('Unknown', 'Upi', 'Card', 'NetBanking', 'Wallet', 'Emi', 'Cod')");
                    table.CheckConstraint("ck_payments_provider", "provider IN ('razorpay', 'internal_cod')");
                    table.CheckConstraint("ck_payments_status", "status IN ('Created', 'Authorized', 'Captured', 'PartiallyRefunded', 'Refunded', 'Failed', 'Cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "gateway_settlement_entries",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    provider_entry_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    provider_payment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    fee = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tax = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    debit = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    credit = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    match_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    mismatch_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gateway_settlement_entries", x => x.id);
                    table.CheckConstraint("ck_settlement_entries_match", "match_status IN ('Unmatched', 'Matched', 'Mismatched')");
                    table.CheckConstraint("ck_settlement_entries_type", "entry_type IN ('payment', 'refund', 'adjustment', 'transfer')");
                    table.ForeignKey(
                        name: "fk_gateway_settlement_entries_gateway_settlements_settlement_id",
                        column: x => x.settlement_id,
                        principalSchema: "payments",
                        principalTable: "gateway_settlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_attempts",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_payment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    method_detail = table.Column<string>(type: "jsonb", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    error_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_attempts", x => x.id);
                    table.CheckConstraint("ck_payment_attempts_method", "method IN ('Unknown', 'Upi', 'Card', 'NetBanking', 'Wallet', 'Emi', 'Cod')");
                    table.CheckConstraint("ck_payment_attempts_source", "source IN ('Checkout', 'Webhook', 'Reconciliation', 'Admin')");
                    table.ForeignKey(
                        name: "fk_payment_attempts_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "payments",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refunds",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    return_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider_refund_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    speed = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                    initiated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    initiated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    rejected_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refunds", x => x.id);
                    table.CheckConstraint("ck_refunds_amount", "amount >= 0");
                    table.CheckConstraint("ck_refunds_approver", "approved_by IS NULL OR initiated_by IS NULL OR approved_by <> initiated_by");
                    table.CheckConstraint("ck_refunds_speed", "speed IN ('Normal', 'Optimum')");
                    table.CheckConstraint("ck_refunds_status", "status IN ('Requested', 'Approved', 'Processing', 'Processed', 'Failed', 'Rejected')");
                    table.ForeignKey(
                        name: "fk_refunds_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "payments",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cod_collections_tenant_id",
                schema: "payments",
                table: "cod_collections",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_cod_collections_tenant_id_order_id",
                schema: "payments",
                table: "cod_collections",
                columns: new[] { "tenant_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cod_collections_tenant_id_status_collected_at",
                schema: "payments",
                table: "cod_collections",
                columns: new[] { "tenant_id", "status", "collected_at" });

            migrationBuilder.CreateIndex(
                name: "ix_cod_collections_tenant_id_sub_order_id",
                schema: "payments",
                table: "cod_collections",
                columns: new[] { "tenant_id", "sub_order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cod_collections_tenant_id_vendor_id_status",
                schema: "payments",
                table: "cod_collections",
                columns: new[] { "tenant_id", "vendor_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_gateway_events_tenant_id",
                schema: "payments",
                table: "gateway_events",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_gateway_events_tenant_id_payment_id",
                schema: "payments",
                table: "gateway_events",
                columns: new[] { "tenant_id", "payment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_gateway_events_tenant_id_provider_provider_event_id",
                schema: "payments",
                table: "gateway_events",
                columns: new[] { "tenant_id", "provider", "provider_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gateway_events_tenant_id_received_at",
                schema: "payments",
                table: "gateway_events",
                columns: new[] { "tenant_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_gateway_events_tenant_id_status_next_attempt_at",
                schema: "payments",
                table: "gateway_events",
                columns: new[] { "tenant_id", "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_gateway_settlement_entries_settlement_id",
                schema: "payments",
                table: "gateway_settlement_entries",
                column: "settlement_id");

            migrationBuilder.CreateIndex(
                name: "ix_gateway_settlement_entries_tenant_id",
                schema: "payments",
                table: "gateway_settlement_entries",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_gateway_settlement_entries_tenant_id_payment_id",
                schema: "payments",
                table: "gateway_settlement_entries",
                columns: new[] { "tenant_id", "payment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_gateway_settlement_entries_tenant_id_provider_payment_id",
                schema: "payments",
                table: "gateway_settlement_entries",
                columns: new[] { "tenant_id", "provider_payment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_gateway_settlement_entries_tenant_id_settlement_id_match_st",
                schema: "payments",
                table: "gateway_settlement_entries",
                columns: new[] { "tenant_id", "settlement_id", "match_status" });

            migrationBuilder.CreateIndex(
                name: "ix_gateway_settlements_tenant_id",
                schema: "payments",
                table: "gateway_settlements",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_gateway_settlements_tenant_id_provider_provider_settlement_",
                schema: "payments",
                table: "gateway_settlements",
                columns: new[] { "tenant_id", "provider", "provider_settlement_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gateway_settlements_tenant_id_settled_at",
                schema: "payments",
                table: "gateway_settlements",
                columns: new[] { "tenant_id", "settled_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_payment_id",
                schema: "payments",
                table: "payment_attempts",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_tenant_id",
                schema: "payments",
                table: "payment_attempts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_tenant_id_payment_id_attempted_at",
                schema: "payments",
                table: "payment_attempts",
                columns: new[] { "tenant_id", "payment_id", "attempted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_tenant_id_provider_payment_id",
                schema: "payments",
                table: "payment_attempts",
                columns: new[] { "tenant_id", "provider_payment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_open_per_order",
                schema: "payments",
                table: "payments",
                columns: new[] { "tenant_id", "order_id" },
                unique: true,
                filter: "status IN ('Created', 'Authorized')");

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id",
                schema: "payments",
                table: "payments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_idempotency_key",
                schema: "payments",
                table: "payments",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_order_id_opened_at",
                schema: "payments",
                table: "payments",
                columns: new[] { "tenant_id", "order_id", "opened_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_order_number",
                schema: "payments",
                table: "payments",
                columns: new[] { "tenant_id", "order_number" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_provider_order_id",
                schema: "payments",
                table: "payments",
                columns: new[] { "tenant_id", "provider_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_provider_payment_id",
                schema: "payments",
                table: "payments",
                columns: new[] { "tenant_id", "provider_payment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_status_opened_at",
                schema: "payments",
                table: "payments",
                columns: new[] { "tenant_id", "status", "opened_at" });

            migrationBuilder.CreateIndex(
                name: "ix_refunds_payment_id",
                schema: "payments",
                table: "refunds",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_tenant_id",
                schema: "payments",
                table: "refunds",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_tenant_id_idempotency_key",
                schema: "payments",
                table: "refunds",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refunds_tenant_id_order_id",
                schema: "payments",
                table: "refunds",
                columns: new[] { "tenant_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_refunds_tenant_id_payment_id",
                schema: "payments",
                table: "refunds",
                columns: new[] { "tenant_id", "payment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_refunds_tenant_id_provider_refund_id",
                schema: "payments",
                table: "refunds",
                columns: new[] { "tenant_id", "provider_refund_id" });

            migrationBuilder.CreateIndex(
                name: "ix_refunds_tenant_id_status_initiated_at",
                schema: "payments",
                table: "refunds",
                columns: new[] { "tenant_id", "status", "initiated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cod_collections",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "gateway_events",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "gateway_settlement_entries",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payment_attempts",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "refunds",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "gateway_settlements",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "payments");
        }
    }
}
