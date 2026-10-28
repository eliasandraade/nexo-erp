using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaxControlledPilotApproval : Migration
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

            SeedPilotApproval(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The approvals go with their columns.
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
