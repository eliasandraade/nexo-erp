using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveShadowFkRestOrderItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_rest_order_items_rest_orders_RestOrderId",
                schema: "nexo",
                table: "rest_order_items");

            migrationBuilder.DropIndex(
                name: "IX_rest_order_items_RestOrderId",
                schema: "nexo",
                table: "rest_order_items");

            migrationBuilder.DropColumn(
                name: "RestOrderId",
                schema: "nexo",
                table: "rest_order_items");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RestOrderId",
                schema: "nexo",
                table: "rest_order_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_rest_order_items_RestOrderId",
                schema: "nexo",
                table: "rest_order_items",
                column: "RestOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_rest_order_items_rest_orders_RestOrderId",
                schema: "nexo",
                table: "rest_order_items",
                column: "RestOrderId",
                principalSchema: "nexo",
                principalTable: "rest_orders",
                principalColumn: "id");
        }
    }
}
