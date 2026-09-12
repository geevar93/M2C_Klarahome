using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Content.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBannerMarquee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_marquee",
                schema: "content",
                table: "banners",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_marquee",
                schema: "content",
                table: "banners");
        }
    }
}
