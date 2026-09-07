using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Content.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Lets <c>content.pages.version</c> hold zero, which is what a page that has never been
    /// published is.
    /// </summary>
    /// <remarks>
    /// <c>pages.version</c> and <c>page_versions.version</c> are both called <c>version</c> and
    /// both were given <c>version &gt;= 1</c> from one shared constant, but they count different
    /// things: the second names a snapshot, and there is no snapshot zero; the first counts how
    /// many snapshots a page has taken, and a fresh draft has taken none.
    ///
    /// The effect was that <em>every</em> INSERT into <c>content.pages</c> failed on
    /// <c>ck_pages_version</c>, so no page could be created and the whole CMS — 51 endpoints, the
    /// storefront's home page, every landing page — was unusable from the day the schema landed.
    /// It went unseen because Step 20 proved nothing against a database.
    /// </remarks>
    public partial class AllowZeroPageVersionCounter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pages_version",
                schema: "content",
                table: "pages");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pages_version",
                schema: "content",
                table: "pages",
                sql: "version >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pages_version",
                schema: "content",
                table: "pages");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pages_version",
                schema: "content",
                table: "pages",
                sql: "version >= 1");
        }
    }
}
