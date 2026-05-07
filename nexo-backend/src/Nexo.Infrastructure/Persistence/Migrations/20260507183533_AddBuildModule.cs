using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bld_projects",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    client_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expected_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    budget_estimated = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    budget_approved = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_projects", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_projects_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_budgets",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    margin_percent = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    final_price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_budgets", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_budgets_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_bld_budgets_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_daily_logs",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    weather_summary = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_daily_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_daily_logs_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_stages",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    planned_start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    planned_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    progress_percent = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_stages", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_stages_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_daily_log_photos",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    daily_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    caption = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_daily_log_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_daily_log_photos_log",
                        column: x => x.daily_log_id,
                        principalSchema: "nexo",
                        principalTable: "bld_daily_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_budget_items",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    budget_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,3)", nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_budget_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_budget_items_budget",
                        column: x => x.budget_id,
                        principalSchema: "nexo",
                        principalTable: "bld_budgets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bld_budget_items_stage",
                        column: x => x.stage_id,
                        principalSchema: "nexo",
                        principalTable: "bld_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bld_budget_items_budget_id",
                schema: "nexo",
                table: "bld_budget_items",
                column: "budget_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_budget_items_stage_id",
                schema: "nexo",
                table: "bld_budget_items",
                column: "stage_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_budget_items_tenant_id",
                schema: "nexo",
                table: "bld_budget_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_budgets_project_id",
                schema: "nexo",
                table: "bld_budgets",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_budgets_tenant_id",
                schema: "nexo",
                table: "bld_budgets",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_budgets_tenant_status",
                schema: "nexo",
                table: "bld_budgets",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_bld_daily_log_photos_log_id",
                schema: "nexo",
                table: "bld_daily_log_photos",
                column: "daily_log_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_daily_log_photos_tenant_id",
                schema: "nexo",
                table: "bld_daily_log_photos",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_daily_logs_project_id",
                schema: "nexo",
                table: "bld_daily_logs",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_daily_logs_tenant_id",
                schema: "nexo",
                table: "bld_daily_logs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "uix_bld_daily_logs_project_date",
                schema: "nexo",
                table: "bld_daily_logs",
                columns: new[] { "tenant_id", "project_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bld_projects_tenant_id",
                schema: "nexo",
                table: "bld_projects",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_projects_tenant_status",
                schema: "nexo",
                table: "bld_projects",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_bld_stages_project_order",
                schema: "nexo",
                table: "bld_stages",
                columns: new[] { "project_id", "order" });

            migrationBuilder.CreateIndex(
                name: "ix_bld_stages_tenant_id",
                schema: "nexo",
                table: "bld_stages",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bld_budget_items",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_daily_log_photos",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_budgets",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_stages",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_daily_logs",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_projects",
                schema: "nexo");
        }
    }
}
