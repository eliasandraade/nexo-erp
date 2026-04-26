using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase_A_Menu_Foundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "public_slug",
                schema: "nexo",
                table: "stores",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "image_url",
                schema: "nexo",
                table: "products",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_menu_visible",
                schema: "nexo",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "business_hours_json",
                schema: "nexo",
                table: "food_service_settings",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cover_image_url",
                schema: "nexo",
                table: "food_service_settings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "nexo",
                table: "food_service_settings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "display_name",
                schema: "nexo",
                table: "food_service_settings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "logo_url",
                schema: "nexo",
                table: "food_service_settings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "whatsapp_phone",
                schema: "nexo",
                table: "food_service_settings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                schema: "nexo",
                table: "categories",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_stores_public_slug",
                schema: "nexo",
                table: "stores",
                column: "public_slug",
                unique: true,
                filter: "public_slug IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stores_public_slug",
                schema: "nexo",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "public_slug",
                schema: "nexo",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "image_url",
                schema: "nexo",
                table: "products");

            migrationBuilder.DropColumn(
                name: "is_menu_visible",
                schema: "nexo",
                table: "products");

            migrationBuilder.DropColumn(
                name: "business_hours_json",
                schema: "nexo",
                table: "food_service_settings");

            migrationBuilder.DropColumn(
                name: "cover_image_url",
                schema: "nexo",
                table: "food_service_settings");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "nexo",
                table: "food_service_settings");

            migrationBuilder.DropColumn(
                name: "display_name",
                schema: "nexo",
                table: "food_service_settings");

            migrationBuilder.DropColumn(
                name: "logo_url",
                schema: "nexo",
                table: "food_service_settings");

            migrationBuilder.DropColumn(
                name: "whatsapp_phone",
                schema: "nexo",
                table: "food_service_settings");

            migrationBuilder.DropColumn(
                name: "sort_order",
                schema: "nexo",
                table: "categories");
        }
    }
}
