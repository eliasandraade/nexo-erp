using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuthMfaTotp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "mfa_enabled",
                schema: "nexo",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "mfa_last_used_step",
                schema: "nexo",
                table: "users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mfa_pending_secret",
                schema: "nexo",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mfa_recovery_codes",
                schema: "nexo",
                table: "users",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mfa_secret",
                schema: "nexo",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "mfa_enabled",
                schema: "nexo",
                table: "users");

            migrationBuilder.DropColumn(
                name: "mfa_last_used_step",
                schema: "nexo",
                table: "users");

            migrationBuilder.DropColumn(
                name: "mfa_pending_secret",
                schema: "nexo",
                table: "users");

            migrationBuilder.DropColumn(
                name: "mfa_recovery_codes",
                schema: "nexo",
                table: "users");

            migrationBuilder.DropColumn(
                name: "mfa_secret",
                schema: "nexo",
                table: "users");
        }
    }
}
