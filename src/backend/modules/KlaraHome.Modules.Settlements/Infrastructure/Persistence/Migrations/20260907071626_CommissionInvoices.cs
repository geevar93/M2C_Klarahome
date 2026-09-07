using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Settlements.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommissionInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_number_sequences_kind",
                schema: "settlements",
                table: "number_sequences");

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                schema: "settlements",
                table: "number_sequences",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.CreateTable(
                name: "commission_invoices",
                schema: "settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    settlement_cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    period_end = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    commission = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    platform_fee = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    payment_fee = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    taxable_value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    gst_rate = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    cgst = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    sgst = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    igst = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    supplier_gstin = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    recipient_gstin = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    place_of_supply_state_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commission_invoices", x => x.id);
                    table.CheckConstraint("ck_commission_invoices_money", "commission >= 0 AND platform_fee >= 0 AND payment_fee >= 0 AND taxable_value >= 0 AND cgst >= 0 AND sgst >= 0 AND igst >= 0 AND total >= 0");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_number_sequences_kind",
                schema: "settlements",
                table: "number_sequences",
                sql: "kind IN ('payout', 'commission-invoice')");

            migrationBuilder.CreateIndex(
                name: "ix_commission_invoices_settlement_cycle_id",
                schema: "settlements",
                table: "commission_invoices",
                column: "settlement_cycle_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_commission_invoices_tenant_id",
                schema: "settlements",
                table: "commission_invoices",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_commission_invoices_tenant_id_invoice_number",
                schema: "settlements",
                table: "commission_invoices",
                columns: new[] { "tenant_id", "invoice_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_commission_invoices_tenant_id_vendor_id_issued_at",
                schema: "settlements",
                table: "commission_invoices",
                columns: new[] { "tenant_id", "vendor_id", "issued_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commission_invoices",
                schema: "settlements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_number_sequences_kind",
                schema: "settlements",
                table: "number_sequences");

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                schema: "settlements",
                table: "number_sequences",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AddCheckConstraint(
                name: "ck_number_sequences_kind",
                schema: "settlements",
                table: "number_sequences",
                sql: "kind IN ('payout')");
        }
    }
}
