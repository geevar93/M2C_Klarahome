using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Vendors.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialVendorsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "vendors");

            migrationBuilder.CreateSequence(
                name: "vendor_code_seq",
                schema: "vendors");

            migrationBuilder.CreateTable(
                name: "commission_plans",
                schema: "vendors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    plan_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    default_rate = table.Column<decimal>(type: "numeric(7,4)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    default_fixed_fee_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    default_fixed_fee_currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commission_plans", x => x.id);
                    table.CheckConstraint("ck_commission_plans_default_rate", "default_rate >= 0 AND default_rate <= 100");
                    table.CheckConstraint("ck_commission_plans_type", "plan_type IN ('Flat', 'Percentage', 'Tiered')");
                });

            migrationBuilder.CreateTable(
                name: "vendor_bank_accounts",
                schema: "vendors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    account_number_encrypted = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    account_number_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    ifsc = table.Column<string>(type: "character(11)", fixedLength: true, maxLength: 11, nullable: false),
                    bank_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    branch_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    verification_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    verification_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_bank_accounts", x => x.id);
                    table.CheckConstraint("ck_vendor_bank_accounts_ifsc", "char_length(ifsc) = 11 AND substring(ifsc from 5 for 1) = '0'");
                    table.CheckConstraint("ck_vendor_bank_accounts_last4", "char_length(account_number_last4) BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_vendor_bank_accounts_verification", "verification_status IN ('Unverified', 'Verified', 'Failed')");
                });

            migrationBuilder.CreateTable(
                name: "vendor_kyc_documents",
                schema: "vendors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number_masked = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_kyc_documents", x => x.id);
                    table.CheckConstraint("ck_vendor_kyc_documents_rejection", "status <> 'Rejected' OR rejection_reason IS NOT NULL");
                    table.CheckConstraint("ck_vendor_kyc_documents_status", "status IN ('Pending', 'Verified', 'Rejected')");
                    table.CheckConstraint("ck_vendor_kyc_documents_type", "document_type IN ('Pan', 'Gstin', 'CancelledCheque', 'AddressProof', 'IdentityProof', 'IncorporationCertificate')");
                });

            migrationBuilder.CreateTable(
                name: "vendor_pickup_locations",
                schema: "vendors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    landmark = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    state_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pincode = table.Column<string>(type: "character(6)", fixedLength: true, maxLength: 6, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    courier_location_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_pickup_locations", x => x.id);
                    table.CheckConstraint("ck_vendor_pickup_locations_pincode", "pincode ~ '^[1-9][0-9]{5}$'");
                });

            migrationBuilder.CreateTable(
                name: "vendor_serviceable_regions",
                schema: "vendors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    state_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pincode_prefix = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                    is_excluded = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_serviceable_regions", x => x.id);
                    table.CheckConstraint("ck_vendor_serviceable_regions_prefix", "pincode_prefix IS NULL OR pincode_prefix ~ '^[1-9][0-9]{1,5}$'");
                    table.CheckConstraint("ck_vendor_serviceable_regions_scope", "scope IN ('State', 'PincodePrefix')");
                    table.CheckConstraint("ck_vendor_serviceable_regions_subject", "(scope = 'State' AND state_id IS NOT NULL AND pincode_prefix IS NULL) OR (scope = 'PincodePrefix' AND pincode_prefix IS NOT NULL AND state_id IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "vendor_users",
                schema: "vendors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_owner = table.Column<bool>(type: "boolean", nullable: false),
                    job_title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vendors",
                schema: "vendors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    gstin = table.Column<string>(type: "character(15)", fixedLength: true, maxLength: 15, nullable: true),
                    pan = table.Column<string>(type: "character(10)", fixedLength: true, maxLength: 10, nullable: true),
                    business_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    support_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    support_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    commission_plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dispatch_sla_hours = table.Column<int>(type: "integer", nullable: false),
                    serves_all_india = table.Column<bool>(type: "boolean", nullable: false),
                    about = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    logo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    banner_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rating = table.Column<decimal>(type: "numeric(3,2)", nullable: true),
                    onboarded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    gateway_account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    registered_address = table.Column<string>(type: "jsonb", nullable: false),
                    return_policy = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendors", x => x.id);
                    table.CheckConstraint("ck_vendors_business_type", "business_type IN ('Individual', 'SoleProprietorship', 'Partnership', 'LimitedLiabilityPartnership', 'PrivateLimited', 'PublicLimited', 'HinduUndividedFamily', 'Trust')");
                    table.CheckConstraint("ck_vendors_dispatch_sla", "dispatch_sla_hours > 0 AND dispatch_sla_hours <= 168");
                    table.CheckConstraint("ck_vendors_gstin", "gstin IS NULL OR char_length(gstin) = 15");
                    table.CheckConstraint("ck_vendors_pan", "pan IS NULL OR char_length(pan) = 10");
                    table.CheckConstraint("ck_vendors_rating", "rating IS NULL OR (rating >= 0 AND rating <= 5)");
                    table.CheckConstraint("ck_vendors_status", "status IN ('Applied', 'UnderReview', 'Approved', 'Active', 'Suspended', 'Offboarded')");
                });

            migrationBuilder.CreateTable(
                name: "commission_plan_rules",
                schema: "vendors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    min_price = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    max_price = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    rate = table.Column<decimal>(type: "numeric(7,4)", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    fixed_fee_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    fixed_fee_currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commission_plan_rules", x => x.id);
                    table.CheckConstraint("ck_commission_plan_rules_band", "min_price IS NULL OR max_price IS NULL OR min_price < max_price");
                    table.CheckConstraint("ck_commission_plan_rules_prices", "(min_price IS NULL OR min_price >= 0) AND (max_price IS NULL OR max_price > 0)");
                    table.CheckConstraint("ck_commission_plan_rules_rate", "rate >= 0 AND rate <= 100");
                    table.ForeignKey(
                        name: "fk_commission_plan_rules_commission_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "vendors",
                        principalTable: "commission_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_commission_plan_rules_plan_id",
                schema: "vendors",
                table: "commission_plan_rules",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_commission_plan_rules_tenant_id",
                schema: "vendors",
                table: "commission_plan_rules",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_commission_plan_rules_tenant_id_plan_id",
                schema: "vendors",
                table: "commission_plan_rules",
                columns: new[] { "tenant_id", "plan_id" });

            migrationBuilder.CreateIndex(
                name: "ix_commission_plans_tenant_id_code",
                schema: "vendors",
                table: "commission_plans",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_commission_plans_default",
                schema: "vendors",
                table: "commission_plans",
                column: "tenant_id",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_bank_accounts_tenant_id",
                schema: "vendors",
                table: "vendor_bank_accounts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_bank_accounts_vendor_id",
                schema: "vendors",
                table: "vendor_bank_accounts",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ux_vendor_bank_accounts_primary",
                schema: "vendors",
                table: "vendor_bank_accounts",
                columns: new[] { "tenant_id", "vendor_id" },
                unique: true,
                filter: "is_primary");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_kyc_documents_tenant_id",
                schema: "vendors",
                table: "vendor_kyc_documents",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_kyc_documents_tenant_id_status",
                schema: "vendors",
                table: "vendor_kyc_documents",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_vendor_kyc_documents_tenant_id_vendor_id_document_type",
                schema: "vendors",
                table: "vendor_kyc_documents",
                columns: new[] { "tenant_id", "vendor_id", "document_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendor_kyc_documents_vendor_id",
                schema: "vendors",
                table: "vendor_kyc_documents",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_pickup_locations_tenant_id",
                schema: "vendors",
                table: "vendor_pickup_locations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_pickup_locations_tenant_id_vendor_id_pincode",
                schema: "vendors",
                table: "vendor_pickup_locations",
                columns: new[] { "tenant_id", "vendor_id", "pincode" });

            migrationBuilder.CreateIndex(
                name: "ix_vendor_pickup_locations_vendor_id",
                schema: "vendors",
                table: "vendor_pickup_locations",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ux_vendor_pickup_locations_default",
                schema: "vendors",
                table: "vendor_pickup_locations",
                columns: new[] { "tenant_id", "vendor_id" },
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_serviceable_regions_tenant_id",
                schema: "vendors",
                table: "vendor_serviceable_regions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_serviceable_regions_tenant_id_vendor_id",
                schema: "vendors",
                table: "vendor_serviceable_regions",
                columns: new[] { "tenant_id", "vendor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_vendor_serviceable_regions_vendor_id",
                schema: "vendors",
                table: "vendor_serviceable_regions",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_users_tenant_id",
                schema: "vendors",
                table: "vendor_users",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_users_tenant_id_user_id",
                schema: "vendors",
                table: "vendor_users",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_vendor_users_tenant_id_vendor_id_user_id",
                schema: "vendors",
                table: "vendor_users",
                columns: new[] { "tenant_id", "vendor_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendor_users_vendor_id",
                schema: "vendors",
                table: "vendor_users",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendors_tenant_id",
                schema: "vendors",
                table: "vendors",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendors_tenant_id_code",
                schema: "vendors",
                table: "vendors",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendors_tenant_id_commission_plan_id",
                schema: "vendors",
                table: "vendors",
                columns: new[] { "tenant_id", "commission_plan_id" });

            migrationBuilder.CreateIndex(
                name: "ix_vendors_tenant_id_slug",
                schema: "vendors",
                table: "vendors",
                columns: new[] { "tenant_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendors_tenant_id_status",
                schema: "vendors",
                table: "vendors",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commission_plan_rules",
                schema: "vendors");

            migrationBuilder.DropTable(
                name: "vendor_bank_accounts",
                schema: "vendors");

            migrationBuilder.DropTable(
                name: "vendor_kyc_documents",
                schema: "vendors");

            migrationBuilder.DropTable(
                name: "vendor_pickup_locations",
                schema: "vendors");

            migrationBuilder.DropTable(
                name: "vendor_serviceable_regions",
                schema: "vendors");

            migrationBuilder.DropTable(
                name: "vendor_users",
                schema: "vendors");

            migrationBuilder.DropTable(
                name: "vendors",
                schema: "vendors");

            migrationBuilder.DropTable(
                name: "commission_plans",
                schema: "vendors");

            migrationBuilder.DropSequence(
                name: "vendor_code_seq",
                schema: "vendors");
        }
    }
}
