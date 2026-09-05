using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Media.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMediaSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "media");

            migrationBuilder.CreateTable(
                name: "files",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    visibility = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    checksum_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scan_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scanned_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    owner_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_files", x => x.id);
                    table.CheckConstraint("ck_files_byte_size", "byte_size > 0");
                    table.CheckConstraint("ck_files_dimensions", "(width IS NULL AND height IS NULL) OR (width > 0 AND height > 0)");
                    table.CheckConstraint("ck_files_scan_state", "scan_state IN ('Skipped', 'Pending', 'Clean', 'Infected')");
                    table.CheckConstraint("ck_files_status", "status IN ('Ready', 'Quarantined', 'Deleted')");
                    table.CheckConstraint("ck_files_visibility", "visibility IN ('Public', 'Private')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_files_tenant_created_at",
                schema: "media",
                table: "files",
                columns: new[] { "tenant_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_files_tenant_id",
                schema: "media",
                table: "files",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_files_tenant_id_owner_type_owner_id",
                schema: "media",
                table: "files",
                columns: new[] { "tenant_id", "owner_type", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_files_tenant_id_visibility_storage_key",
                schema: "media",
                table: "files",
                columns: new[] { "tenant_id", "visibility", "storage_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "files",
                schema: "media");
        }
    }
}
