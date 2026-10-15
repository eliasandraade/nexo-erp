using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceSelfCancelAndUsageReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_svc_package_usages_appointment_id",
                schema: "nexo",
                table: "svc_package_usages");

            migrationBuilder.AddColumn<string>(
                name: "reversal_reason",
                schema: "nexo",
                table: "svc_package_usages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reversed_at",
                schema: "nexo",
                table: "svc_package_usages",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reversed_by_user_id",
                schema: "nexo",
                table: "svc_package_usages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "public_token",
                schema: "nexo",
                table: "svc_appointments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_svc_package_usages_appointment_id",
                schema: "nexo",
                table: "svc_package_usages",
                column: "appointment_id",
                unique: true,
                filter: "appointment_id IS NOT NULL AND reversed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_svc_appointments_public_token",
                schema: "nexo",
                table: "svc_appointments",
                column: "public_token",
                unique: true,
                filter: "public_token IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_svc_package_usages_appointment_id",
                schema: "nexo",
                table: "svc_package_usages");

            migrationBuilder.DropIndex(
                name: "ux_svc_appointments_public_token",
                schema: "nexo",
                table: "svc_appointments");

            migrationBuilder.DropColumn(
                name: "reversal_reason",
                schema: "nexo",
                table: "svc_package_usages");

            migrationBuilder.DropColumn(
                name: "reversed_at",
                schema: "nexo",
                table: "svc_package_usages");

            migrationBuilder.DropColumn(
                name: "reversed_by_user_id",
                schema: "nexo",
                table: "svc_package_usages");

            migrationBuilder.DropColumn(
                name: "public_token",
                schema: "nexo",
                table: "svc_appointments");

            migrationBuilder.CreateIndex(
                name: "ux_svc_package_usages_appointment_id",
                schema: "nexo",
                table: "svc_package_usages",
                column: "appointment_id",
                unique: true,
                filter: "appointment_id IS NOT NULL");
        }
    }
}
