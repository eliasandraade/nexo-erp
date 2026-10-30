using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BuildWave2Commercial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "approved_at",
                schema: "nexo",
                table: "bld_budgets",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by",
                schema: "nexo",
                table: "bld_budgets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approved_by_name",
                schema: "nexo",
                table: "bld_budgets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "area_m2",
                schema: "nexo",
                table: "bld_budgets",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "internal_notes",
                schema: "nexo",
                table: "bld_budgets",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                schema: "nexo",
                table: "bld_budgets",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_budget_id",
                schema: "nexo",
                table: "bld_budgets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "rejected_at",
                schema: "nexo",
                table: "bld_budgets",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "rejected_by",
                schema: "nexo",
                table: "bld_budgets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejected_by_name",
                schema: "nexo",
                table: "bld_budgets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                schema: "nexo",
                table: "bld_budgets",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "root_budget_id",
                schema: "nexo",
                table: "bld_budgets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_template_id",
                schema: "nexo",
                table: "bld_budgets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "valid_until",
                schema: "nexo",
                table: "bld_budgets",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "nexo",
                table: "bld_budgets",
                type: "integer",
                nullable: false,
                defaultValue: 1);   // legacy budgets are version 1

            migrationBuilder.AddColumn<int>(
                name: "kind",
                schema: "nexo",
                table: "bld_budget_items",
                type: "integer",
                nullable: false,
                defaultValue: 5);   // legacy items: Other (BuildCostKind.Other = 5)

            migrationBuilder.AddColumn<string>(
                name: "stage_name",
                schema: "nexo",
                table: "bld_budget_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bld_budget_templates",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    default_margin_percent = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_budget_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_budget_templates_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_change_orders",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    reason = table.Column<int>(type: "integer", nullable: false),
                    cost_impact = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    schedule_impact_days = table.Column<int>(type: "integer", nullable: false),
                    stage_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    decided_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision_channel = table.Column<int>(type: "integer", nullable: true),
                    client_comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    signer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    source_selection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    applied_to_budget = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_change_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_change_orders_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bld_change_orders_stage",
                        column: x => x.stage_id,
                        principalSchema: "nexo",
                        principalTable: "bld_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "bld_material_requests",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage_id = table.Column<Guid>(type: "uuid", nullable: true),
                    material_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,3)", nullable: false),
                    needed_by = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estimated_unit_cost = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    quoted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    ordered_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    delivered_quantity = table.Column<decimal>(type: "numeric(12,3)", nullable: false),
                    delivered_at = table.Column<DateOnly>(type: "date", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_material_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_material_requests_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bld_material_requests_stage",
                        column: x => x.stage_id,
                        principalSchema: "nexo",
                        principalTable: "bld_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_bld_material_requests_supplier",
                        column: x => x.supplier_id,
                        principalSchema: "nexo",
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "bld_measurements",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    rejected_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_measurements", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_measurements_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_selections",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    stage_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    client_visible = table.Column<bool>(type: "boolean", nullable: false),
                    chosen_option_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    chosen_via = table.Column<int>(type: "integer", nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    confirmed_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    change_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_selections", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_selections_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bld_selections_stage",
                        column: x => x.stage_id,
                        principalSchema: "nexo",
                        principalTable: "bld_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "bld_budget_template_items",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity_per_m2 = table.Column<decimal>(type: "numeric(12,4)", nullable: true),
                    fixed_quantity = table.Column<decimal>(type: "numeric(12,3)", nullable: true),
                    unit_cost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    stage_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_budget_template_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_budget_template_items_template",
                        column: x => x.template_id,
                        principalSchema: "nexo",
                        principalTable: "bld_budget_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_change_order_items",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_bld_change_order_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_change_order_items_change_order",
                        column: x => x.change_order_id,
                        principalSchema: "nexo",
                        principalTable: "bld_change_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_material_deliveries",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,3)", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    received_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_material_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_material_deliveries_request",
                        column: x => x.material_request_id,
                        principalSchema: "nexo",
                        principalTable: "bld_material_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_measurement_items",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    measurement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_percent = table.Column<int>(type: "integer", nullable: false),
                    measured_percent = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_measurement_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_measurement_items_measurement",
                        column: x => x.measurement_id,
                        principalSchema: "nexo",
                        principalTable: "bld_measurements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bld_selection_options",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    price_delta = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    is_standard = table.Column<bool>(type: "boolean", nullable: false),
                    image_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    lead_time_days = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_selection_options", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_selection_options_selection",
                        column: x => x.selection_id,
                        principalSchema: "nexo",
                        principalTable: "bld_selections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bld_budgets_source_template_id",
                schema: "nexo",
                table: "bld_budgets",
                column: "source_template_id");

            migrationBuilder.CreateIndex(
                name: "uix_bld_budgets_family_version",
                schema: "nexo",
                table: "bld_budgets",
                columns: new[] { "tenant_id", "root_budget_id", "version" },
                unique: true,
                filter: "root_budget_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bld_budget_template_items_template",
                schema: "nexo",
                table: "bld_budget_template_items",
                columns: new[] { "tenant_id", "template_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_budget_template_items_template_id",
                schema: "nexo",
                table: "bld_budget_template_items",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_budget_templates_tenant_name",
                schema: "nexo",
                table: "bld_budget_templates",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_bld_change_order_items_change_order",
                schema: "nexo",
                table: "bld_change_order_items",
                columns: new[] { "tenant_id", "change_order_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_change_order_items_change_order_id",
                schema: "nexo",
                table: "bld_change_order_items",
                column: "change_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_change_orders_project_id",
                schema: "nexo",
                table: "bld_change_orders",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_change_orders_project_status",
                schema: "nexo",
                table: "bld_change_orders",
                columns: new[] { "tenant_id", "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_change_orders_stage_id",
                schema: "nexo",
                table: "bld_change_orders",
                column: "stage_id");

            migrationBuilder.CreateIndex(
                name: "uix_bld_change_orders_project_number",
                schema: "nexo",
                table: "bld_change_orders",
                columns: new[] { "tenant_id", "project_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uix_bld_change_orders_public_id",
                schema: "nexo",
                table: "bld_change_orders",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bld_material_deliveries_material_request_id",
                schema: "nexo",
                table: "bld_material_deliveries",
                column: "material_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_material_deliveries_request",
                schema: "nexo",
                table: "bld_material_deliveries",
                columns: new[] { "tenant_id", "material_request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bld_material_requests_movement",
                schema: "nexo",
                table: "bld_material_requests",
                column: "movement_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_material_requests_project_id",
                schema: "nexo",
                table: "bld_material_requests",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_material_requests_project_status",
                schema: "nexo",
                table: "bld_material_requests",
                columns: new[] { "tenant_id", "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_material_requests_stage_id",
                schema: "nexo",
                table: "bld_material_requests",
                column: "stage_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_material_requests_supplier_id",
                schema: "nexo",
                table: "bld_material_requests",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_measurement_items_measurement",
                schema: "nexo",
                table: "bld_measurement_items",
                columns: new[] { "tenant_id", "measurement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_measurement_items_measurement_id",
                schema: "nexo",
                table: "bld_measurement_items",
                column: "measurement_id");

            migrationBuilder.CreateIndex(
                name: "IX_bld_measurements_project_id",
                schema: "nexo",
                table: "bld_measurements",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "uix_bld_measurements_project_number",
                schema: "nexo",
                table: "bld_measurements",
                columns: new[] { "tenant_id", "project_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bld_selection_options_selection",
                schema: "nexo",
                table: "bld_selection_options",
                columns: new[] { "tenant_id", "selection_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_selection_options_selection_id",
                schema: "nexo",
                table: "bld_selection_options",
                column: "selection_id");

            migrationBuilder.CreateIndex(
                name: "uix_bld_selection_options_public_id",
                schema: "nexo",
                table: "bld_selection_options",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bld_selections_project_id",
                schema: "nexo",
                table: "bld_selections",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_bld_selections_project_status",
                schema: "nexo",
                table: "bld_selections",
                columns: new[] { "tenant_id", "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_bld_selections_stage_id",
                schema: "nexo",
                table: "bld_selections",
                column: "stage_id");

            migrationBuilder.CreateIndex(
                name: "uix_bld_selections_public_id",
                schema: "nexo",
                table: "bld_selections",
                column: "public_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_bld_budgets_template",
                schema: "nexo",
                table: "bld_budgets",
                column: "source_template_id",
                principalSchema: "nexo",
                principalTable: "bld_budget_templates",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_bld_budgets_template",
                schema: "nexo",
                table: "bld_budgets");

            migrationBuilder.DropTable(
                name: "bld_budget_template_items",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_change_order_items",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_material_deliveries",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_measurement_items",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_selection_options",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_budget_templates",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_change_orders",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_material_requests",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_measurements",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "bld_selections",
                schema: "nexo");

            migrationBuilder.DropIndex(
                name: "IX_bld_budgets_source_template_id",
                schema: "nexo",
                table: "bld_budgets");

            migrationBuilder.DropIndex(
                name: "uix_bld_budgets_family_version",
                schema: "nexo",
                table: "bld_budgets");

            migrationBuilder.DropColumn(
                name: "approved_at",
                schema: "nexo",
                table: "bld_budgets");

            migrationBuilder.DropColumn(
                name: "approved_by",
                schema: "nexo",
                table: "bld_budgets");

            migrationBuilder.DropColumn(
                name: "approved_by_name",
                schema: "nexo",
                table: "bld_budgets");

            migrationBuilder.DropColumn(
                name: "area_m2",
                schema: "nexo",
                table: "bld_budgets");

            migrationBuilder.DropColumn(
                name: "internal_notes",
                schema: "nexo",
                table: "bld_budgets");

            migrationBuilder.DropColumn(
                name: "notes",
                schema: "nexo",
                table: "bld_budgets");

            migrationBuilder.DropColumn(
                name: "parent_budget_id",
                schema: "nexo",
                table: "bld_budgets");

            migrationBuilder.DropColumn(
                name: "rejected_at",
                schema: "nexo",
                table: "bld_budgets");

            migrationBuilder.DropColumn(
                name: "rejected_by",
                schema: "nexo",
                table: "bld_budgets");

            migrationBuilder.DropColumn(
                name: "rejected_by_name",
                schema: "nexo",
                table: "bld_budgets");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                schema: "nexo",
                table: "bld_budgets");

            migrationBuilder.DropColumn(
                name: "root_budget_id",
                schema: "nexo",
                table: "bld_budgets");

            migrationBuilder.DropColumn(
                name: "source_template_id",
                schema: "nexo",
                table: "bld_budgets");

            migrationBuilder.DropColumn(
                name: "valid_until",
                schema: "nexo",
                table: "bld_budgets");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "nexo",
                table: "bld_budgets");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "nexo",
                table: "bld_budget_items");

            migrationBuilder.DropColumn(
                name: "stage_name",
                schema: "nexo",
                table: "bld_budget_items");
        }
    }
}
