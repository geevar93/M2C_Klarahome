using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Shipping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShiprocketServiceabilityDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "city",
                schema: "shipping",
                table: "serviceability_cache",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "state",
                schema: "shipping",
                table: "serviceability_cache",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "city",
                schema: "shipping",
                table: "serviceability_cache");

            migrationBuilder.DropColumn(
                name: "state",
                schema: "shipping",
                table: "serviceability_cache");
        }
    }
}
