using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BuildWave5Ai : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bld_ai_suggestions",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capability = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    input_refs_json = table.Column<string>(type: "text", nullable: false),
                    output_json = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(4,3)", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    prompt_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    prompt_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prompt_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    input_tokens = table.Column<int>(type: "integer", nullable: false),
                    output_tokens = table.Column<int>(type: "integer", nullable: false),
                    cost_micros = table.Column<long>(type: "bigint", nullable: false),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    applied_refs_json = table.Column<string>(type: "text", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_ai_suggestions", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_ai_suggestions_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_documents",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    file_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    client_visible = table.Column<bool>(type: "boolean", nullable: false),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: true),
                    extracted_json = table.Column<string>(type: "text", nullable: true),
                    suggestion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_documents_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bld_documents_suggestion",
                        column: x => x.suggestion_id,
                        principalSchema: "nexo",
                        principalTable: "bld_ai_suggestions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bld_ai_suggestions_project_capability",
                schema: "nexo",
                table: "bld_ai_suggestions",
                columns: new[] { "tenant_id", "project_id", "capability", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_ai_suggestions_project_id",
                schema: "nexo",
                table: "bld_ai_suggestions",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_ai_suggestions_tenant_created",
                schema: "nexo",
                table: "bld_ai_suggestions",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bld_documents_project_category",
                schema: "nexo",
                table: "bld_documents",
                columns: new[] { "tenant_id", "project_id", "category" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_documents_project_id",
                schema: "nexo",
                table: "bld_documents",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_documents_suggestion_id",
                schema: "nexo",
                table: "bld_documents",
                column: "suggestion_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bld_documents",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_ai_suggestions",
                schema: "nexo");
        }
    }
}
