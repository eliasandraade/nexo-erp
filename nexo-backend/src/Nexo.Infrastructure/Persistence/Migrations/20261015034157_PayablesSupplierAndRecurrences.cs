using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayablesSupplierAndRecurrences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "supplier_id",
                schema: "nexo",
                table: "financial_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "financial_recurrences",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    day_of_month = table.Column<int>(type: "integer", nullable: false),
                    start_month = table.Column<DateOnly>(type: "date", nullable: false),
                    end_month = table.Column<DateOnly>(type: "date", nullable: true),
                    generated_through = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_recurrences", x => x.id);
                    table.CheckConstraint("ck_financial_recurrences_amount", "amount > 0");
                    table.CheckConstraint("ck_financial_recurrences_day", "day_of_month BETWEEN 1 AND 31");
                    table.ForeignKey(
                        name: "FK_financial_recurrences_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Supplier of each compra título, from the purchase
            migrationBuilder.Sql(@"
UPDATE nexo.financial_transactions ft SET supplier_id = p.supplier_id
  FROM nexo.ret_purchases p
 WHERE ft.supplier_id IS NULL
   AND ft.reference_type IN ('Purchase', 'PurchaseCancellation') AND ft.reference_id = p.id;");

            migrationBuilder.CreateIndex(
                name: "IX_financial_transactions_tenant_id_supplier_id",
                schema: "nexo",
                table: "financial_transactions",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_recurrences_tenant_id_is_active",
                schema: "nexo",
                table: "financial_recurrences",
                columns: new[] { "tenant_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "financial_recurrences",
                schema: "nexo");

            migrationBuilder.DropIndex(
                name: "IX_financial_transactions_tenant_id_supplier_id",
                schema: "nexo",
                table: "financial_transactions");

            migrationBuilder.DropColumn(
                name: "supplier_id",
                schema: "nexo",
                table: "financial_transactions");
        }
    }
}
