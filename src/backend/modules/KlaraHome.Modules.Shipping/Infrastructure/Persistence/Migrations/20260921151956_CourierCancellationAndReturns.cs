using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Shipping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CourierCancellationAndReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "courier_cancellation_requested_at",
                schema: "shipping",
                table: "shipments",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "return_requested_at",
                schema: "shipping",
                table: "shipments",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipments_courier_cancellation_requested_at",
                schema: "shipping",
                table: "shipments",
                column: "courier_cancellation_requested_at",
                filter: "courier_cancellation_requested_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_return_requested_at",
                schema: "shipping",
                table: "shipments",
                columns: new[] { "tenant_id", "return_requested_at" },
                filter: "return_requested_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_shipments_courier_cancellation_requested_at",
                schema: "shipping",
                table: "shipments");

            migrationBuilder.DropIndex(
                name: "ix_shipments_tenant_id_return_requested_at",
                schema: "shipping",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "courier_cancellation_requested_at",
                schema: "shipping",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "return_requested_at",
                schema: "shipping",
                table: "shipments");
        }
    }
}
