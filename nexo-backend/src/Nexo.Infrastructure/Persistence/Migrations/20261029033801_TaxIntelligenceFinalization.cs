using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaxIntelligenceFinalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "counterparty_regular_regime_option",
                schema: "nexo",
                table: "tax_transactions",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "counterparty_simples_situation",
                schema: "nexo",
                table: "tax_transactions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "assumptions",
                schema: "nexo",
                table: "tax_simulations",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "caveats",
                schema: "nexo",
                table: "tax_simulations",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_name",
                schema: "nexo",
                table: "tax_simulations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "governance",
                schema: "nexo",
                table: "tax_simulations",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "professional_review_document_id",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "professional_review_record_id",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "professional_review_recorded_by",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "professional_review_recorded_by_id",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "professional_review_recorded_by_role",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "professional_review_regimes",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<DateOnly>(
                name: "professional_reviewed_on",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ibs_cbs_regular_regime_option",
                schema: "nexo",
                table: "tax_profile_versions",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "simples_situation",
                schema: "nexo",
                table: "tax_profile_versions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unknown");

            // Existing profile versions get the Simples situation their regime already states (the same mapping as the domain);
            // the option for the regular IBS/CBS regime stays unknown (null), so a Simples establishment is not calculated until it says.
            migrationBuilder.Sql(@"UPDATE nexo.tax_profile_versions SET simples_situation = CASE regime
                WHEN 'SimplesNacional' THEN 'WithinSublimit'
                WHEN 'SimplesNacionalExcessoSublimite' THEN 'AboveSublimit'
                WHEN 'Mei' THEN 'Mei'
                ELSE 'NotOptant' END;");

            migrationBuilder.AddColumn<Guid>(
                name: "file_id",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hash_kind",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "reference");

            migrationBuilder.AddColumn<string>(
                name: "revocation_reason",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "revoked_at",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "revoked_by_name",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "superseded_by_evidence_id",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "supersedes_evidence_id",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "assigned_at",
                schema: "nexo",
                table: "tax_opportunities",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "assigned_to_id",
                schema: "nexo",
                table: "tax_opportunities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "assigned_to_name",
                schema: "nexo",
                table: "tax_opportunities",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "verification_recorded_by",
                schema: "nexo",
                table: "tax_legal_sources",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "verification_recorded_by_id",
                schema: "nexo",
                table: "tax_legal_sources",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                schema: "nexo",
                table: "tax_credit_ledger_entries",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "supersedes_assessment_id",
                schema: "nexo",
                table: "tax_compliance_assessments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tax_compliance_obligation_results",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    obligation_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    obligation_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    evidence_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    remediation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    remediation_verified_on = table.Column<DateOnly>(type: "date", nullable: true),
                    remediation_verified_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_compliance_obligation_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_compliance_obligation_results_assessments",
                        column: x => x.assessment_id,
                        principalSchema: "nexo",
                        principalTable: "tax_compliance_assessments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_compliance_obligation_results_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_compliance_obligation_results_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_evidence_files",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    evidence_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    extension = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scan_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scan_engine = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    replaces_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    replaced_by_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    uploaded_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    revoked_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    legal_hold = table.Column<bool>(type: "boolean", nullable: false),
                    legal_hold_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    legal_hold_changed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    legal_hold_changed_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_evidence_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_evidence_files_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_evidence_files_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_item_classifications",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ncm = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    nbs = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    c_class_trib = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    set_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    set_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_item_classifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_item_classifications_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_materiality_policies",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    threshold = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    rationale = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    decision_reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    configured_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    configured_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_materiality_policies", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_materiality_policies_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_module_activations",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    decision_reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    actor_role = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_module_activations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tax_obligation_definitions",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_obligation_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tax_platform_documents",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    extension = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scan_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    uploaded_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    revoked_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_platform_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tax_retention_policies",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    evidence_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    retention_months = table.Column<int>(type: "integer", nullable: false),
                    starts_at = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    allow_tenant_override = table.Column<bool>(type: "boolean", nullable: false),
                    rationale = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    configured_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    configured_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    superseded_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_retention_policies", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_retention_policies_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_classification_codes",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    source = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    source_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    professional_review = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    professional_review_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status_changed_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status_changed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_classification_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_classification_codes_documents",
                        column: x => x.source_document_id,
                        principalSchema: "nexo",
                        principalTable: "tax_platform_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tax_professional_review_records",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    previous_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reviewer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    professional_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reviewed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    scope = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    covered_regimes = table.Column<List<string>>(type: "text[]", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    assumptions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    caveats = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    supporting_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    recorded_by_role = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_professional_review_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_professional_review_records_documents",
                        column: x => x.supporting_document_id,
                        principalSchema: "nexo",
                        principalTable: "tax_platform_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_professional_review_records_versions",
                        column: x => x.rule_version_id,
                        principalSchema: "nexo",
                        principalTable: "tax_rule_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tax_opportunity_evidence_file_id",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ux_tax_credit_ledger_entries_credit_idempotency",
                schema: "nexo",
                table: "tax_credit_ledger_entries",
                columns: new[] { "credit_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_tax_classification_codes_source_document_id",
                schema: "nexo",
                table: "tax_classification_codes",
                column: "source_document_id");

            migrationBuilder.CreateIndex(
                name: "ux_tax_classification_codes_kind_code_from",
                schema: "nexo",
                table: "tax_classification_codes",
                columns: new[] { "kind", "code", "valid_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_compliance_obligation_results_store_id",
                schema: "nexo",
                table: "tax_compliance_obligation_results",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_tax_compliance_obligation_results_tenant_id",
                schema: "nexo",
                table: "tax_compliance_obligation_results",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_tax_compliance_obligation_results_obligation",
                schema: "nexo",
                table: "tax_compliance_obligation_results",
                columns: new[] { "assessment_id", "obligation_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_evidence_files_owner",
                schema: "nexo",
                table: "tax_evidence_files",
                columns: new[] { "tenant_id", "owner", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_evidence_files_store_id",
                schema: "nexo",
                table: "tax_evidence_files",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ux_tax_evidence_files_storage_key",
                schema: "nexo",
                table: "tax_evidence_files",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tax_item_classifications_tenant_product",
                schema: "nexo",
                table: "tax_item_classifications",
                columns: new[] { "tenant_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tax_materiality_policies_tenant_version",
                schema: "nexo",
                table: "tax_materiality_policies",
                columns: new[] { "tenant_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_module_activations_created_at",
                schema: "nexo",
                table: "tax_module_activations",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ux_tax_obligation_definitions_code",
                schema: "nexo",
                table: "tax_obligation_definitions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tax_platform_documents_storage_key",
                schema: "nexo",
                table: "tax_platform_documents",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tax_professional_review_records_supporting_document_id",
                schema: "nexo",
                table: "tax_professional_review_records",
                column: "supporting_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_professional_review_records_version",
                schema: "nexo",
                table: "tax_professional_review_records",
                columns: new[] { "rule_version_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_retention_policies_scope",
                schema: "nexo",
                table: "tax_retention_policies",
                columns: new[] { "tenant_id", "evidence_type", "superseded_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_tax_opportunity_evidence_files",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                column: "file_id",
                principalSchema: "nexo",
                principalTable: "tax_evidence_files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tax_opportunity_evidence_files",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropTable(
                name: "tax_classification_codes",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_compliance_obligation_results",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_evidence_files",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_item_classifications",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_materiality_policies",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_module_activations",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_obligation_definitions",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_professional_review_records",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_retention_policies",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_platform_documents",
                schema: "nexo");

            migrationBuilder.DropIndex(
                name: "IX_tax_opportunity_evidence_file_id",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropIndex(
                name: "ux_tax_credit_ledger_entries_credit_idempotency",
                schema: "nexo",
                table: "tax_credit_ledger_entries");

            migrationBuilder.DropColumn(
                name: "counterparty_regular_regime_option",
                schema: "nexo",
                table: "tax_transactions");

            migrationBuilder.DropColumn(
                name: "counterparty_simples_situation",
                schema: "nexo",
                table: "tax_transactions");

            migrationBuilder.DropColumn(
                name: "assumptions",
                schema: "nexo",
                table: "tax_simulations");

            migrationBuilder.DropColumn(
                name: "caveats",
                schema: "nexo",
                table: "tax_simulations");

            migrationBuilder.DropColumn(
                name: "created_by_name",
                schema: "nexo",
                table: "tax_simulations");

            migrationBuilder.DropColumn(
                name: "governance",
                schema: "nexo",
                table: "tax_simulations");

            migrationBuilder.DropColumn(
                name: "professional_review_document_id",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "professional_review_record_id",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "professional_review_recorded_by",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "professional_review_recorded_by_id",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "professional_review_recorded_by_role",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "professional_review_regimes",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "professional_reviewed_on",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "ibs_cbs_regular_regime_option",
                schema: "nexo",
                table: "tax_profile_versions");

            migrationBuilder.DropColumn(
                name: "simples_situation",
                schema: "nexo",
                table: "tax_profile_versions");

            migrationBuilder.DropColumn(
                name: "file_id",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropColumn(
                name: "hash_kind",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropColumn(
                name: "revocation_reason",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropColumn(
                name: "revoked_at",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropColumn(
                name: "revoked_by_name",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropColumn(
                name: "superseded_by_evidence_id",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropColumn(
                name: "supersedes_evidence_id",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropColumn(
                name: "assigned_at",
                schema: "nexo",
                table: "tax_opportunities");

            migrationBuilder.DropColumn(
                name: "assigned_to_id",
                schema: "nexo",
                table: "tax_opportunities");

            migrationBuilder.DropColumn(
                name: "assigned_to_name",
                schema: "nexo",
                table: "tax_opportunities");

            migrationBuilder.DropColumn(
                name: "verification_recorded_by",
                schema: "nexo",
                table: "tax_legal_sources");

            migrationBuilder.DropColumn(
                name: "verification_recorded_by_id",
                schema: "nexo",
                table: "tax_legal_sources");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                schema: "nexo",
                table: "tax_credit_ledger_entries");

            migrationBuilder.DropColumn(
                name: "supersedes_assessment_id",
                schema: "nexo",
                table: "tax_compliance_assessments");
        }
    }
}
