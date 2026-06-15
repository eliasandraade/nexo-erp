using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserVerificationToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "verification_token",
                schema: "nexo",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "verification_token_expiry",
                schema: "nexo",
                table: "users",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_verification_token",
                schema: "nexo",
                table: "users",
                column: "verification_token",
                unique: true,
                filter: "verification_token IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_verification_token",
                schema: "nexo",
                table: "users");

            migrationBuilder.DropColumn(
                name: "verification_token",
                schema: "nexo",
                table: "users");

            migrationBuilder.DropColumn(
                name: "verification_token_expiry",
                schema: "nexo",
                table: "users");
        }
    }
}
