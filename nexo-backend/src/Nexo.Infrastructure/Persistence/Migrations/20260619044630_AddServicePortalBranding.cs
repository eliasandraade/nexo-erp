using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServicePortalBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address",
                schema: "nexo",
                table: "svc_settings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "brand_color",
                schema: "nexo",
                table: "svc_settings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cover_image_url",
                schema: "nexo",
                table: "svc_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "nexo",
                table: "svc_settings",
                type: "character varying(280)",
                maxLength: 280,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "display_name",
                schema: "nexo",
                table: "svc_settings",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "logo_url",
                schema: "nexo",
                table: "svc_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "whatsapp",
                schema: "nexo",
                table: "svc_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "address",
                schema: "nexo",
                table: "svc_settings");

            migrationBuilder.DropColumn(
                name: "brand_color",
                schema: "nexo",
                table: "svc_settings");

            migrationBuilder.DropColumn(
                name: "cover_image_url",
                schema: "nexo",
                table: "svc_settings");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "nexo",
                table: "svc_settings");

            migrationBuilder.DropColumn(
                name: "display_name",
                schema: "nexo",
                table: "svc_settings");

            migrationBuilder.DropColumn(
                name: "logo_url",
                schema: "nexo",
                table: "svc_settings");

            migrationBuilder.DropColumn(
                name: "whatsapp",
                schema: "nexo",
                table: "svc_settings");
        }
    }
}
