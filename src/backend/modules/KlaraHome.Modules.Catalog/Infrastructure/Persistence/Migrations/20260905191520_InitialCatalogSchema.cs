using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace KlaraHome.Modules.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalogSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateSequence(
                name: "sku_seq",
                schema: "catalog");

            migrationBuilder.CreateTable(
                name: "attribute_options",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    label = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    swatch_hex = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attribute_options", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "attribute_set_members",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attribute_set_members", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "attribute_sets",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attribute_sets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "attributes",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    data_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_variant_defining = table.Column<bool>(type: "boolean", nullable: false),
                    is_filterable = table.Column<bool>(type: "boolean", nullable: false),
                    is_searchable = table.Column<bool>(type: "boolean", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attributes", x => x.id);
                    table.CheckConstraint("ck_attributes_data_type", "data_type IN ('Text', 'Number', 'Boolean', 'Select', 'MultiSelect', 'Date')");
                    table.CheckConstraint("ck_attributes_variant_defining", "is_variant_defining = false OR data_type IN ('Select', 'MultiSelect')");
                });

            migrationBuilder.CreateTable(
                name: "brands",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    slug = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    logo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    seo = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brands", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "catalog_jobs",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    source_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    result_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    processed_rows = table.Column<int>(type: "integer", nullable: false),
                    succeeded_rows = table.Column<int>(type: "integer", nullable: false),
                    failed_rows = table.Column<int>(type: "integer", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    errors = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_jobs", x => x.id);
                    table.CheckConstraint("ck_catalog_jobs_counts", "total_rows >= 0 AND processed_rows >= 0 AND succeeded_rows >= 0 AND failed_rows >= 0");
                    table.CheckConstraint("ck_catalog_jobs_kind", "kind IN ('ProductImport', 'ProductExport')");
                    table.CheckConstraint("ck_catalog_jobs_status", "status IN ('Queued', 'Running', 'Succeeded', 'PartiallySucceeded', 'Failed')");
                });

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    slug = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    path = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    image_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attribute_set_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    seo = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.CheckConstraint("ck_categories_level", "level >= 0 AND level < 6");
                    table.CheckConstraint("ck_categories_parent", "(level = 0 AND parent_id IS NULL) OR (level > 0 AND parent_id IS NOT NULL)");
                    table.CheckConstraint("ck_categories_position", "position >= 0");
                });

            migrationBuilder.CreateTable(
                name: "listings",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    vendor_sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    handling_time_hours = table.Column<int>(type: "integer", nullable: false),
                    is_cod_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    max_order_quantity = table.Column<int>(type: "integer", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    mrp_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    mrp_currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    selling_price_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    selling_price_currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listings", x => x.id);
                    table.CheckConstraint("ck_listings_handling_time", "handling_time_hours > 0 AND handling_time_hours <= 168");
                    table.CheckConstraint("ck_listings_max_order_quantity", "max_order_quantity IS NULL OR max_order_quantity > 0");
                    table.CheckConstraint("ck_listings_price_below_mrp", "selling_price_amount <= mrp_amount");
                    table.CheckConstraint("ck_listings_prices_positive", "mrp_amount >= 0 AND selling_price_amount >= 0");
                    table.CheckConstraint("ck_listings_status", "status IN ('Draft', 'Active', 'Inactive', 'Archived')");
                    table.CheckConstraint("ck_listings_vendor_present", "vendor_id IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "media_assets",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    alt_text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_assets", x => x.id);
                    table.CheckConstraint("ck_media_assets_kind", "kind IN ('Image', 'Video', 'Document')");
                    table.CheckConstraint("ck_media_assets_owner", "product_id IS NOT NULL OR variant_id IS NOT NULL");
                    table.CheckConstraint("ck_media_assets_position", "position >= 0");
                });

            migrationBuilder.CreateTable(
                name: "product_attribute_values",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value_text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    value_number = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    value_boolean = table.Column<bool>(type: "boolean", nullable: true),
                    value_date = table.Column<DateOnly>(type: "date", nullable: true),
                    value_option_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_attribute_values", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "product_moderations",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reviewer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_moderations", x => x.id);
                    table.CheckConstraint("ck_product_moderations_rejection", "status <> 'Rejected' OR notes IS NOT NULL");
                    table.CheckConstraint("ck_product_moderations_reviewed", "(status = 'Pending') = (reviewed_at IS NULL)");
                    table.CheckConstraint("ck_product_moderations_status", "status IN ('Pending', 'Approved', 'Rejected', 'Withdrawn')");
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    slug = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    short_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hsn_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    gst_rate = table.Column<decimal>(type: "numeric(7,4)", nullable: false),
                    country_of_origin = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: true),
                    is_returnable = table.Column<bool>(type: "boolean", nullable: false),
                    return_window_days = table.Column<int>(type: "integer", nullable: true),
                    warranty = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    rating_average = table.Column<decimal>(type: "numeric(3,2)", nullable: true),
                    rating_count = table.Column<int>(type: "integer", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('english', coalesce(name, '') || ' ' || coalesce(short_description, ''))", stored: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    importer = table.Column<string>(type: "jsonb", nullable: false),
                    manufacturer = table.Column<string>(type: "jsonb", nullable: false),
                    packer = table.Column<string>(type: "jsonb", nullable: false),
                    seo = table.Column<string>(type: "jsonb", nullable: false),
                    specifications = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.CheckConstraint("ck_products_country_of_origin", "country_of_origin IS NULL OR char_length(country_of_origin) = 2");
                    table.CheckConstraint("ck_products_gst_rate", "gst_rate >= 0 AND gst_rate <= 100");
                    table.CheckConstraint("ck_products_hsn", "hsn_code IS NULL OR char_length(hsn_code) IN (4, 6, 8)");
                    table.CheckConstraint("ck_products_rating", "rating_average IS NULL OR (rating_average >= 0 AND rating_average <= 5)");
                    table.CheckConstraint("ck_products_rating_count", "rating_count >= 0");
                    table.CheckConstraint("ck_products_status", "status IN ('Draft', 'PendingApproval', 'Active', 'Inactive', 'Archived')");
                });

            migrationBuilder.CreateTable(
                name: "variant_attribute_values",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    option_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_variant_attribute_values", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "variants",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    name_suffix = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    weight_grams = table.Column<int>(type: "integer", nullable: false),
                    length_mm = table.Column<int>(type: "integer", nullable: false),
                    width_mm = table.Column<int>(type: "integer", nullable: false),
                    height_mm = table.Column<int>(type: "integer", nullable: false),
                    net_quantity = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    shelf_life_days = table.Column<int>(type: "integer", nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    attribute_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    mrp_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    mrp_currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_variants", x => x.id);
                    table.CheckConstraint("ck_variants_dimensions", "weight_grams >= 0 AND length_mm >= 0 AND width_mm >= 0 AND height_mm >= 0");
                    table.CheckConstraint("ck_variants_mrp", "mrp_amount >= 0");
                    table.CheckConstraint("ck_variants_shelf_life", "shelf_life_days IS NULL OR shelf_life_days > 0");
                    table.CheckConstraint("ck_variants_status", "status IN ('Draft', 'Active', 'Inactive', 'Archived')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_attribute_options_tenant_id",
                schema: "catalog",
                table: "attribute_options",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_attribute_options_tenant_id_attribute_id_position",
                schema: "catalog",
                table: "attribute_options",
                columns: new[] { "tenant_id", "attribute_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_attribute_options_tenant_id_attribute_id_value",
                schema: "catalog",
                table: "attribute_options",
                columns: new[] { "tenant_id", "attribute_id", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attribute_set_members_tenant_id",
                schema: "catalog",
                table: "attribute_set_members",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_attribute_set_members_tenant_id_attribute_set_id_attribute_",
                schema: "catalog",
                table: "attribute_set_members",
                columns: new[] { "tenant_id", "attribute_set_id", "attribute_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attribute_set_members_tenant_id_attribute_set_id_position",
                schema: "catalog",
                table: "attribute_set_members",
                columns: new[] { "tenant_id", "attribute_set_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_attribute_sets_tenant_id",
                schema: "catalog",
                table: "attribute_sets",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_attribute_sets_tenant_id_code",
                schema: "catalog",
                table: "attribute_sets",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attributes_tenant_id",
                schema: "catalog",
                table: "attributes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_attributes_tenant_id_code",
                schema: "catalog",
                table: "attributes",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attributes_tenant_id_is_filterable_position",
                schema: "catalog",
                table: "attributes",
                columns: new[] { "tenant_id", "is_filterable", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_brands_tenant_id",
                schema: "catalog",
                table: "brands",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_brands_tenant_id_name",
                schema: "catalog",
                table: "brands",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_brands_tenant_id_slug",
                schema: "catalog",
                table: "brands",
                columns: new[] { "tenant_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_catalog_jobs_status_created_at",
                schema: "catalog",
                table: "catalog_jobs",
                columns: new[] { "status", "created_at" },
                filter: "status = 'Queued'");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_jobs_tenant_id",
                schema: "catalog",
                table: "catalog_jobs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_jobs_tenant_id_vendor_id_created_at",
                schema: "catalog",
                table: "catalog_jobs",
                columns: new[] { "tenant_id", "vendor_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_jobs_vendor_id",
                schema: "catalog",
                table: "catalog_jobs",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_categories_tenant_id",
                schema: "catalog",
                table: "categories",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_categories_tenant_id_parent_id_position",
                schema: "catalog",
                table: "categories",
                columns: new[] { "tenant_id", "parent_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_categories_tenant_id_path",
                schema: "catalog",
                table: "categories",
                columns: new[] { "tenant_id", "path" });

            migrationBuilder.CreateIndex(
                name: "ix_categories_tenant_id_slug",
                schema: "catalog",
                table: "categories",
                columns: new[] { "tenant_id", "slug" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_listings_tenant_id",
                schema: "catalog",
                table: "listings",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_listings_tenant_id_product_id",
                schema: "catalog",
                table: "listings",
                columns: new[] { "tenant_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_listings_tenant_id_variant_id_status",
                schema: "catalog",
                table: "listings",
                columns: new[] { "tenant_id", "variant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_listings_tenant_id_vendor_id_status",
                schema: "catalog",
                table: "listings",
                columns: new[] { "tenant_id", "vendor_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_listings_tenant_id_vendor_id_variant_id",
                schema: "catalog",
                table: "listings",
                columns: new[] { "tenant_id", "vendor_id", "variant_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_listings_vendor_id",
                schema: "catalog",
                table: "listings",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_tenant_id",
                schema: "catalog",
                table: "media_assets",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_tenant_id_product_id_position",
                schema: "catalog",
                table: "media_assets",
                columns: new[] { "tenant_id", "product_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_tenant_id_product_id_variant_id_file_id",
                schema: "catalog",
                table: "media_assets",
                columns: new[] { "tenant_id", "product_id", "variant_id", "file_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_tenant_id_variant_id_position",
                schema: "catalog",
                table: "media_assets",
                columns: new[] { "tenant_id", "variant_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_product_attribute_values_tenant_id",
                schema: "catalog",
                table: "product_attribute_values",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_attribute_values_tenant_id_attribute_id_value_optio",
                schema: "catalog",
                table: "product_attribute_values",
                columns: new[] { "tenant_id", "attribute_id", "value_option_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_attribute_values_tenant_id_product_id_attribute_id_",
                schema: "catalog",
                table: "product_attribute_values",
                columns: new[] { "tenant_id", "product_id", "attribute_id", "value_option_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_moderations_tenant_id",
                schema: "catalog",
                table: "product_moderations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_moderations_tenant_id_product_id",
                schema: "catalog",
                table: "product_moderations",
                columns: new[] { "tenant_id", "product_id" },
                unique: true,
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_product_moderations_tenant_id_product_id_submitted_at",
                schema: "catalog",
                table: "product_moderations",
                columns: new[] { "tenant_id", "product_id", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_product_moderations_tenant_id_status_submitted_at",
                schema: "catalog",
                table: "product_moderations",
                columns: new[] { "tenant_id", "status", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_product_moderations_vendor_id",
                schema: "catalog",
                table: "product_moderations",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_search_vector",
                schema: "catalog",
                table: "products",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id",
                schema: "catalog",
                table: "products",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id_brand_id",
                schema: "catalog",
                table: "products",
                columns: new[] { "tenant_id", "brand_id" });

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id_category_id_status",
                schema: "catalog",
                table: "products",
                columns: new[] { "tenant_id", "category_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id_slug",
                schema: "catalog",
                table: "products",
                columns: new[] { "tenant_id", "slug" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id_vendor_id_status",
                schema: "catalog",
                table: "products",
                columns: new[] { "tenant_id", "vendor_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_products_vendor_id",
                schema: "catalog",
                table: "products",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_variant_attribute_values_tenant_id",
                schema: "catalog",
                table: "variant_attribute_values",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_variant_attribute_values_tenant_id_attribute_id_option_id",
                schema: "catalog",
                table: "variant_attribute_values",
                columns: new[] { "tenant_id", "attribute_id", "option_id" });

            migrationBuilder.CreateIndex(
                name: "ix_variant_attribute_values_tenant_id_variant_id_attribute_id",
                schema: "catalog",
                table: "variant_attribute_values",
                columns: new[] { "tenant_id", "variant_id", "attribute_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_variants_barcode",
                schema: "catalog",
                table: "variants",
                column: "barcode",
                filter: "barcode IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_variants_tenant_id",
                schema: "catalog",
                table: "variants",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_variants_tenant_id_product_id_attribute_hash",
                schema: "catalog",
                table: "variants",
                columns: new[] { "tenant_id", "product_id", "attribute_hash" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_variants_tenant_id_product_id_position",
                schema: "catalog",
                table: "variants",
                columns: new[] { "tenant_id", "product_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_variants_tenant_id_sku",
                schema: "catalog",
                table: "variants",
                columns: new[] { "tenant_id", "sku" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attribute_options",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "attribute_set_members",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "attribute_sets",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "attributes",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "brands",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "catalog_jobs",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "listings",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "media_assets",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_attribute_values",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_moderations",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "products",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "variant_attribute_values",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "variants",
                schema: "catalog");

            migrationBuilder.DropSequence(
                name: "sku_seq",
                schema: "catalog");
        }
    }
}
