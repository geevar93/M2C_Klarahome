using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Shipping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialShippingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "shipping");

            migrationBuilder.CreateTable(
                name: "courier_events",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    signature_valid = table.Column<bool>(type: "boolean", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    awb = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    process_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_courier_events", x => x.id);
                    table.CheckConstraint("ck_courier_events_status", "status IN ('Pending', 'Processed', 'Ignored', 'Failed', 'DeadLettered')");
                });

            migrationBuilder.CreateTable(
                name: "manifests",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    courier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pickup_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    shipment_count = table.Column<int>(type: "integer", nullable: false),
                    total_weight_grams = table.Column<int>(type: "integer", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_manifest_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    generated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manifests", x => x.id);
                    table.CheckConstraint("ck_manifests_counts", "shipment_count >= 0 AND total_weight_grams >= 0");
                });

            migrationBuilder.CreateTable(
                name: "ndr_records",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sub_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    awb = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    action = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    action_remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    rescheduled_for = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    actioned_by = table.Column<Guid>(type: "uuid", nullable: true),
                    actioned_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ndr_records", x => x.id);
                    table.CheckConstraint("ck_ndr_records_action", "action IN ('Pending', 'Reattempt', 'Rescheduled', 'AddressUpdated', 'ReturnToOrigin', 'Resolved')");
                    table.CheckConstraint("ck_ndr_records_attempt", "attempt_number > 0");
                    table.CheckConstraint("ck_ndr_records_reason", "reason_code IN ('Other', 'CustomerUnavailable', 'AddressIncorrect', 'Refused', 'RescheduleRequested', 'CodNotReady', 'Unreachable')");
                    table.CheckConstraint("ck_ndr_records_resolution", "(action = 'Pending') = (resolved_at IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "serviceability_cache",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pincode = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    courier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    prepaid_ok = table.Column<bool>(type: "boolean", nullable: false),
                    cod_ok = table.Column<bool>(type: "boolean", nullable: false),
                    pickup_ok = table.Column<bool>(type: "boolean", nullable: false),
                    eta_days = table.Column<int>(type: "integer", nullable: true),
                    max_weight_grams = table.Column<int>(type: "integer", nullable: true),
                    refreshed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_serviceability_cache", x => x.id);
                    table.CheckConstraint("ck_serviceability_cache_pincode", "pincode ~ '^[1-9][0-9]{5}$'");
                });

            migrationBuilder.CreateTable(
                name: "shipments",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sub_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_order_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    courier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    service_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    awb = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    provider_shipment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    tracking_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    label_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    label_object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    manifest_id = table.Column<Guid>(type: "uuid", nullable: true),
                    weight_grams = table.Column<int>(type: "integer", nullable: false),
                    charged_weight_grams = table.Column<int>(type: "integer", nullable: true),
                    dimensions = table.Column<string>(type: "jsonb", nullable: false),
                    pickup_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pickup_pincode = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                    destination_pincode = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    destination_state_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cod_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    declared_value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    freight_charged = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    freight_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    is_return = table.Column<bool>(type: "boolean", nullable: false),
                    pickup_scheduled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    picked_up_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    expected_delivery_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_tracked_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    delivery_attempts = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipments", x => x.id);
                    table.CheckConstraint("ck_shipments_amounts", "weight_grams >= 0 AND (charged_weight_grams IS NULL OR charged_weight_grams >= 0) AND declared_value >= 0 AND freight_charged >= 0 AND (freight_cost IS NULL OR freight_cost >= 0) AND (cod_amount IS NULL OR cod_amount >= 0)");
                    table.CheckConstraint("ck_shipments_booking", "(status = 'Draft') OR (status = 'Cancelled') OR (awb IS NOT NULL AND courier IS NOT NULL)");
                    table.CheckConstraint("ck_shipments_pincode", "destination_pincode ~ '^[1-9][0-9]{5}$'");
                    table.CheckConstraint("ck_shipments_status", "status IN ('Draft', 'Created', 'LabelGenerated', 'PickupScheduled', 'PickedUp', 'InTransit', 'OutForDelivery', 'Delivered', 'Exception', 'RtoInitiated', 'RtoDelivered', 'Cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "shipping_rates",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    min_weight_grams = table.Column<int>(type: "integer", nullable: false),
                    max_weight_grams = table.Column<int>(type: "integer", nullable: false),
                    min_order_value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    max_order_value = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    base_rate = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    per_kg_rate = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    free_above = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    cod_fee = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    is_cod_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    eta_min_days = table.Column<int>(type: "integer", nullable: false),
                    eta_max_days = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_shipping_rates", x => x.id);
                    table.CheckConstraint("ck_shipping_rates_amounts", "base_rate >= 0 AND per_kg_rate >= 0 AND cod_fee >= 0 AND (free_above IS NULL OR free_above >= 0)");
                    table.CheckConstraint("ck_shipping_rates_bands", "min_weight_grams >= 0 AND max_weight_grams >= min_weight_grams AND min_order_value >= 0 AND (max_order_value IS NULL OR max_order_value >= min_order_value)");
                    table.CheckConstraint("ck_shipping_rates_eta", "eta_min_days >= 0 AND eta_max_days >= eta_min_days");
                    table.CheckConstraint("ck_shipping_rates_method", "method IN ('Standard', 'Express')");
                });

            migrationBuilder.CreateTable(
                name: "shipping_zones",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    states = table.Column<string>(type: "jsonb", nullable: false),
                    pincode_ranges = table.Column<string>(type: "jsonb", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_shipping_zones", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shipment_lines",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_weight_grams = table.Column<int>(type: "integer", nullable: false),
                    declared_value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_lines", x => x.id);
                    table.CheckConstraint("ck_shipment_lines_quantity", "quantity > 0 AND unit_weight_grams >= 0 AND declared_value >= 0");
                    table.ForeignKey(
                        name: "fk_shipment_lines_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalSchema: "shipping",
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_courier_events_tenant_id",
                schema: "shipping",
                table: "courier_events",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_courier_events_tenant_id_awb",
                schema: "shipping",
                table: "courier_events",
                columns: new[] { "tenant_id", "awb" });

            migrationBuilder.CreateIndex(
                name: "ix_courier_events_tenant_id_provider_provider_event_id",
                schema: "shipping",
                table: "courier_events",
                columns: new[] { "tenant_id", "provider", "provider_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_courier_events_tenant_id_status_next_attempt_at",
                schema: "shipping",
                table: "courier_events",
                columns: new[] { "tenant_id", "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_manifests_tenant_id",
                schema: "shipping",
                table: "manifests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_manifests_tenant_id_reference",
                schema: "shipping",
                table: "manifests",
                columns: new[] { "tenant_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_manifests_tenant_id_vendor_id_generated_at",
                schema: "shipping",
                table: "manifests",
                columns: new[] { "tenant_id", "vendor_id", "generated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_manifests_vendor_id",
                schema: "shipping",
                table: "manifests",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_ndr_records_tenant_id",
                schema: "shipping",
                table: "ndr_records",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ndr_records_tenant_id_shipment_id_attempt_number",
                schema: "shipping",
                table: "ndr_records",
                columns: new[] { "tenant_id", "shipment_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ndr_records_tenant_id_vendor_id_action_raised_at",
                schema: "shipping",
                table: "ndr_records",
                columns: new[] { "tenant_id", "vendor_id", "action", "raised_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ndr_records_vendor_id",
                schema: "shipping",
                table: "ndr_records",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_serviceability_cache_tenant_id",
                schema: "shipping",
                table: "serviceability_cache",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_serviceability_cache_tenant_id_pincode_courier",
                schema: "shipping",
                table: "serviceability_cache",
                columns: new[] { "tenant_id", "pincode", "courier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_serviceability_cache_tenant_id_refreshed_at",
                schema: "shipping",
                table: "serviceability_cache",
                columns: new[] { "tenant_id", "refreshed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_lines_shipment_id",
                schema: "shipping",
                table: "shipment_lines",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_lines_tenant_id",
                schema: "shipping",
                table: "shipment_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_lines_tenant_id_order_line_id",
                schema: "shipping",
                table: "shipment_lines",
                columns: new[] { "tenant_id", "order_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id",
                schema: "shipping",
                table: "shipments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_awb",
                schema: "shipping",
                table: "shipments",
                columns: new[] { "tenant_id", "awb" },
                unique: true,
                filter: "awb IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_last_tracked_at",
                schema: "shipping",
                table: "shipments",
                columns: new[] { "tenant_id", "last_tracked_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_order_id",
                schema: "shipping",
                table: "shipments",
                columns: new[] { "tenant_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_sub_order_id",
                schema: "shipping",
                table: "shipments",
                columns: new[] { "tenant_id", "sub_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_vendor_id_status",
                schema: "shipping",
                table: "shipments",
                columns: new[] { "tenant_id", "vendor_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_vendor_id",
                schema: "shipping",
                table: "shipments",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipping_rates_tenant_id",
                schema: "shipping",
                table: "shipping_rates",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipping_rates_tenant_id_vendor_id",
                schema: "shipping",
                table: "shipping_rates",
                columns: new[] { "tenant_id", "vendor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_shipping_rates_tenant_id_zone_id_method_is_active",
                schema: "shipping",
                table: "shipping_rates",
                columns: new[] { "tenant_id", "zone_id", "method", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_shipping_rates_vendor_id",
                schema: "shipping",
                table: "shipping_rates",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipping_zones_tenant_id",
                schema: "shipping",
                table: "shipping_zones",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipping_zones_tenant_id_code",
                schema: "shipping",
                table: "shipping_zones",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipping_zones_tenant_id_is_active_priority",
                schema: "shipping",
                table: "shipping_zones",
                columns: new[] { "tenant_id", "is_active", "priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "courier_events",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "manifests",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "ndr_records",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "serviceability_cache",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "shipment_lines",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "shipping_rates",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "shipping_zones",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "shipments",
                schema: "shipping");
        }
    }
}
