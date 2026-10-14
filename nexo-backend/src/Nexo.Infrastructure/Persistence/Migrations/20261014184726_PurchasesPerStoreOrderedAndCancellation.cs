using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurchasesPerStoreOrderedAndCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ret_purchases_tenant_id_number",
                schema: "nexo",
                table: "ret_purchases");

            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                schema: "nexo",
                table: "ret_purchases",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ordered_at",
                schema: "nexo",
                table: "ret_purchases",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "store_id",
                schema: "nexo",
                table: "ret_purchases",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Existing purchases had no store. Use the store that actually received the goods (their
            // stock movements), else the store of their products (catalogue is per store), else the
            // tenant's default store (first active by name, as at login) so they stay visible.
            migrationBuilder.Sql(@"
                UPDATE nexo.ret_purchases p
                SET store_id = COALESCE(
                    (SELECT sm.store_id FROM nexo.stock_movements sm
                     WHERE sm.reference_type = 'Purchase' AND sm.reference_id = p.id
                     LIMIT 1),
                    (SELECT pr.store_id FROM nexo.ret_purchase_items i
                     JOIN nexo.products pr ON pr.id = i.product_id
                     WHERE i.purchase_id = p.id
                     LIMIT 1),
                    (SELECT s.id FROM nexo.stores s
                     WHERE s.tenant_id = p.tenant_id AND s.status = 'Active'
                     ORDER BY s.name LIMIT 1),
                    (SELECT s.id FROM nexo.stores s
                     WHERE s.tenant_id = p.tenant_id
                     ORDER BY s.name LIMIT 1),
                    p.store_id)
                WHERE p.store_id = '00000000-0000-0000-0000-000000000000';");

            migrationBuilder.CreateIndex(
                name: "IX_ret_purchases_tenant_id_store_id_purchase_number",
                schema: "nexo",
                table: "ret_purchases",
                columns: new[] { "tenant_id", "store_id", "purchase_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ret_purchases_tenant_id_store_id_purchase_number",
                schema: "nexo",
                table: "ret_purchases");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                schema: "nexo",
                table: "ret_purchases");

            migrationBuilder.DropColumn(
                name: "ordered_at",
                schema: "nexo",
                table: "ret_purchases");

            migrationBuilder.DropColumn(
                name: "store_id",
                schema: "nexo",
                table: "ret_purchases");

            migrationBuilder.CreateIndex(
                name: "ix_ret_purchases_tenant_id_number",
                schema: "nexo",
                table: "ret_purchases",
                columns: new[] { "tenant_id", "purchase_number" },
                unique: true);
        }
    }
}
