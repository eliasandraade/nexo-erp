using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlatformSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "mfa_enabled",
                schema: "nexo",
                table: "platform_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "mfa_last_used_step",
                schema: "nexo",
                table: "platform_users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mfa_pending_secret",
                schema: "nexo",
                table: "platform_users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mfa_recovery_codes",
                schema: "nexo",
                table: "platform_users",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mfa_secret",
                schema: "nexo",
                table: "platform_users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "security_stamp",
                schema: "nexo",
                table: "platform_users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Existing administrators get a random stamp (their old 8-hour tokens have no session and stop working anyway).
            migrationBuilder.Sql(@"UPDATE nexo.platform_users SET security_stamp = md5(random()::text || id::text) WHERE security_stamp = '';");

            migrationBuilder.AddColumn<string>(
                name: "impersonator_email",
                schema: "nexo",
                table: "audit_records",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "impersonator_id",
                schema: "nexo",
                table: "audit_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "platform_sessions",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    refresh_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    previous_refresh_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    rotated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    security_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    mfa_verified = table.Column<bool>(type: "boolean", nullable: false),
                    pending_mfa = table.Column<bool>(type: "boolean", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_sessions_user",
                        column: x => x.platform_user_id,
                        principalSchema: "nexo",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_platform_sessions_previous_hash",
                schema: "nexo",
                table: "platform_sessions",
                column: "previous_refresh_token_hash");

            migrationBuilder.CreateIndex(
                name: "ix_platform_sessions_user_expires",
                schema: "nexo",
                table: "platform_sessions",
                columns: new[] { "platform_user_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ux_platform_sessions_refresh_hash",
                schema: "nexo",
                table: "platform_sessions",
                column: "refresh_token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_sessions",
                schema: "nexo");

            migrationBuilder.DropColumn(
                name: "mfa_enabled",
                schema: "nexo",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "mfa_last_used_step",
                schema: "nexo",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "mfa_pending_secret",
                schema: "nexo",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "mfa_recovery_codes",
                schema: "nexo",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "mfa_secret",
                schema: "nexo",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "security_stamp",
                schema: "nexo",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "impersonator_email",
                schema: "nexo",
                table: "audit_records");

            migrationBuilder.DropColumn(
                name: "impersonator_id",
                schema: "nexo",
                table: "audit_records");
        }
    }
}
