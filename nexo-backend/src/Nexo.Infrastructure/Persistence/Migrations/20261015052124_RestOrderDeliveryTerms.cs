using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RestOrderDeliveryTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "delivery_fee_amount",
                schema: "nexo",
                table: "rest_orders",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "discount_amount",
                schema: "nexo",
                table: "rest_orders",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "from_delivery_order",
                schema: "nexo",
                table: "rest_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Comandas accepted from a delivery order and not paid yet get the quoted terms now, so
            // their payment charges the quote. Paid ones stay as they were (their sale is closed).
            migrationBuilder.Sql(@"
UPDATE nexo.rest_orders o
   SET from_delivery_order = true,
       delivery_fee_amount = d.delivery_fee,
       discount_amount     = d.discount_amount
  FROM nexo.rest_delivery_orders d
 WHERE d.rest_order_id = o.id
   AND o.status NOT IN ('Paid', 'Cancelled');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "delivery_fee_amount",
                schema: "nexo",
                table: "rest_orders");

            migrationBuilder.DropColumn(
                name: "discount_amount",
                schema: "nexo",
                table: "rest_orders");

            migrationBuilder.DropColumn(
                name: "from_delivery_order",
                schema: "nexo",
                table: "rest_orders");
        }
    }
}
