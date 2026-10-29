using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaxIntelligenceReviewDelta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "accessory_obligations",
                schema: "nexo",
                table: "tax_profile_versions");

            migrationBuilder.DropColumn(
                name: "accessory_obligations_evidence",
                schema: "nexo",
                table: "tax_profile_versions");

            migrationBuilder.RenameColumn(
                name: "eligible_amount",
                schema: "nexo",
                table: "tax_credits",
                newName: "potential_amount");

            migrationBuilder.RenameColumn(
                name: "eligibility_basis",
                schema: "nexo",
                table: "tax_credits",
                newName: "potential_basis");

            migrationBuilder.AddColumn<string>(
                name: "competence_period",
                schema: "nexo",
                table: "tax_transactions",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "document_date",
                schema: "nexo",
                table: "tax_transactions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "execution_from",
                schema: "nexo",
                table: "tax_transactions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "execution_to",
                schema: "nexo",
                table: "tax_transactions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "payment_or_advance_date",
                schema: "nexo",
                table: "tax_transactions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "supply_date",
                schema: "nexo",
                table: "tax_transactions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supply_pattern",
                schema: "nexo",
                table: "tax_transactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "SingleSupply");

            migrationBuilder.AddColumn<string>(
                name: "c_class_trib",
                schema: "nexo",
                table: "tax_transaction_lines",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nbs",
                schema: "nexo",
                table: "tax_transaction_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "professional_reference",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "professional_review_assumptions",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "professional_review_caveats",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "professional_review_scope",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "professional_review_valid_from",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "professional_review_valid_to",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phase",
                schema: "nexo",
                table: "tax_opportunity_requirements",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "ScenarioApproval");

            migrationBuilder.AddColumn<string>(
                name: "required_evidence",
                schema: "nexo",
                table: "tax_opportunity_requirements",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "requirement_id",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_notes",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reviewed_at",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by_id",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reviewed_by_name",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Submitted");

            migrationBuilder.AddColumn<string>(
                name: "type",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Other");

            migrationBuilder.AddColumn<string>(
                name: "discard_reason",
                schema: "nexo",
                table: "tax_credits",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recognition_basis",
                schema: "nexo",
                table: "tax_credits",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stage",
                schema: "nexo",
                table: "tax_credits",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "PotentialIdentified");

            migrationBuilder.CreateTable(
                name: "tax_compliance_assessments",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    period_from = table.Column<DateOnly>(type: "date", nullable: false),
                    period_to = table.Column<DateOnly>(type: "date", nullable: false),
                    scope = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    obligations_checked = table.Column<List<string>>(type: "text[]", nullable: false),
                    evidence = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    assessed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    assessed_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    limitations = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    pnct_participation = table.Column<bool>(type: "boolean", nullable: false),
                    recorded_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_compliance_assessments", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_compliance_assessments_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_compliance_assessments_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_execution_verifications",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fiscal_document_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    authorization_protocol = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    dossier = table.Column<string>(type: "json", nullable: false),
                    dossier_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    verified_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    verified_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    verified_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_execution_verifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_execution_verifications_decisions",
                        column: x => x.decision_id,
                        principalSchema: "nexo",
                        principalTable: "tax_decisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_execution_verifications_opportunities",
                        column: x => x.opportunity_id,
                        principalSchema: "nexo",
                        principalTable: "tax_opportunities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_execution_verifications_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_execution_verifications_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tax_opportunity_evidence_requirement_id",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                column: "requirement_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_compliance_assessments_period",
                schema: "nexo",
                table: "tax_compliance_assessments",
                columns: new[] { "tenant_id", "store_id", "period_from", "period_to" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_compliance_assessments_store_id",
                schema: "nexo",
                table: "tax_compliance_assessments",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_tax_execution_verifications_decision_id",
                schema: "nexo",
                table: "tax_execution_verifications",
                column: "decision_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_execution_verifications_store_id",
                schema: "nexo",
                table: "tax_execution_verifications",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_tax_execution_verifications_tenant_id",
                schema: "nexo",
                table: "tax_execution_verifications",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_tax_execution_verifications_opportunity_id",
                schema: "nexo",
                table: "tax_execution_verifications",
                column: "opportunity_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_tax_opportunity_evidence_requirements",
                schema: "nexo",
                table: "tax_opportunity_evidence",
                column: "requirement_id",
                principalSchema: "nexo",
                principalTable: "tax_opportunity_requirements",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Catalogue V1.1 as literal rows (see the .CatalogV11.cs part of this migration).
            SeedCatalogV11(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            UnseedCatalogV11(migrationBuilder);

            migrationBuilder.DropForeignKey(
                name: "fk_tax_opportunity_evidence_requirements",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropTable(
                name: "tax_compliance_assessments",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_execution_verifications",
                schema: "nexo");

            migrationBuilder.DropIndex(
                name: "IX_tax_opportunity_evidence_requirement_id",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropColumn(
                name: "competence_period",
                schema: "nexo",
                table: "tax_transactions");

            migrationBuilder.DropColumn(
                name: "document_date",
                schema: "nexo",
                table: "tax_transactions");

            migrationBuilder.DropColumn(
                name: "execution_from",
                schema: "nexo",
                table: "tax_transactions");

            migrationBuilder.DropColumn(
                name: "execution_to",
                schema: "nexo",
                table: "tax_transactions");

            migrationBuilder.DropColumn(
                name: "payment_or_advance_date",
                schema: "nexo",
                table: "tax_transactions");

            migrationBuilder.DropColumn(
                name: "supply_date",
                schema: "nexo",
                table: "tax_transactions");

            migrationBuilder.DropColumn(
                name: "supply_pattern",
                schema: "nexo",
                table: "tax_transactions");

            migrationBuilder.DropColumn(
                name: "c_class_trib",
                schema: "nexo",
                table: "tax_transaction_lines");

            migrationBuilder.DropColumn(
                name: "nbs",
                schema: "nexo",
                table: "tax_transaction_lines");

            migrationBuilder.DropColumn(
                name: "professional_reference",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "professional_review_assumptions",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "professional_review_caveats",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "professional_review_scope",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "professional_review_valid_from",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "professional_review_valid_to",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "phase",
                schema: "nexo",
                table: "tax_opportunity_requirements");

            migrationBuilder.DropColumn(
                name: "required_evidence",
                schema: "nexo",
                table: "tax_opportunity_requirements");

            migrationBuilder.DropColumn(
                name: "notes",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropColumn(
                name: "requirement_id",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropColumn(
                name: "review_notes",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropColumn(
                name: "reviewed_by_id",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropColumn(
                name: "reviewed_by_name",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropColumn(
                name: "type",
                schema: "nexo",
                table: "tax_opportunity_evidence");

            migrationBuilder.DropColumn(
                name: "discard_reason",
                schema: "nexo",
                table: "tax_credits");

            migrationBuilder.DropColumn(
                name: "recognition_basis",
                schema: "nexo",
                table: "tax_credits");

            migrationBuilder.DropColumn(
                name: "stage",
                schema: "nexo",
                table: "tax_credits");

            migrationBuilder.RenameColumn(
                name: "potential_basis",
                schema: "nexo",
                table: "tax_credits",
                newName: "eligibility_basis");

            migrationBuilder.RenameColumn(
                name: "potential_amount",
                schema: "nexo",
                table: "tax_credits",
                newName: "eligible_amount");

            migrationBuilder.AddColumn<string>(
                name: "accessory_obligations",
                schema: "nexo",
                table: "tax_profile_versions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "accessory_obligations_evidence",
                schema: "nexo",
                table: "tax_profile_versions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }
    }
}
