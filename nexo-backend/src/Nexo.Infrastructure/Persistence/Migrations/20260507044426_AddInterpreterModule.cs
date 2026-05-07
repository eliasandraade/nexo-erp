using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInterpreterModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "int_attachments",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_int_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_attachments_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "int_audit_logs",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_state = table.Column<string>(type: "jsonb", nullable: false),
                    new_state = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_int_audit_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_audit_logs_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "int_extraction_results",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    input_source = table.Column<int>(type: "integer", nullable: false),
                    raw_user_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    detected_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    amount_confidence = table.Column<float>(type: "real", nullable: false),
                    amount_status = table.Column<int>(type: "integer", nullable: false),
                    detected_date = table.Column<DateOnly>(type: "date", nullable: true),
                    date_confidence = table.Column<float>(type: "real", nullable: false),
                    date_status = table.Column<int>(type: "integer", nullable: false),
                    detected_payee = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    payee_confidence = table.Column<float>(type: "real", nullable: false),
                    payee_status = table.Column<int>(type: "integer", nullable: false),
                    detected_account = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    account_confidence = table.Column<float>(type: "real", nullable: false),
                    account_status = table.Column<int>(type: "integer", nullable: false),
                    analyzer_provider = table.Column<int>(type: "integer", nullable: false),
                    prompt_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    prompt_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    llm_raw_response = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_int_extraction_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_extraction_results_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "int_interpretation_suggestions",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    suggested_direction = table.Column<int>(type: "integer", nullable: false),
                    direction_source = table.Column<int>(type: "integer", nullable: false),
                    suggested_nature = table.Column<int>(type: "integer", nullable: false),
                    nature_source = table.Column<int>(type: "integer", nullable: false),
                    suggested_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_source = table.Column<int>(type: "integer", nullable: false),
                    suggested_context_type = table.Column<int>(type: "integer", nullable: true),
                    suggested_context_id = table.Column<Guid>(type: "uuid", nullable: true),
                    context_source = table.Column<int>(type: "integer", nullable: false),
                    suggested_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_source = table.Column<int>(type: "integer", nullable: false),
                    was_accepted = table.Column<bool>(type: "boolean", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_int_interpretation_suggestions", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_suggestions_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "int_memory_profiles",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    profile_type = table.Column<int>(type: "integer", nullable: false),
                    profile_version = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "jsonb", nullable: false),
                    last_rebuild_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    movements_considered = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_int_memory_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_memory_profiles_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "int_movements",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    nature = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    normalized_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    context_type = table.Column<int>(type: "integer", nullable: false),
                    context_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_int_movements", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_movements_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "int_reprocess_logs",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    triggered_by = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger_reason = table.Column<int>(type: "integer", nullable: false),
                    previous_extraction_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_extraction_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_suggestion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_suggestion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    analyzer_provider = table.Column<int>(type: "integer", nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    finished_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: true),
                    was_accepted = table.Column<bool>(type: "boolean", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    diff_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_int_reprocess_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_reprocess_logs_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "int_stopwords",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    word = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_int_stopwords", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_stopwords_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "int_user_corrections",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    suggestion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correction_type = table.Column<int>(type: "integer", nullable: false),
                    original_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    corrected_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    raw_user_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_int_user_corrections", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_corrections_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_int_attachments_movement_id",
                schema: "nexo",
                table: "int_attachments",
                column: "movement_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_attachments_tenant_id",
                schema: "nexo",
                table: "int_attachments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_audit_logs_movement_id",
                schema: "nexo",
                table: "int_audit_logs",
                column: "movement_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_audit_logs_tenant_created",
                schema: "nexo",
                table: "int_audit_logs",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_int_audit_logs_tenant_id",
                schema: "nexo",
                table: "int_audit_logs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_extraction_results_movement_created",
                schema: "nexo",
                table: "int_extraction_results",
                columns: new[] { "movement_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_int_extraction_results_movement_id",
                schema: "nexo",
                table: "int_extraction_results",
                column: "movement_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_extraction_results_tenant_id",
                schema: "nexo",
                table: "int_extraction_results",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_suggestions_movement_created",
                schema: "nexo",
                table: "int_interpretation_suggestions",
                columns: new[] { "movement_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_int_suggestions_movement_id",
                schema: "nexo",
                table: "int_interpretation_suggestions",
                column: "movement_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_suggestions_tenant_id",
                schema: "nexo",
                table: "int_interpretation_suggestions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_memory_profiles_tenant_id",
                schema: "nexo",
                table: "int_memory_profiles",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_memory_profiles_tenant_user",
                schema: "nexo",
                table: "int_memory_profiles",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_int_movements_context",
                schema: "nexo",
                table: "int_movements",
                columns: new[] { "context_type", "context_id" });

            migrationBuilder.CreateIndex(
                name: "ix_int_movements_created_by",
                schema: "nexo",
                table: "int_movements",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_int_movements_tenant_date",
                schema: "nexo",
                table: "int_movements",
                columns: new[] { "tenant_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_int_movements_tenant_id",
                schema: "nexo",
                table: "int_movements",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_movements_tenant_status",
                schema: "nexo",
                table: "int_movements",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_int_reprocess_logs_movement_id",
                schema: "nexo",
                table: "int_reprocess_logs",
                column: "movement_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_reprocess_logs_tenant_id",
                schema: "nexo",
                table: "int_reprocess_logs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_reprocess_logs_tenant_started",
                schema: "nexo",
                table: "int_reprocess_logs",
                columns: new[] { "tenant_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_int_stopwords_tenant_id",
                schema: "nexo",
                table: "int_stopwords",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_stopwords_tenant_word",
                schema: "nexo",
                table: "int_stopwords",
                columns: new[] { "tenant_id", "word" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_int_corrections_movement_id",
                schema: "nexo",
                table: "int_user_corrections",
                column: "movement_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_corrections_tenant_created",
                schema: "nexo",
                table: "int_user_corrections",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_int_corrections_tenant_id",
                schema: "nexo",
                table: "int_user_corrections",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_corrections_tenant_type",
                schema: "nexo",
                table: "int_user_corrections",
                columns: new[] { "tenant_id", "correction_type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "int_attachments",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "int_audit_logs",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "int_extraction_results",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "int_interpretation_suggestions",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "int_memory_profiles",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "int_movements",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "int_reprocess_logs",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "int_stopwords",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "int_user_corrections",
                schema: "nexo");
        }
    }
}
