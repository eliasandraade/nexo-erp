using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalePaymentTenderedAndChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "change_amount",
                schema: "nexo",
                table: "sale_payments",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "tendered_amount",
                schema: "nexo",
                table: "sale_payments",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_sale_payments_change_nonnegative",
                schema: "nexo",
                table: "sale_payments",
                sql: "change_amount >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sale_payments_change_nonnegative",
                schema: "nexo",
                table: "sale_payments");

            migrationBuilder.DropColumn(
                name: "change_amount",
                schema: "nexo",
                table: "sale_payments");

            migrationBuilder.DropColumn(
                name: "tendered_amount",
                schema: "nexo",
                table: "sale_payments");
        }
    }
}
