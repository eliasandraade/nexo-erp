using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaxReviewFindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<Guid>>(
                name: "participant_ids",
                schema: "nexo",
                table: "tax_opportunities",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<Guid>(
                name: "compliance_assessment_id",
                schema: "nexo",
                table: "tax_calculations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tax_calculations_compliance_assessment_id",
                schema: "nexo",
                table: "tax_calculations",
                column: "compliance_assessment_id");

            migrationBuilder.AddForeignKey(
                name: "fk_tax_calculations_compliance_assessments",
                schema: "nexo",
                table: "tax_calculations",
                column: "compliance_assessment_id",
                principalSchema: "nexo",
                principalTable: "tax_compliance_assessments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tax_calculations_compliance_assessments",
                schema: "nexo",
                table: "tax_calculations");

            migrationBuilder.DropIndex(
                name: "IX_tax_calculations_compliance_assessment_id",
                schema: "nexo",
                table: "tax_calculations");

            migrationBuilder.DropColumn(
                name: "participant_ids",
                schema: "nexo",
                table: "tax_opportunities");

            migrationBuilder.DropColumn(
                name: "compliance_assessment_id",
                schema: "nexo",
                table: "tax_calculations");
        }
    }
}
