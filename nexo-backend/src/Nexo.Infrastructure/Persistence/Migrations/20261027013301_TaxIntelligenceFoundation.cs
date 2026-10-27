using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Tax Intelligence foundation: 21 new tables (catalogue + establishment data) and the first
    /// catalogue content. Additive only — no existing table is touched; Down drops only these tables.
    /// Canonical documents are "json" (not jsonb) so the stored text is exactly what was hashed.
    /// </summary>
    public partial class TaxIntelligenceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tax_legal_sources",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    excerpt = table.Column<string>(type: "text", nullable: false),
                    published_on = table.Column<DateOnly>(type: "date", nullable: true),
                    verification = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    verified_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    verified_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_legal_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tax_profiles",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_profiles_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_profiles_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_rules",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tax_simulations",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    engine_version = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_simulations", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_simulations_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_simulations_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_transactions",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    operation_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    counterparty_regime = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    counterparty_state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    facts_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_transactions_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_transactions_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_profile_versions",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    regime = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    city_ibge_code = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    primary_cnae = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    secondary_cnaes = table.Column<List<string>>(type: "text[]", nullable: false),
                    special_regimes = table.Column<List<string>>(type: "text[]", nullable: false),
                    benefits = table.Column<List<string>>(type: "text[]", nullable: false),
                    accessory_obligations_compliant = table.Column<bool>(type: "boolean", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_profile_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_profile_versions_profiles",
                        column: x => x.profile_id,
                        principalSchema: "nexo",
                        principalTable: "tax_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_profile_versions_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_profile_versions_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_rule_versions",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    conditions = table.Column<string>(type: "json", nullable: false),
                    result = table.Column<string>(type: "json", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    origin = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    activated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    status_changed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_rule_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_rule_versions_rules",
                        column: x => x.rule_id,
                        principalSchema: "nexo",
                        principalTable: "tax_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tax_simulation_scenarios",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    simulation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scenario_index = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_baseline = table.Column<bool>(type: "boolean", nullable: false),
                    input = table.Column<string>(type: "json", nullable: false),
                    result = table.Column<string>(type: "json", nullable: false),
                    total_tax = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    net_cost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_simulation_scenarios", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_simulation_scenarios_simulations",
                        column: x => x.simulation_id,
                        principalSchema: "nexo",
                        principalTable: "tax_simulations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tax_simulation_scenarios_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_simulation_scenarios_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_transaction_lines",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_index = table.Column<int>(type: "integer", nullable: false),
                    item_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ncm = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    gross_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    unconditional_discount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    conditional_discount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    surcharges = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    excluded_taxes = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    excluded_taxes_informed = table.Column<bool>(type: "boolean", nullable: false),
                    special_treatment = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_transaction_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_transaction_lines_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_transaction_lines_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tax_transaction_lines_transactions",
                        column: x => x.transaction_id,
                        principalSchema: "nexo",
                        principalTable: "tax_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_calculations",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_date = table.Column<DateOnly>(type: "date", nullable: false),
                    engine_version = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    input = table.Column<string>(type: "json", nullable: false),
                    input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    rule_set_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    calculation_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    output = table.Column<string>(type: "json", nullable: false),
                    output_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    total_base = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    total_tax = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_calculations", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_calculations_profile_versions",
                        column: x => x.profile_version_id,
                        principalSchema: "nexo",
                        principalTable: "tax_profile_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_calculations_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_calculations_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tax_calculations_transactions",
                        column: x => x.transaction_id,
                        principalSchema: "nexo",
                        principalTable: "tax_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tax_rule_version_sources",
                schema: "nexo",
                columns: table => new
                {
                    rule_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_source_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_rule_version_sources", x => new { x.rule_version_id, x.legal_source_id });
                    table.ForeignKey(
                        name: "fk_tax_rule_version_sources_legal_sources",
                        column: x => x.legal_source_id,
                        principalSchema: "nexo",
                        principalTable: "tax_legal_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_rule_version_sources_versions",
                        column: x => x.rule_version_id,
                        principalSchema: "nexo",
                        principalTable: "tax_rule_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_components",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calculation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_index = table.Column<int>(type: "integer", nullable: false),
                    tax = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    base_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    rate_percent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    rule_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_components", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_components_calculations",
                        column: x => x.calculation_id,
                        principalSchema: "nexo",
                        principalTable: "tax_calculations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tax_components_rule_versions",
                        column: x => x.rule_version_id,
                        principalSchema: "nexo",
                        principalTable: "tax_rule_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_components_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_components_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_credits",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    source_document_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_document_key = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: true),
                    supplier_document = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    acquired_on = table.Column<DateOnly>(type: "date", nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    original_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    eligible_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    eligibility_basis = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    source_calculation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_credits", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_credits_calculations",
                        column: x => x.source_calculation_id,
                        principalSchema: "nexo",
                        principalTable: "tax_calculations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_credits_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_credits_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_opportunities",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    detector_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    source_calculation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    current_scenario = table.Column<string>(type: "json", nullable: false),
                    proposed_scenario = table.Column<string>(type: "json", nullable: false),
                    estimated_impact = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    risk = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    confidence = table.Column<int>(type: "integer", nullable: false),
                    legal_source_codes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    caveats = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    blocked_as = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    block_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reviewed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_opportunities", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_opportunities_calculations",
                        column: x => x.source_calculation_id,
                        principalSchema: "nexo",
                        principalTable: "tax_calculations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_opportunities_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_opportunities_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_rule_executions",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calculation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    rule_version = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    line_index = table.Column<int>(type: "integer", nullable: true),
                    matched = table.Column<bool>(type: "boolean", nullable: false),
                    conditions = table.Column<string>(type: "json", nullable: false),
                    output = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    legal_source_codes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_rule_executions", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_rule_executions_calculations",
                        column: x => x.calculation_id,
                        principalSchema: "nexo",
                        principalTable: "tax_calculations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tax_rule_executions_rule_versions",
                        column: x => x.rule_version_id,
                        principalSchema: "nexo",
                        principalTable: "tax_rule_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_rule_executions_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_rule_executions_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_split_expectations",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calculation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    procedure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expected_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_split_expectations", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_split_expectations_calculations",
                        column: x => x.calculation_id,
                        principalSchema: "nexo",
                        principalTable: "tax_calculations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_split_expectations_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_split_expectations_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_credit_ledger_entries",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    obligation_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_credit_ledger_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_credit_ledger_entries_credits",
                        column: x => x.credit_id,
                        principalSchema: "nexo",
                        principalTable: "tax_credits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_credit_ledger_entries_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_credit_ledger_entries_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_decisions",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    strategy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    dossier = table.Column<string>(type: "json", nullable: false),
                    dossier_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    risk = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    high_risk_acknowledged = table.Column<bool>(type: "boolean", nullable: false),
                    engine_version = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    approved_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    approved_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_decisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_decisions_opportunities",
                        column: x => x.opportunity_id,
                        principalSchema: "nexo",
                        principalTable: "tax_opportunities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_decisions_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_decisions_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_opportunity_evidence",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    document_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    added_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_opportunity_evidence", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_opportunity_evidence_opportunities",
                        column: x => x.opportunity_id,
                        principalSchema: "nexo",
                        principalTable: "tax_opportunities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tax_opportunity_evidence_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_opportunity_evidence_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_opportunity_requirements",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    legal_source_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    met = table.Column<bool>(type: "boolean", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    answered_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    answered_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    answered_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_opportunity_requirements", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_opportunity_requirements_opportunities",
                        column: x => x.opportunity_id,
                        principalSchema: "nexo",
                        principalTable: "tax_opportunities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tax_opportunity_requirements_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_opportunity_requirements_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_split_settlements",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    expectation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actual_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    external_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    settled_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    recorded_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_split_settlements", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_split_settlements_expectations",
                        column: x => x.expectation_id,
                        principalSchema: "nexo",
                        principalTable: "tax_split_expectations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_split_settlements_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_split_settlements_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tax_calculations_profile_version_id",
                schema: "nexo",
                table: "tax_calculations",
                column: "profile_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_calculations_store_id",
                schema: "nexo",
                table: "tax_calculations",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_calculations_tenant_store_created",
                schema: "nexo",
                table: "tax_calculations",
                columns: new[] { "tenant_id", "store_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_tax_calculations_transaction_id",
                schema: "nexo",
                table: "tax_calculations",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ux_tax_calculations_key",
                schema: "nexo",
                table: "tax_calculations",
                columns: new[] { "tenant_id", "store_id", "calculation_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_components_calculation_id",
                schema: "nexo",
                table: "tax_components",
                column: "calculation_id");

            migrationBuilder.CreateIndex(
                name: "IX_tax_components_rule_version_id",
                schema: "nexo",
                table: "tax_components",
                column: "rule_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_components_store_id",
                schema: "nexo",
                table: "tax_components",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_tax_components_tenant_id",
                schema: "nexo",
                table: "tax_components",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_credit_ledger_entries_credit_id",
                schema: "nexo",
                table: "tax_credit_ledger_entries",
                column: "credit_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_credit_ledger_entries_store_id",
                schema: "nexo",
                table: "tax_credit_ledger_entries",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_tax_credit_ledger_entries_tenant_id",
                schema: "nexo",
                table: "tax_credit_ledger_entries",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_tax_credit_ledger_entries_credit_obligation",
                schema: "nexo",
                table: "tax_credit_ledger_entries",
                columns: new[] { "credit_id", "obligation_reference" },
                unique: true,
                filter: "type = 'Used'");

            migrationBuilder.CreateIndex(
                name: "IX_tax_credits_source_calculation_id",
                schema: "nexo",
                table: "tax_credits",
                column: "source_calculation_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_credits_store_id",
                schema: "nexo",
                table: "tax_credits",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_credits_tenant_store_tax",
                schema: "nexo",
                table: "tax_credits",
                columns: new[] { "tenant_id", "store_id", "tax" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_decisions_store_id",
                schema: "nexo",
                table: "tax_decisions",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_tax_decisions_tenant_id",
                schema: "nexo",
                table: "tax_decisions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_tax_decisions_opportunity_id",
                schema: "nexo",
                table: "tax_decisions",
                column: "opportunity_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tax_legal_sources_code",
                schema: "nexo",
                table: "tax_legal_sources",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tax_opportunities_source_calculation_id",
                schema: "nexo",
                table: "tax_opportunities",
                column: "source_calculation_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_opportunities_store_id",
                schema: "nexo",
                table: "tax_opportunities",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_opportunities_tenant_store_status",
                schema: "nexo",
                table: "tax_opportunities",
                columns: new[] { "tenant_id", "store_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_tax_opportunities_detector_calculation",
                schema: "nexo",
                table: "tax_opportunities",
                columns: new[] { "tenant_id", "store_id", "detector_key", "source_calculation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_opportunity_evidence_opportunity_id",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                column: "opportunity_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_opportunity_evidence_store_id",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_tax_opportunity_evidence_tenant_id",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_opportunity_requirements_store_id",
                schema: "nexo",
                table: "tax_opportunity_requirements",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_tax_opportunity_requirements_tenant_id",
                schema: "nexo",
                table: "tax_opportunity_requirements",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_tax_opportunity_requirements_opportunity_code",
                schema: "nexo",
                table: "tax_opportunity_requirements",
                columns: new[] { "opportunity_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_profile_versions_store_id",
                schema: "nexo",
                table: "tax_profile_versions",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_profile_versions_tenant_store_valid_from",
                schema: "nexo",
                table: "tax_profile_versions",
                columns: new[] { "tenant_id", "store_id", "valid_from" });

            migrationBuilder.CreateIndex(
                name: "ux_tax_profile_versions_profile_number",
                schema: "nexo",
                table: "tax_profile_versions",
                columns: new[] { "profile_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_profiles_store_id",
                schema: "nexo",
                table: "tax_profiles",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ux_tax_profiles_tenant_store",
                schema: "nexo",
                table: "tax_profiles",
                columns: new[] { "tenant_id", "store_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_rule_executions_calculation_id",
                schema: "nexo",
                table: "tax_rule_executions",
                column: "calculation_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_rule_executions_rule_version_id",
                schema: "nexo",
                table: "tax_rule_executions",
                column: "rule_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_rule_executions_store_id",
                schema: "nexo",
                table: "tax_rule_executions",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_tax_rule_executions_tenant_id",
                schema: "nexo",
                table: "tax_rule_executions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_rule_version_sources_legal_source_id",
                schema: "nexo",
                table: "tax_rule_version_sources",
                column: "legal_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_rule_versions_status_valid_from",
                schema: "nexo",
                table: "tax_rule_versions",
                columns: new[] { "status", "valid_from" });

            migrationBuilder.CreateIndex(
                name: "ux_tax_rule_versions_rule_version",
                schema: "nexo",
                table: "tax_rule_versions",
                columns: new[] { "rule_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tax_rules_key",
                schema: "nexo",
                table: "tax_rules",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_simulation_scenarios_store_id",
                schema: "nexo",
                table: "tax_simulation_scenarios",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_tax_simulation_scenarios_tenant_id",
                schema: "nexo",
                table: "tax_simulation_scenarios",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_tax_simulation_scenarios_simulation_index",
                schema: "nexo",
                table: "tax_simulation_scenarios",
                columns: new[] { "simulation_id", "scenario_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_simulations_store_id",
                schema: "nexo",
                table: "tax_simulations",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_simulations_tenant_store_created",
                schema: "nexo",
                table: "tax_simulations",
                columns: new[] { "tenant_id", "store_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_split_expectations_store_id",
                schema: "nexo",
                table: "tax_split_expectations",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_tax_split_expectations_tenant_id",
                schema: "nexo",
                table: "tax_split_expectations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_tax_split_expectations_calculation_tax",
                schema: "nexo",
                table: "tax_split_expectations",
                columns: new[] { "calculation_id", "tax" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_split_settlements_store_id",
                schema: "nexo",
                table: "tax_split_settlements",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_tax_split_settlements_tenant_id",
                schema: "nexo",
                table: "tax_split_settlements",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_tax_split_settlements_expectation_reference",
                schema: "nexo",
                table: "tax_split_settlements",
                columns: new[] { "expectation_id", "external_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_transaction_lines_store_id",
                schema: "nexo",
                table: "tax_transaction_lines",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_tax_transaction_lines_tenant_id",
                schema: "nexo",
                table: "tax_transaction_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_tax_transaction_lines_transaction_index",
                schema: "nexo",
                table: "tax_transaction_lines",
                columns: new[] { "transaction_id", "line_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_transactions_facts_hash",
                schema: "nexo",
                table: "tax_transactions",
                columns: new[] { "tenant_id", "store_id", "facts_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_transactions_source",
                schema: "nexo",
                table: "tax_transactions",
                columns: new[] { "tenant_id", "store_id", "source", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_transactions_store_id",
                schema: "nexo",
                table: "tax_transactions",
                column: "store_id");

            // First catalogue content: legal sources (literal excerpts) and the rules built on them.
            Nexo.Infrastructure.TaxIntelligence.Catalog.TaxCatalogV1.Seed(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tax_components",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_credit_ledger_entries",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_decisions",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_opportunity_evidence",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_opportunity_requirements",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_rule_executions",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_rule_version_sources",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_simulation_scenarios",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_split_settlements",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_transaction_lines",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_credits",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_opportunities",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_legal_sources",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_rule_versions",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_simulations",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_split_expectations",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_rules",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_calculations",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_profile_versions",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_transactions",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_profiles",
                schema: "nexo");
        }
    }
}
