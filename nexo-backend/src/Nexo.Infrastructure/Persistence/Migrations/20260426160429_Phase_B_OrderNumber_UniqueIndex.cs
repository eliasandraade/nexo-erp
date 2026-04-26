using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase_B_OrderNumber_UniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_rest_delivery_orders_store_number",
                schema: "nexo",
                table: "rest_delivery_orders");

            migrationBuilder.CreateIndex(
                name: "ix_rest_delivery_orders_store_number",
                schema: "nexo",
                table: "rest_delivery_orders",
                columns: new[] { "tenant_id", "store_id", "order_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_rest_delivery_orders_store_number",
                schema: "nexo",
                table: "rest_delivery_orders");

            migrationBuilder.CreateIndex(
                name: "ix_rest_delivery_orders_store_number",
                schema: "nexo",
                table: "rest_delivery_orders",
                columns: new[] { "tenant_id", "store_id", "order_number" });
        }
    }
}
