using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Content.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialContentSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "content");

            migrationBuilder.CreateTable(
                name: "banners",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    placement = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    media_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mobile_media_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    alt_text = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    link = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    cta_label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    audience = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_banners", x => x.id);
                    table.CheckConstraint("ck_banners_audience", "audience BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_banners_content", "(placement = 'AnnouncementBar' AND message IS NOT NULL) OR (placement <> 'AnnouncementBar' AND media_file_id IS NOT NULL)");
                    table.CheckConstraint("ck_banners_placement", "placement IN ('AnnouncementBar', 'HomeHero', 'HomeStrip', 'CategoryHeader', 'ListingSidebar', 'ProductStrip', 'CartStrip')");
                    table.CheckConstraint("ck_banners_window", "starts_at IS NULL OR ends_at IS NULL OR ends_at > starts_at");
                });

            migrationBuilder.CreateTable(
                name: "collection_items",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    is_pinned = table.Column<bool>(type: "boolean", nullable: false),
                    is_from_rule = table.Column<bool>(type: "boolean", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_items", x => x.id);
                    table.CheckConstraint("ck_collection_items_position", "position >= 0");
                });

            migrationBuilder.CreateTable(
                name: "collections",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rules = table.Column<string>(type: "jsonb", nullable: false),
                    hero_image_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_listed = table.Column<bool>(type: "boolean", nullable: false),
                    item_count = table.Column<int>(type: "integer", nullable: false),
                    refreshed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    content_changed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
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
                    table.PrimaryKey("pk_collections", x => x.id);
                    table.CheckConstraint("ck_collections_counts", "item_count >= 0");
                    table.CheckConstraint("ck_collections_type", "type IN ('Manual', 'Rule')");
                });

            migrationBuilder.CreateTable(
                name: "menus",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    placement = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
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
                    table.PrimaryKey("pk_menus", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "page_versions",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    blocks = table.Column<string>(type: "jsonb", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    restored_from = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seo = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_page_versions", x => x.id);
                    table.CheckConstraint("ck_page_versions_version", "version >= 1");
                });

            migrationBuilder.CreateTable(
                name: "pages",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    content_changed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    cover_image_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    author = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false),
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
                    table.PrimaryKey("pk_pages", x => x.id);
                    table.CheckConstraint("ck_pages_schedule", "(status = 'Scheduled' AND scheduled_at IS NOT NULL) OR (status <> 'Scheduled' AND scheduled_at IS NULL)");
                    table.CheckConstraint("ck_pages_status", "status IN ('Draft', 'InReview', 'Scheduled', 'Published', 'Unpublished', 'Archived')");
                    table.CheckConstraint("ck_pages_type", "type IN ('Home', 'Landing', 'Static', 'Legal', 'Blog')");
                    table.CheckConstraint("ck_pages_version", "version >= 1");
                });

            migrationBuilder.CreateTable(
                name: "redirects",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    to_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    hit_count = table.Column<long>(type: "bigint", nullable: false),
                    last_hit_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
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
                    table.PrimaryKey("pk_redirects", x => x.id);
                    table.CheckConstraint("ck_redirects_hit_count", "hit_count >= 0");
                    table.CheckConstraint("ck_redirects_status_code", "status_code IN (301, 302, 410)");
                    table.CheckConstraint("ck_redirects_target", "(status_code = 410 AND to_path IS NULL) OR (status_code <> 410 AND to_path IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "menu_items",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    link_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    is_visible = table.Column<bool>(type: "boolean", nullable: false),
                    opens_in_new_tab = table.Column<bool>(type: "boolean", nullable: false),
                    icon_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    badge = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_items", x => x.id);
                    table.CheckConstraint("ck_menu_items_depth", "depth >= 0 AND depth < 3");
                    table.CheckConstraint("ck_menu_items_link_target", "(link_type IN ('Page', 'Category', 'Collection') AND target_id IS NOT NULL) OR (link_type = 'Url' AND url IS NOT NULL) OR (link_type = 'None' AND target_id IS NULL AND url IS NULL)");
                    table.CheckConstraint("ck_menu_items_link_type", "link_type IN ('None', 'Page', 'Category', 'Collection', 'Url')");
                    table.CheckConstraint("ck_menu_items_position", "position >= 0");
                    table.ForeignKey(
                        name: "fk_menu_items_menus_menu_id",
                        column: x => x.menu_id,
                        principalSchema: "content",
                        principalTable: "menus",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "page_blocks",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    block_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    config = table.Column<string>(type: "jsonb", nullable: false),
                    is_visible = table.Column<bool>(type: "boolean", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_page_blocks", x => x.id);
                    table.CheckConstraint("ck_page_blocks_position", "position >= 0");
                    table.CheckConstraint("ck_page_blocks_type", "block_type IN ('Hero', 'BannerGrid', 'ProductCarousel', 'CategoryTiles', 'RichText', 'Faq', 'Testimonial', 'CustomHtml')");
                    table.CheckConstraint("ck_page_blocks_window", "starts_at IS NULL OR ends_at IS NULL OR ends_at > starts_at");
                    table.ForeignKey(
                        name: "fk_page_blocks_pages_page_id",
                        column: x => x.page_id,
                        principalSchema: "content",
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_banners_tenant_id",
                schema: "content",
                table: "banners",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_banners_tenant_id_placement_priority",
                schema: "content",
                table: "banners",
                columns: new[] { "tenant_id", "placement", "priority" },
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_collection_items_collection_id_position",
                schema: "content",
                table: "collection_items",
                columns: new[] { "collection_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_collection_items_collection_id_product_id",
                schema: "content",
                table: "collection_items",
                columns: new[] { "collection_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_collection_items_tenant_id",
                schema: "content",
                table: "collection_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_collection_items_tenant_id_product_id",
                schema: "content",
                table: "collection_items",
                columns: new[] { "tenant_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_collections_stale",
                schema: "content",
                table: "collections",
                columns: new[] { "type", "refreshed_at" },
                filter: "type = 'Rule' AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_collections_tenant_id",
                schema: "content",
                table: "collections",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_collections_tenant_id_slug",
                schema: "content",
                table: "collections",
                columns: new[] { "tenant_id", "slug" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_menu_items_menu_id_parent_id_position",
                schema: "content",
                table: "menu_items",
                columns: new[] { "menu_id", "parent_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_menu_items_tenant_id",
                schema: "content",
                table: "menu_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_menus_tenant_id",
                schema: "content",
                table: "menus",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_menus_tenant_id_code",
                schema: "content",
                table: "menus",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_page_blocks_page_id_position",
                schema: "content",
                table: "page_blocks",
                columns: new[] { "page_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_page_blocks_tenant_id",
                schema: "content",
                table: "page_blocks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_page_versions_page_id_version",
                schema: "content",
                table: "page_versions",
                columns: new[] { "page_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_page_versions_tenant_id",
                schema: "content",
                table: "page_versions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_pages_due",
                schema: "content",
                table: "pages",
                columns: new[] { "status", "scheduled_at" },
                filter: "status = 'Scheduled'");

            migrationBuilder.CreateIndex(
                name: "ix_pages_tenant_id",
                schema: "content",
                table: "pages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_pages_tenant_id_slug",
                schema: "content",
                table: "pages",
                columns: new[] { "tenant_id", "slug" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pages_tenant_id_status_slug",
                schema: "content",
                table: "pages",
                columns: new[] { "tenant_id", "status", "slug" });

            migrationBuilder.CreateIndex(
                name: "ix_pages_tenant_id_type_published_at",
                schema: "content",
                table: "pages",
                columns: new[] { "tenant_id", "type", "published_at" });

            migrationBuilder.CreateIndex(
                name: "ux_pages_single_published_home",
                schema: "content",
                table: "pages",
                columns: new[] { "tenant_id", "type" },
                unique: true,
                filter: "type = 'Home' AND status = 'Published' AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_redirects_tenant_id",
                schema: "content",
                table: "redirects",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_redirects_tenant_id_from_path",
                schema: "content",
                table: "redirects",
                columns: new[] { "tenant_id", "from_path" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "banners",
                schema: "content");

            migrationBuilder.DropTable(
                name: "collection_items",
                schema: "content");

            migrationBuilder.DropTable(
                name: "collections",
                schema: "content");

            migrationBuilder.DropTable(
                name: "menu_items",
                schema: "content");

            migrationBuilder.DropTable(
                name: "page_blocks",
                schema: "content");

            migrationBuilder.DropTable(
                name: "page_versions",
                schema: "content");

            migrationBuilder.DropTable(
                name: "redirects",
                schema: "content");

            migrationBuilder.DropTable(
                name: "menus",
                schema: "content");

            migrationBuilder.DropTable(
                name: "pages",
                schema: "content");
        }
    }
}
