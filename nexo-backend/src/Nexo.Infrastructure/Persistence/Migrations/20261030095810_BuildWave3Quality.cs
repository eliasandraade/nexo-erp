using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BuildWave3Quality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bld_checklist_templates",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    stage_name_hint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_checklist_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_checklist_templates_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_warranty_terms",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    months = table.Column<int>(type: "integer", nullable: false),
                    starts_at = table.Column<DateOnly>(type: "date", nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_warranty_terms", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_warranty_terms_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_checklist_template_items",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    required = table.Column<bool>(type: "boolean", nullable: false),
                    requires_photo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_checklist_template_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_checklist_template_items_template",
                        column: x => x.template_id,
                        principalSchema: "nexo",
                        principalTable: "bld_checklist_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_inspections",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage_id = table.Column<Guid>(type: "uuid", nullable: true),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    scheduled_for = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    inspector_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    completed_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_inspections", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_inspections_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bld_inspections_stage",
                        column: x => x.stage_id,
                        principalSchema: "nexo",
                        principalTable: "bld_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_bld_inspections_template",
                        column: x => x.template_id,
                        principalSchema: "nexo",
                        principalTable: "bld_checklist_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "bld_assistance_requests",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    opened_via = table.Column<int>(type: "integer", nullable: false),
                    opened_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    opened_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    triage_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    warranty_covered = table.Column<bool>(type: "boolean", nullable: true),
                    warranty_term_id = table.Column<Guid>(type: "uuid", nullable: true),
                    triaged_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    triaged_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    scheduled_for = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    resolution_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    client_rating = table.Column<int>(type: "integer", nullable: true),
                    client_feedback = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    feedback_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    photo_keys = table.Column<string>(type: "json", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_assistance_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_assistance_requests_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bld_assistance_requests_warranty_term",
                        column: x => x.warranty_term_id,
                        principalSchema: "nexo",
                        principalTable: "bld_warranty_terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "bld_handovers",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    scheduled_for = table.Column<DateOnly>(type: "date", nullable: true),
                    walkthrough_inspection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivered_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    delivered_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    keys_delivered = table.Column<bool>(type: "boolean", nullable: false),
                    documents = table.Column<string>(type: "json", nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    accepted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    accepted_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    acceptance_channel = table.Column<int>(type: "integer", nullable: true),
                    acceptance_comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_handovers", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_handovers_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bld_handovers_walkthrough",
                        column: x => x.walkthrough_inspection_id,
                        principalSchema: "nexo",
                        principalTable: "bld_inspections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "bld_inspection_items",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inspection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    required = table.Column<bool>(type: "boolean", nullable: false),
                    requires_photo = table.Column<bool>(type: "boolean", nullable: false),
                    result = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    severity = table.Column<int>(type: "integer", nullable: true),
                    photo_keys = table.Column<string>(type: "json", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_inspection_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_inspection_items_inspection",
                        column: x => x.inspection_id,
                        principalSchema: "nexo",
                        principalTable: "bld_inspections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_non_conformities",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inspection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inspection_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    number = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    severity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    responsible_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    root_cause = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    corrective_action = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    photo_keys = table.Column<string>(type: "json", nullable: false),
                    cost_impact = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    verified_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_non_conformities", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_non_conformities_inspection",
                        column: x => x.inspection_id,
                        principalSchema: "nexo",
                        principalTable: "bld_inspections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_bld_non_conformities_inspection_item",
                        column: x => x.inspection_item_id,
                        principalSchema: "nexo",
                        principalTable: "bld_inspection_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_bld_non_conformities_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bld_non_conformities_stage",
                        column: x => x.stage_id,
                        principalSchema: "nexo",
                        principalTable: "bld_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bld_assistance_requests_project_id",
                schema: "nexo",
                table: "bld_assistance_requests",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_assistance_requests_project_status",
                schema: "nexo",
                table: "bld_assistance_requests",
                columns: new[] { "tenant_id", "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_assistance_requests_warranty_term_id",
                schema: "nexo",
                table: "bld_assistance_requests",
                column: "warranty_term_id");

            migrationBuilder.CreateIndex(
                name: "uix_bld_assistance_requests_project_number",
                schema: "nexo",
                table: "bld_assistance_requests",
                columns: new[] { "tenant_id", "project_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uix_bld_assistance_requests_public_id",
                schema: "nexo",
                table: "bld_assistance_requests",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bld_checklist_template_items_template",
                schema: "nexo",
                table: "bld_checklist_template_items",
                columns: new[] { "tenant_id", "template_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_checklist_template_items_template_id",
                schema: "nexo",
                table: "bld_checklist_template_items",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_checklist_templates_tenant_name",
                schema: "nexo",
                table: "bld_checklist_templates",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_handovers_project_id",
                schema: "nexo",
                table: "bld_handovers",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_handovers_walkthrough_inspection_id",
                schema: "nexo",
                table: "bld_handovers",
                column: "walkthrough_inspection_id");

            migrationBuilder.CreateIndex(
                name: "uix_bld_handovers_project",
                schema: "nexo",
                table: "bld_handovers",
                columns: new[] { "tenant_id", "project_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bld_inspection_items_inspection",
                schema: "nexo",
                table: "bld_inspection_items",
                columns: new[] { "tenant_id", "inspection_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_inspection_items_inspection_id",
                schema: "nexo",
                table: "bld_inspection_items",
                column: "inspection_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_inspections_project_id",
                schema: "nexo",
                table: "bld_inspections",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_inspections_project_kind_status",
                schema: "nexo",
                table: "bld_inspections",
                columns: new[] { "tenant_id", "project_id", "kind", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_inspections_stage_id",
                schema: "nexo",
                table: "bld_inspections",
                column: "stage_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_inspections_template_id",
                schema: "nexo",
                table: "bld_inspections",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_non_conformities_inspection_id",
                schema: "nexo",
                table: "bld_non_conformities",
                column: "inspection_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_non_conformities_inspection_item",
                schema: "nexo",
                table: "bld_non_conformities",
                columns: new[] { "tenant_id", "inspection_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_non_conformities_inspection_item_id",
                schema: "nexo",
                table: "bld_non_conformities",
                column: "inspection_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_non_conformities_project_id",
                schema: "nexo",
                table: "bld_non_conformities",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_non_conformities_project_status",
                schema: "nexo",
                table: "bld_non_conformities",
                columns: new[] { "tenant_id", "project_id", "status", "severity" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_non_conformities_stage_id",
                schema: "nexo",
                table: "bld_non_conformities",
                column: "stage_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_non_conformities_stage_severity",
                schema: "nexo",
                table: "bld_non_conformities",
                columns: new[] { "tenant_id", "stage_id", "severity", "status" });

            migrationBuilder.CreateIndex(
                name: "uix_bld_non_conformities_project_number",
                schema: "nexo",
                table: "bld_non_conformities",
                columns: new[] { "tenant_id", "project_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bld_warranty_terms_project",
                schema: "nexo",
                table: "bld_warranty_terms",
                columns: new[] { "tenant_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_warranty_terms_project_id",
                schema: "nexo",
                table: "bld_warranty_terms",
                column: "project_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bld_assistance_requests",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_checklist_template_items",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_handovers",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_non_conformities",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_warranty_terms",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_inspection_items",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_inspections",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_checklist_templates",
                schema: "nexo");
        }
    }
}
