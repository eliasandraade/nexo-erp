using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuthPasswordReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "password_reset_expires_at",
                schema: "nexo",
                table: "users",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "password_reset_token_hash",
                schema: "nexo",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_users_password_reset_token_hash",
                schema: "nexo",
                table: "users",
                column: "password_reset_token_hash",
                unique: true,
                filter: "password_reset_token_hash IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_users_password_reset_token_hash",
                schema: "nexo",
                table: "users");

            migrationBuilder.DropColumn(
                name: "password_reset_expires_at",
                schema: "nexo",
                table: "users");

            migrationBuilder.DropColumn(
                name: "password_reset_token_hash",
                schema: "nexo",
                table: "users");
        }
    }
}
