using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase_C_PortalFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AcceptingOrders",
                schema: "nexo",
                table: "food_service_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DeliveryEnabled",
                schema: "nexo",
                table: "food_service_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TakeawayEnabled",
                schema: "nexo",
                table: "food_service_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptingOrders",
                schema: "nexo",
                table: "food_service_settings");

            migrationBuilder.DropColumn(
                name: "DeliveryEnabled",
                schema: "nexo",
                table: "food_service_settings");

            migrationBuilder.DropColumn(
                name: "TakeawayEnabled",
                schema: "nexo",
                table: "food_service_settings");
        }
    }
}
