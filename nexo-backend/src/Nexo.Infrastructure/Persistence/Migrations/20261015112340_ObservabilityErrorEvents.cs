using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ObservabilityErrorEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "error_events",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    exception_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    route = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    first_seen_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    last_request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stack_top = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_error_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_error_events_last_seen_at",
                schema: "nexo",
                table: "error_events",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "ux_error_events_fingerprint",
                schema: "nexo",
                table: "error_events",
                column: "fingerprint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "error_events",
                schema: "nexo");
        }
    }
}
