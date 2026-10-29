using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Schema only — no governance decision. Adds the internal pilot approval columns (every existing
    /// version starts NotApproved), the governance record of each calculation and the platform-kept pilot
    /// scope (created empty: no tenant takes part). Approving a rule for the pilot, opening the pilot and
    /// admitting tenants are explicit, audited platform actions, never this migration.
    /// </summary>
    public partial class TaxControlledPilotGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "internal_approval",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "NotApproved");

            migrationBuilder.AddColumn<string>(
                name: "internal_approval_caveats",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "internal_approval_decision_ref",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "internal_approval_policy",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "internal_approval_scope",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "internal_approval_valid_from",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "internal_approval_valid_to",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "internal_approval_withdrawal_reason",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "internal_approval_withdrawn_at",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "internal_approved_at",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "internal_approved_by",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "internal_approved_by_id",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "internal_approved_by_role",
                schema: "nexo",
                table: "tax_rule_versions",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "approval_enforced",
                schema: "nexo",
                table: "tax_calculations",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approval_mode",
                schema: "nexo",
                table: "tax_calculations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approval_policy",
                schema: "nexo",
                table: "tax_calculations",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "governance",
                schema: "nexo",
                table: "tax_calculations",
                type: "json",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tax_pilot_scopes",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    decision_reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    activated_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activated_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    activated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_pilot_scopes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tax_pilot_participants",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    added_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    added_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    removed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    removed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    removed_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    removal_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_pilot_participants", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_pilot_participants_scopes",
                        column: x => x.scope_id,
                        principalSchema: "nexo",
                        principalTable: "tax_pilot_scopes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tax_pilot_participants_scope_tenant",
                schema: "nexo",
                table: "tax_pilot_participants",
                columns: new[] { "scope_id", "tenant_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tax_pilot_participants",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tax_pilot_scopes",
                schema: "nexo");

            migrationBuilder.DropColumn(
                name: "internal_approval",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "internal_approval_caveats",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "internal_approval_decision_ref",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "internal_approval_policy",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "internal_approval_scope",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "internal_approval_valid_from",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "internal_approval_valid_to",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "internal_approval_withdrawal_reason",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "internal_approval_withdrawn_at",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "internal_approved_at",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "internal_approved_by",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "internal_approved_by_id",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "internal_approved_by_role",
                schema: "nexo",
                table: "tax_rule_versions");

            migrationBuilder.DropColumn(
                name: "approval_enforced",
                schema: "nexo",
                table: "tax_calculations");

            migrationBuilder.DropColumn(
                name: "approval_mode",
                schema: "nexo",
                table: "tax_calculations");

            migrationBuilder.DropColumn(
                name: "approval_policy",
                schema: "nexo",
                table: "tax_calculations");

            migrationBuilder.DropColumn(
                name: "governance",
                schema: "nexo",
                table: "tax_calculations");
        }
    }
}
