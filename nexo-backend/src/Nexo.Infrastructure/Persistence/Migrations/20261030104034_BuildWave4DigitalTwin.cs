using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BuildWave4DigitalTwin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bld_element_progress_events",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    element_external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_bld_element_progress_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_element_progress_events_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_models",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    discipline = table.Column<int>(type: "integer", nullable: false),
                    current_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_models", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_models_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_tenant_settings",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    build_3d_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    build_ai_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    build_vision_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    build_predict_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_tenant_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_tenant_settings_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_model_versions",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    source_format = table.Column<int>(type: "integer", nullable: false),
                    source_file_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    geometry_source = table.Column<int>(type: "integer", nullable: false),
                    viewer_file_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    viewer_source = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    status_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    element_count = table.Column<int>(type: "integer", nullable: false),
                    storey_count = table.Column<int>(type: "integer", nullable: false),
                    schema = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    layers = table.Column<string>(type: "json", nullable: true),
                    processed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_model_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_model_versions_model",
                        column: x => x.model_id,
                        principalSchema: "nexo",
                        principalTable: "bld_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_model_elements",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    type_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    material = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    properties = table.Column<string>(type: "json", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_model_elements", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_model_elements_version",
                        column: x => x.model_version_id,
                        principalSchema: "nexo",
                        principalTable: "bld_model_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_model_processing_jobs",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    finished_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_model_processing_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_model_processing_jobs_version",
                        column: x => x.version_id,
                        principalSchema: "nexo",
                        principalTable: "bld_model_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_model_zones",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    parent_zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    elevation = table.Column<decimal>(type: "numeric(12,3)", nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false),
                    outline_2d = table.Column<string>(type: "json", nullable: true),
                    sheet_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    simplified_box = table.Column<string>(type: "json", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_model_zones", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_model_zones_parent",
                        column: x => x.parent_zone_id,
                        principalSchema: "nexo",
                        principalTable: "bld_model_zones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_bld_model_zones_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bld_model_zones_sheet",
                        column: x => x.sheet_version_id,
                        principalSchema: "nexo",
                        principalTable: "bld_model_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_bld_model_zones_version",
                        column: x => x.model_version_id,
                        principalSchema: "nexo",
                        principalTable: "bld_model_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_model_stage_bindings",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<int>(type: "integer", nullable: false),
                    model_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    element_external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    source = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_model_stage_bindings", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_model_stage_bindings_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bld_model_stage_bindings_stage",
                        column: x => x.stage_id,
                        principalSchema: "nexo",
                        principalTable: "bld_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bld_model_stage_bindings_zone",
                        column: x => x.zone_id,
                        principalSchema: "nexo",
                        principalTable: "bld_model_zones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_reality_captures",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    captured_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    stage_id = table.Column<Guid>(type: "uuid", nullable: true),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    element_external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    media_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    thumbnail_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    client_visible = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_reality_captures", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_reality_captures_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bld_reality_captures_stage",
                        column: x => x.stage_id,
                        principalSchema: "nexo",
                        principalTable: "bld_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_bld_reality_captures_zone",
                        column: x => x.zone_id,
                        principalSchema: "nexo",
                        principalTable: "bld_model_zones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bld_element_progress_events_element_date",
                schema: "nexo",
                table: "bld_element_progress_events",
                columns: new[] { "tenant_id", "project_id", "element_external_id", "effective_date" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_element_progress_events_project_id",
                schema: "nexo",
                table: "bld_element_progress_events",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_model_elements_model_version_id",
                schema: "nexo",
                table: "bld_model_elements",
                column: "model_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_model_elements_version_category",
                schema: "nexo",
                table: "bld_model_elements",
                columns: new[] { "tenant_id", "model_version_id", "category" });

            migrationBuilder.CreateIndex(
                name: "ix_bld_model_elements_version_zone",
                schema: "nexo",
                table: "bld_model_elements",
                columns: new[] { "tenant_id", "model_version_id", "zone_id" });

            migrationBuilder.CreateIndex(
                name: "ux_bld_model_elements_version_external",
                schema: "nexo",
                table: "bld_model_elements",
                columns: new[] { "tenant_id", "model_version_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bld_model_processing_jobs_status_created",
                schema: "nexo",
                table: "bld_model_processing_jobs",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bld_model_processing_jobs_version",
                schema: "nexo",
                table: "bld_model_processing_jobs",
                columns: new[] { "tenant_id", "version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_model_processing_jobs_version_id",
                schema: "nexo",
                table: "bld_model_processing_jobs",
                column: "version_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_model_stage_bindings_project_id",
                schema: "nexo",
                table: "bld_model_stage_bindings",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_model_stage_bindings_project_scope",
                schema: "nexo",
                table: "bld_model_stage_bindings",
                columns: new[] { "tenant_id", "project_id", "scope" });

            migrationBuilder.CreateIndex(
                name: "ix_bld_model_stage_bindings_stage",
                schema: "nexo",
                table: "bld_model_stage_bindings",
                columns: new[] { "tenant_id", "stage_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_model_stage_bindings_stage_id",
                schema: "nexo",
                table: "bld_model_stage_bindings",
                column: "stage_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_model_stage_bindings_zone_id",
                schema: "nexo",
                table: "bld_model_stage_bindings",
                column: "zone_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_model_versions_model_id",
                schema: "nexo",
                table: "bld_model_versions",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_model_versions_project",
                schema: "nexo",
                table: "bld_model_versions",
                columns: new[] { "tenant_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ux_bld_model_versions_model_number",
                schema: "nexo",
                table: "bld_model_versions",
                columns: new[] { "tenant_id", "model_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bld_model_zones_model_version_id",
                schema: "nexo",
                table: "bld_model_zones",
                column: "model_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_model_zones_parent_zone_id",
                schema: "nexo",
                table: "bld_model_zones",
                column: "parent_zone_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_model_zones_project",
                schema: "nexo",
                table: "bld_model_zones",
                columns: new[] { "tenant_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_model_zones_project_id",
                schema: "nexo",
                table: "bld_model_zones",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_model_zones_sheet_version_id",
                schema: "nexo",
                table: "bld_model_zones",
                column: "sheet_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_model_zones_version",
                schema: "nexo",
                table: "bld_model_zones",
                columns: new[] { "tenant_id", "model_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bld_models_project",
                schema: "nexo",
                table: "bld_models",
                columns: new[] { "tenant_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_models_project_id",
                schema: "nexo",
                table: "bld_models",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_reality_captures_project_captured",
                schema: "nexo",
                table: "bld_reality_captures",
                columns: new[] { "tenant_id", "project_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_reality_captures_project_id",
                schema: "nexo",
                table: "bld_reality_captures",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_reality_captures_stage_id",
                schema: "nexo",
                table: "bld_reality_captures",
                column: "stage_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_reality_captures_zone_id",
                schema: "nexo",
                table: "bld_reality_captures",
                column: "zone_id");

            migrationBuilder.CreateIndex(
                name: "ux_bld_tenant_settings_tenant",
                schema: "nexo",
                table: "bld_tenant_settings",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bld_element_progress_events",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_model_elements",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_model_processing_jobs",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_model_stage_bindings",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_reality_captures",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_tenant_settings",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_model_zones",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_model_versions",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_models",
                schema: "nexo");
        }
    }
}
