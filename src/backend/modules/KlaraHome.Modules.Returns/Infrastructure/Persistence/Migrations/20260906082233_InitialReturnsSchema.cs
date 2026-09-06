using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Returns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialReturnsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "returns");

            migrationBuilder.CreateTable(
                name: "credit_notes",
                schema: "returns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoice_number = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    credit_note_number = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
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
                    table.PrimaryKey("pk_credit_notes", x => x.id);
                    table.CheckConstraint("ck_credit_notes_amounts", "taxable_value >= 0 AND cgst >= 0 AND sgst >= 0 AND igst >= 0 AND cess >= 0 AND total >= 0");
                    table.CheckConstraint("ck_credit_notes_tax_split", "(igst = 0) OR (cgst = 0 AND sgst = 0)");
                    table.CheckConstraint("ck_credit_notes_vendor", "vendor_id IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "number_sequences",
                schema: "returns",
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
                    table.CheckConstraint("ck_number_sequences_kind", "kind IN ('return', 'credit-note')");
                    table.CheckConstraint("ck_number_sequences_next", "next_value >= 1");
                });

            migrationBuilder.CreateTable(
                name: "return_reasons",
                schema: "returns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    label = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    requires_evidence = table.Column<bool>(type: "boolean", nullable: false),
                    is_pickup_required = table.Column<bool>(type: "boolean", nullable: false),
                    requires_qc = table.Column<bool>(type: "boolean", nullable: false),
                    is_auto_approved = table.Column<bool>(type: "boolean", nullable: false),
                    shipping_payer = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_vendor_fault = table.Column<bool>(type: "boolean", nullable: false),
                    allows_replacement = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_return_reasons", x => x.id);
                    table.CheckConstraint("ck_return_reasons_shipping_payer", "shipping_payer IN ('Platform', 'Vendor', 'Customer')");
                });

            migrationBuilder.CreateTable(
                name: "returns",
                schema: "returns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sub_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_order_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    evidence_file_ids = table.Column<string>(type: "jsonb", nullable: false),
                    estimated_refund = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    approved_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    refund_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    return_shipping_fee = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    shipping_refund_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    refund_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    is_pickup_required = table.Column<bool>(type: "boolean", nullable: false),
                    pickup_shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pickup_awb = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    pickup_scheduled_for = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    refund_id = table.Column<Guid>(type: "uuid", nullable: true),
                    credit_note_id = table.Column<Guid>(type: "uuid", nullable: true),
                    replacement_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    qc_passed = table.Column<bool>(type: "boolean", nullable: true),
                    qc_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    qc_by = table.Column<Guid>(type: "uuid", nullable: true),
                    rejected_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    picked_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    inspected_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    refunded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_returns", x => x.id);
                    table.CheckConstraint("ck_returns_amounts", "estimated_refund >= 0 AND approved_amount >= 0 AND refund_amount >= 0 AND return_shipping_fee >= 0");
                    table.CheckConstraint("ck_returns_refund_mode", "refund_mode IS NULL OR refund_mode IN ('Original', 'Wallet')");
                    table.CheckConstraint("ck_returns_refund_settled", "(refund_amount = 0 AND refund_mode IS NULL) OR (refund_amount > 0 AND refund_mode IS NOT NULL)");
                    table.CheckConstraint("ck_returns_status", "status IN ('Requested', 'Approved', 'Rejected', 'PickupScheduled', 'Picked', 'InTransit', 'Received', 'QcPassed', 'QcFailed', 'Refunded', 'Replaced', 'Closed', 'Cancelled')");
                    table.CheckConstraint("ck_returns_type", "type IN ('Return', 'Replacement')");
                });

            migrationBuilder.CreateTable(
                name: "return_lines",
                schema: "returns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    quantity_accepted = table.Column<int>(type: "integer", nullable: false),
                    taxable_value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    cgst = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    sgst = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    igst = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    cess = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    refund_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    disposition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    qc_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_return_lines", x => x.id);
                    table.CheckConstraint("ck_return_lines_amounts", "taxable_value >= 0 AND cgst >= 0 AND sgst >= 0 AND igst >= 0 AND cess >= 0 AND refund_amount >= 0");
                    table.CheckConstraint("ck_return_lines_disposition", "disposition IN ('Pending', 'Restock', 'Scrap', 'Quarantine')");
                    table.CheckConstraint("ck_return_lines_quantities", "quantity > 0 AND quantity_accepted >= 0 AND quantity_accepted <= quantity");
                    table.ForeignKey(
                        name: "fk_return_lines_returns_return_id",
                        column: x => x.return_id,
                        principalSchema: "returns",
                        principalTable: "returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_tenant_id",
                schema: "returns",
                table: "credit_notes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_tenant_id_order_id",
                schema: "returns",
                table: "credit_notes",
                columns: new[] { "tenant_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_tenant_id_return_id",
                schema: "returns",
                table: "credit_notes",
                columns: new[] { "tenant_id", "return_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_tenant_id_vendor_id_financial_year_credit_note",
                schema: "returns",
                table: "credit_notes",
                columns: new[] { "tenant_id", "vendor_id", "financial_year", "credit_note_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_tenant_id_vendor_id_issued_at",
                schema: "returns",
                table: "credit_notes",
                columns: new[] { "tenant_id", "vendor_id", "issued_at" });

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_vendor_id",
                schema: "returns",
                table: "credit_notes",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_number_sequences_tenant_id",
                schema: "returns",
                table: "number_sequences",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_number_sequences_tenant_id_kind_scope_key_financial_year",
                schema: "returns",
                table: "number_sequences",
                columns: new[] { "tenant_id", "kind", "scope_key", "financial_year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_lines_return_id",
                schema: "returns",
                table: "return_lines",
                column: "return_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_lines_tenant_id",
                schema: "returns",
                table: "return_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_lines_tenant_id_order_line_id",
                schema: "returns",
                table: "return_lines",
                columns: new[] { "tenant_id", "order_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_return_lines_tenant_id_return_id_order_line_id",
                schema: "returns",
                table: "return_lines",
                columns: new[] { "tenant_id", "return_id", "order_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_reasons_tenant_id",
                schema: "returns",
                table: "return_reasons",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_reasons_tenant_id_code",
                schema: "returns",
                table: "return_reasons",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_reasons_tenant_id_is_active_sort_order",
                schema: "returns",
                table: "return_reasons",
                columns: new[] { "tenant_id", "is_active", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_returns_tenant_id",
                schema: "returns",
                table: "returns",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_returns_tenant_id_customer_id_requested_at",
                schema: "returns",
                table: "returns",
                columns: new[] { "tenant_id", "customer_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_returns_tenant_id_order_id",
                schema: "returns",
                table: "returns",
                columns: new[] { "tenant_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_returns_tenant_id_return_number",
                schema: "returns",
                table: "returns",
                columns: new[] { "tenant_id", "return_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_returns_tenant_id_status_requested_at",
                schema: "returns",
                table: "returns",
                columns: new[] { "tenant_id", "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_returns_tenant_id_sub_order_id",
                schema: "returns",
                table: "returns",
                columns: new[] { "tenant_id", "sub_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_returns_tenant_id_vendor_id_status",
                schema: "returns",
                table: "returns",
                columns: new[] { "tenant_id", "vendor_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_returns_vendor_id",
                schema: "returns",
                table: "returns",
                column: "vendor_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_notes",
                schema: "returns");

            migrationBuilder.DropTable(
                name: "number_sequences",
                schema: "returns");

            migrationBuilder.DropTable(
                name: "return_lines",
                schema: "returns");

            migrationBuilder.DropTable(
                name: "return_reasons",
                schema: "returns");

            migrationBuilder.DropTable(
                name: "returns",
                schema: "returns");
        }
    }
}
