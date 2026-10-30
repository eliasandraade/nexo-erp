using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BuildWave1Portal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "stage_id",
                schema: "nexo",
                table: "int_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "weight",
                schema: "nexo",
                table: "bld_stages",
                type: "numeric(12,4)",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "area_m2",
                schema: "nexo",
                table: "bld_projects",
                type: "numeric(14,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "latitude",
                schema: "nexo",
                table: "bld_projects",
                type: "numeric(9,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "longitude",
                schema: "nexo",
                table: "bld_projects",
                type: "numeric(9,6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tracking_mode",
                schema: "nexo",
                table: "bld_projects",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "client_summary",
                schema: "nexo",
                table: "bld_daily_logs",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "client_visible",
                schema: "nexo",
                table: "bld_daily_logs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "client_visible",
                schema: "nexo",
                table: "bld_daily_log_photos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "bld_client_updates",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    source = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_client_updates", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_client_updates_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_portal_companies",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    logo_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_portal_companies", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_portal_companies_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_progress_snapshots",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    planned_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    actual_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    forecast_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    forecast_delivery_date = table.Column<DateOnly>(type: "date", nullable: true),
                    stages = table.Column<string>(type: "json", nullable: false),
                    methodology = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_progress_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_progress_snapshots_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_project_portals",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    access = table.Column<int>(type: "integer", nullable: false),
                    password_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    password_version = table.Column<int>(type: "integer", nullable: false),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    welcome_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    logo_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cover_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    primary_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    accent_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contact_whatsapp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    visibility = table.Column<string>(type: "json", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_project_portals", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_project_portals_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_schedule_revisions",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    items = table.Column<string>(type: "json", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_schedule_revisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_schedule_revisions_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_stage_progress_events",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_percent = table.Column<int>(type: "integer", nullable: false),
                    percent = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    source_ref = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_stage_progress_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_stage_progress_events_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_portal_sessions",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    portal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    password_version = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_portal_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_portal_sessions_portal",
                        column: x => x.portal_id,
                        principalSchema: "nexo",
                        principalTable: "bld_project_portals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_int_movements_stage_id",
                schema: "nexo",
                table: "int_movements",
                column: "stage_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_client_updates_project_id",
                schema: "nexo",
                table: "bld_client_updates",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_client_updates_project_status",
                schema: "nexo",
                table: "bld_client_updates",
                columns: new[] { "tenant_id", "project_id", "status", "published_at" });

            migrationBuilder.CreateIndex(
                name: "uix_bld_portal_companies_slug",
                schema: "nexo",
                table: "bld_portal_companies",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uix_bld_portal_companies_tenant",
                schema: "nexo",
                table: "bld_portal_companies",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bld_portal_sessions_portal",
                schema: "nexo",
                table: "bld_portal_sessions",
                columns: new[] { "portal_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "uix_bld_portal_sessions_token_hash",
                schema: "nexo",
                table: "bld_portal_sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bld_progress_snapshots_tenant_id",
                schema: "nexo",
                table: "bld_progress_snapshots",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "uix_bld_progress_snapshots_project_date",
                schema: "nexo",
                table: "bld_progress_snapshots",
                columns: new[] { "project_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uix_bld_project_portals_project",
                schema: "nexo",
                table: "bld_project_portals",
                column: "project_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uix_bld_project_portals_tenant_slug",
                schema: "nexo",
                table: "bld_project_portals",
                columns: new[] { "tenant_id", "slug" },
                unique: true,
                filter: "slug IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_bld_schedule_revisions_project_id",
                schema: "nexo",
                table: "bld_schedule_revisions",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "uix_bld_schedule_revisions_project_number",
                schema: "nexo",
                table: "bld_schedule_revisions",
                columns: new[] { "tenant_id", "project_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bld_stage_progress_events_project",
                schema: "nexo",
                table: "bld_stage_progress_events",
                columns: new[] { "tenant_id", "project_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_stage_progress_events_project_id",
                schema: "nexo",
                table: "bld_stage_progress_events",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_stage_progress_events_stage",
                schema: "nexo",
                table: "bld_stage_progress_events",
                column: "stage_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bld_client_updates",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_portal_companies",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_portal_sessions",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_progress_snapshots",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_schedule_revisions",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_stage_progress_events",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_project_portals",
                schema: "nexo");

            migrationBuilder.DropIndex(
                name: "ix_int_movements_stage_id",
                schema: "nexo",
                table: "int_movements");

            migrationBuilder.DropColumn(
                name: "stage_id",
                schema: "nexo",
                table: "int_movements");

            migrationBuilder.DropColumn(
                name: "weight",
                schema: "nexo",
                table: "bld_stages");

            migrationBuilder.DropColumn(
                name: "area_m2",
                schema: "nexo",
                table: "bld_projects");

            migrationBuilder.DropColumn(
                name: "latitude",
                schema: "nexo",
                table: "bld_projects");

            migrationBuilder.DropColumn(
                name: "longitude",
                schema: "nexo",
                table: "bld_projects");

            migrationBuilder.DropColumn(
                name: "tracking_mode",
                schema: "nexo",
                table: "bld_projects");

            migrationBuilder.DropColumn(
                name: "client_summary",
                schema: "nexo",
                table: "bld_daily_logs");

            migrationBuilder.DropColumn(
                name: "client_visible",
                schema: "nexo",
                table: "bld_daily_logs");

            migrationBuilder.DropColumn(
                name: "client_visible",
                schema: "nexo",
                table: "bld_daily_log_photos");
        }
    }
}
