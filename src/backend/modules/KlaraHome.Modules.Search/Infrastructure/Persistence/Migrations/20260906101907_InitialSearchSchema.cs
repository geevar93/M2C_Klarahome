using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace KlaraHome.Modules.Search.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSearchSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "search");

            migrationBuilder.CreateTable(
                name: "product_search_projection",
                schema: "search",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    product_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    variant_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    product_slug = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: true),
                    brand_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    brand_slug = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    category_slug = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    category_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    category_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    vendor_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    vendor_slug = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    vendor_rating = table.Column<decimal>(type: "numeric(3,2)", nullable: true),
                    keywords = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    attributes = table.Column<string>(type: "jsonb", nullable: false),
                    attribute_meta = table.Column<string>(type: "jsonb", nullable: false),
                    mrp = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "INR"),
                    discount_percent = table.Column<int>(type: "integer", nullable: false),
                    rating_average = table.Column<decimal>(type: "numeric(3,2)", nullable: true),
                    rating_count = table.Column<int>(type: "integer", nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false),
                    quantity_available = table.Column<int>(type: "integer", nullable: false),
                    is_cod_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    is_returnable = table.Column<bool>(type: "boolean", nullable: false),
                    offer_count = table.Column<int>(type: "integer", nullable: false),
                    units_sold = table.Column<long>(type: "bigint", nullable: false),
                    popularity_score = table.Column<decimal>(type: "numeric(12,4)", nullable: false),
                    primary_image_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    indexed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "setweight(to_tsvector('english', coalesce(product_name, '')), 'A') ||\r\nsetweight(to_tsvector('english', coalesce(brand_name, '') || ' ' || coalesce(variant_name, '') || ' ' || coalesce(sku, '')), 'B') ||\r\nsetweight(to_tsvector('english', coalesce(category_name, '')), 'C') ||\r\nsetweight(to_tsvector('english', coalesce(keywords, '')), 'D')", stored: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_search_projection", x => x.id);
                    table.CheckConstraint("ck_product_search_projection_amounts", "mrp >= 0 AND price >= 0 AND mrp >= price");
                    table.CheckConstraint("ck_product_search_projection_counts", "rating_count >= 0 AND offer_count >= 0 AND quantity_available >= 0 AND units_sold >= 0");
                    table.CheckConstraint("ck_product_search_projection_discount", "discount_percent >= 0 AND discount_percent <= 100");
                    table.CheckConstraint("ck_product_search_projection_rating", "(rating_average IS NULL OR (rating_average >= 0 AND rating_average <= 5)) AND (vendor_rating IS NULL OR (vendor_rating >= 0 AND vendor_rating <= 5))");
                });

            migrationBuilder.CreateTable(
                name: "search_stop_words",
                schema: "search",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    word = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                    table.PrimaryKey("pk_search_stop_words", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "search_synonyms",
                schema: "search",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    term = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expansions = table.Column<List<string>>(type: "text[]", nullable: false),
                    is_bidirectional = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_search_synonyms", x => x.id);
                    table.CheckConstraint("ck_search_synonyms_expansions", "cardinality(expansions) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_search_projection_attributes",
                schema: "search",
                table: "product_search_projection",
                column: "attributes")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_product_search_projection_category_ids",
                schema: "search",
                table: "product_search_projection",
                column: "category_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_product_search_projection_product_name_trgm",
                schema: "search",
                table: "product_search_projection",
                column: "product_name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_product_search_projection_search_vector",
                schema: "search",
                table: "product_search_projection",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_product_search_projection_stale",
                schema: "search",
                table: "product_search_projection",
                columns: new[] { "tenant_id", "indexed_at" },
                filter: "is_active = true");

            migrationBuilder.CreateIndex(
                name: "ix_product_search_projection_tenant_id",
                schema: "search",
                table: "product_search_projection",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_search_projection_tenant_id_brand_id",
                schema: "search",
                table: "product_search_projection",
                columns: new[] { "tenant_id", "brand_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_search_projection_tenant_id_category_path_price",
                schema: "search",
                table: "product_search_projection",
                columns: new[] { "tenant_id", "category_path", "price" });

            migrationBuilder.CreateIndex(
                name: "ix_product_search_projection_tenant_id_is_active_popularity_sc",
                schema: "search",
                table: "product_search_projection",
                columns: new[] { "tenant_id", "is_active", "popularity_score" });

            migrationBuilder.CreateIndex(
                name: "ix_product_search_projection_tenant_id_listing_id",
                schema: "search",
                table: "product_search_projection",
                columns: new[] { "tenant_id", "listing_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_search_projection_tenant_id_variant_id",
                schema: "search",
                table: "product_search_projection",
                columns: new[] { "tenant_id", "variant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_search_projection_tenant_id_vendor_id",
                schema: "search",
                table: "product_search_projection",
                columns: new[] { "tenant_id", "vendor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_search_stop_words_tenant_id",
                schema: "search",
                table: "search_stop_words",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_search_stop_words_tenant_id_word",
                schema: "search",
                table: "search_stop_words",
                columns: new[] { "tenant_id", "word" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_search_synonyms_tenant_id",
                schema: "search",
                table: "search_synonyms",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_search_synonyms_tenant_id_term",
                schema: "search",
                table: "search_synonyms",
                columns: new[] { "tenant_id", "term" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_search_projection",
                schema: "search");

            migrationBuilder.DropTable(
                name: "search_stop_words",
                schema: "search");

            migrationBuilder.DropTable(
                name: "search_synonyms",
                schema: "search");
        }
    }
}
