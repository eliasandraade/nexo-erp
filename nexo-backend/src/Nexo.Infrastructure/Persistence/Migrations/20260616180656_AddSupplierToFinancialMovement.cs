using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierToFinancialMovement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "supplier_id",
                schema: "nexo",
                table: "int_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_int_movements_supplier_id",
                schema: "nexo",
                table: "int_movements",
                column: "supplier_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_int_movements_supplier_id",
                schema: "nexo",
                table: "int_movements");

            migrationBuilder.DropColumn(
                name: "supplier_id",
                schema: "nexo",
                table: "int_movements");
        }
    }
}
