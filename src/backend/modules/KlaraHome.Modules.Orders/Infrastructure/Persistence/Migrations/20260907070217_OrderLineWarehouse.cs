using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderLineWarehouse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "warehouse_id",
                schema: "orders",
                table: "order_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_warehouse_id",
                schema: "orders",
                table: "order_lines",
                column: "warehouse_id",
                filter: "warehouse_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_order_lines_warehouse_id",
                schema: "orders",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "warehouse_id",
                schema: "orders",
                table: "order_lines");
        }
    }
}
