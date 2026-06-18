using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServicePublicBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_confirm_appointments",
                schema: "nexo",
                table: "svc_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "booking_days_ahead",
                schema: "nexo",
                table: "svc_settings",
                type: "integer",
                nullable: false,
                defaultValue: 14);

            migrationBuilder.AddColumn<int>(
                name: "min_lead_minutes",
                schema: "nexo",
                table: "svc_settings",
                type: "integer",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.AddColumn<bool>(
                name: "public_booking_enabled",
                schema: "nexo",
                table: "svc_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "show_prices",
                schema: "nexo",
                table: "svc_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "slot_interval_minutes",
                schema: "nexo",
                table: "svc_settings",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<string>(
                name: "time_zone_id",
                schema: "nexo",
                table: "svc_settings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "America/Sao_Paulo");

            migrationBuilder.AddColumn<string>(
                name: "working_hours_json",
                schema: "nexo",
                table: "svc_professionals",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auto_confirm_appointments",
                schema: "nexo",
                table: "svc_settings");

            migrationBuilder.DropColumn(
                name: "booking_days_ahead",
                schema: "nexo",
                table: "svc_settings");

            migrationBuilder.DropColumn(
                name: "min_lead_minutes",
                schema: "nexo",
                table: "svc_settings");

            migrationBuilder.DropColumn(
                name: "public_booking_enabled",
                schema: "nexo",
                table: "svc_settings");

            migrationBuilder.DropColumn(
                name: "show_prices",
                schema: "nexo",
                table: "svc_settings");

            migrationBuilder.DropColumn(
                name: "slot_interval_minutes",
                schema: "nexo",
                table: "svc_settings");

            migrationBuilder.DropColumn(
                name: "time_zone_id",
                schema: "nexo",
                table: "svc_settings");

            migrationBuilder.DropColumn(
                name: "working_hours_json",
                schema: "nexo",
                table: "svc_professionals");
        }
    }
}
