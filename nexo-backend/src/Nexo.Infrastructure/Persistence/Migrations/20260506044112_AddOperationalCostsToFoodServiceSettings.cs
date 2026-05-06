using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalCostsToFoodServiceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cost_per_minute_gas",
                schema: "nexo",
                table: "food_service_settings",
                type: "numeric(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "cost_per_minute_labor",
                schema: "nexo",
                table: "food_service_settings",
                type: "numeric(18,4)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cost_per_minute_gas",
                schema: "nexo",
                table: "food_service_settings");

            migrationBuilder.DropColumn(
                name: "cost_per_minute_labor",
                schema: "nexo",
                table: "food_service_settings");
        }
    }
}
