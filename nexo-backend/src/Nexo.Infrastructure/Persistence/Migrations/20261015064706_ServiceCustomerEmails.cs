using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceCustomerEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "customer_emails_enabled",
                schema: "nexo",
                table: "svc_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "reminder_hours_before",
                schema: "nexo",
                table: "svc_settings",
                type: "integer",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<bool>(
                name: "confirmation_email_pending",
                schema: "nexo",
                table: "svc_appointments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "reminder_sent_at",
                schema: "nexo",
                table: "svc_appointments",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_svc_appointments_reminder_due",
                schema: "nexo",
                table: "svc_appointments",
                column: "starts_at",
                filter: "reminder_sent_at IS NULL AND public_token IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_svc_appointments_reminder_due",
                schema: "nexo",
                table: "svc_appointments");

            migrationBuilder.DropColumn(
                name: "customer_emails_enabled",
                schema: "nexo",
                table: "svc_settings");

            migrationBuilder.DropColumn(
                name: "reminder_hours_before",
                schema: "nexo",
                table: "svc_settings");

            migrationBuilder.DropColumn(
                name: "confirmation_email_pending",
                schema: "nexo",
                table: "svc_appointments");

            migrationBuilder.DropColumn(
                name: "reminder_sent_at",
                schema: "nexo",
                table: "svc_appointments");
        }
    }
}
