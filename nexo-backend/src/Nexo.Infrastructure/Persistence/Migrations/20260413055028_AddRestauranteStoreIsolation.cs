using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRestauranteStoreIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Step 1: Drop old tenant-only indexes before adding store-scoped ones ─
            migrationBuilder.DropIndex(
                name: "ix_rest_tables_tenant_area",
                schema: "nexo",
                table: "rest_tables");

            migrationBuilder.DropIndex(
                name: "ix_rest_tables_tenant_number",
                schema: "nexo",
                table: "rest_tables");

            migrationBuilder.DropIndex(
                name: "ix_rest_tables_tenant_status",
                schema: "nexo",
                table: "rest_tables");

            migrationBuilder.DropIndex(
                name: "ix_rest_recipe_cards_tenant_product",
                schema: "nexo",
                table: "rest_recipe_cards");

            migrationBuilder.DropIndex(
                name: "ix_rest_orders_tenant_created_at",
                schema: "nexo",
                table: "rest_orders");

            migrationBuilder.DropIndex(
                name: "ix_rest_orders_tenant_number",
                schema: "nexo",
                table: "rest_orders");

            migrationBuilder.DropIndex(
                name: "ix_rest_orders_tenant_status",
                schema: "nexo",
                table: "rest_orders");

            migrationBuilder.DropIndex(
                name: "ix_rest_orders_tenant_table",
                schema: "nexo",
                table: "rest_orders");

            migrationBuilder.DropIndex(
                name: "ix_rest_areas_tenant_id_name",
                schema: "nexo",
                table: "rest_areas");

            // ── Step 2: Add store_id as NULLABLE first (safe for existing data) ──────
            migrationBuilder.AddColumn<Guid>(
                name: "store_id",
                schema: "nexo",
                table: "rest_tables",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "store_id",
                schema: "nexo",
                table: "rest_recipe_cards",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "store_id",
                schema: "nexo",
                table: "rest_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "store_id",
                schema: "nexo",
                table: "rest_areas",
                type: "uuid",
                nullable: true);

            // ── Step 3: Backfill store_id from each tenant's earliest active store ──
            // If no data exists, these UPDATEs are no-ops (idempotent).
            migrationBuilder.Sql(@"
                UPDATE nexo.rest_areas ra
                SET store_id = (
                    SELECT s.id FROM nexo.stores s
                    WHERE s.tenant_id = ra.tenant_id
                      AND s.is_active = true
                    ORDER BY s.created_at ASC
                    LIMIT 1
                )
                WHERE ra.store_id IS NULL;
            ");

            migrationBuilder.Sql(@"
                UPDATE nexo.rest_tables rt
                SET store_id = (
                    SELECT s.id FROM nexo.stores s
                    WHERE s.tenant_id = rt.tenant_id
                      AND s.is_active = true
                    ORDER BY s.created_at ASC
                    LIMIT 1
                )
                WHERE rt.store_id IS NULL;
            ");

            migrationBuilder.Sql(@"
                UPDATE nexo.rest_orders ro
                SET store_id = (
                    SELECT s.id FROM nexo.stores s
                    WHERE s.tenant_id = ro.tenant_id
                      AND s.is_active = true
                    ORDER BY s.created_at ASC
                    LIMIT 1
                )
                WHERE ro.store_id IS NULL;
            ");

            migrationBuilder.Sql(@"
                UPDATE nexo.rest_recipe_cards rrc
                SET store_id = (
                    SELECT s.id FROM nexo.stores s
                    WHERE s.tenant_id = rrc.tenant_id
                      AND s.is_active = true
                    ORDER BY s.created_at ASC
                    LIMIT 1
                )
                WHERE rrc.store_id IS NULL;
            ");

            // ── Step 4: Enforce NOT NULL now that all rows are backfilled ────────────
            migrationBuilder.Sql(@"ALTER TABLE nexo.rest_areas ALTER COLUMN store_id SET NOT NULL;");
            migrationBuilder.Sql(@"ALTER TABLE nexo.rest_tables ALTER COLUMN store_id SET NOT NULL;");
            migrationBuilder.Sql(@"ALTER TABLE nexo.rest_orders ALTER COLUMN store_id SET NOT NULL;");
            migrationBuilder.Sql(@"ALTER TABLE nexo.rest_recipe_cards ALTER COLUMN store_id SET NOT NULL;");

            // ── Step 5: Add FK indexes (auto-created by EF for the FK columns) ──────
            migrationBuilder.CreateIndex(
                name: "IX_rest_tables_store_id",
                schema: "nexo",
                table: "rest_tables",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_rest_recipe_cards_store_id",
                schema: "nexo",
                table: "rest_recipe_cards",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_rest_orders_store_id",
                schema: "nexo",
                table: "rest_orders",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_rest_areas_store_id",
                schema: "nexo",
                table: "rest_areas",
                column: "store_id");

            // ── Step 6: Add store-scoped composite indexes ────────────────────────────
            migrationBuilder.CreateIndex(
                name: "ix_rest_tables_tenant_store_area",
                schema: "nexo",
                table: "rest_tables",
                columns: new[] { "tenant_id", "store_id", "area_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rest_tables_tenant_store_number",
                schema: "nexo",
                table: "rest_tables",
                columns: new[] { "tenant_id", "store_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rest_tables_tenant_store_status",
                schema: "nexo",
                table: "rest_tables",
                columns: new[] { "tenant_id", "store_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_rest_recipe_cards_tenant_store_product",
                schema: "nexo",
                table: "rest_recipe_cards",
                columns: new[] { "tenant_id", "store_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rest_orders_tenant_store_created_at",
                schema: "nexo",
                table: "rest_orders",
                columns: new[] { "tenant_id", "store_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_rest_orders_tenant_store_number",
                schema: "nexo",
                table: "rest_orders",
                columns: new[] { "tenant_id", "store_id", "order_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rest_orders_tenant_store_status",
                schema: "nexo",
                table: "rest_orders",
                columns: new[] { "tenant_id", "store_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_rest_orders_tenant_store_table",
                schema: "nexo",
                table: "rest_orders",
                columns: new[] { "tenant_id", "store_id", "table_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rest_areas_tenant_store_name",
                schema: "nexo",
                table: "rest_areas",
                columns: new[] { "tenant_id", "store_id", "name" },
                unique: true);

            // ── Step 7: Add FK constraints ────────────────────────────────────────────
            migrationBuilder.AddForeignKey(
                name: "fk_rest_areas_stores",
                schema: "nexo",
                table: "rest_areas",
                column: "store_id",
                principalSchema: "nexo",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_rest_orders_stores",
                schema: "nexo",
                table: "rest_orders",
                column: "store_id",
                principalSchema: "nexo",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_rest_recipe_cards_stores",
                schema: "nexo",
                table: "rest_recipe_cards",
                column: "store_id",
                principalSchema: "nexo",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_rest_tables_stores",
                schema: "nexo",
                table: "rest_tables",
                column: "store_id",
                principalSchema: "nexo",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_rest_areas_stores",
                schema: "nexo",
                table: "rest_areas");

            migrationBuilder.DropForeignKey(
                name: "fk_rest_orders_stores",
                schema: "nexo",
                table: "rest_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_rest_recipe_cards_stores",
                schema: "nexo",
                table: "rest_recipe_cards");

            migrationBuilder.DropForeignKey(
                name: "fk_rest_tables_stores",
                schema: "nexo",
                table: "rest_tables");

            migrationBuilder.DropIndex(
                name: "IX_rest_tables_store_id",
                schema: "nexo",
                table: "rest_tables");

            migrationBuilder.DropIndex(
                name: "ix_rest_tables_tenant_store_area",
                schema: "nexo",
                table: "rest_tables");

            migrationBuilder.DropIndex(
                name: "ix_rest_tables_tenant_store_number",
                schema: "nexo",
                table: "rest_tables");

            migrationBuilder.DropIndex(
                name: "ix_rest_tables_tenant_store_status",
                schema: "nexo",
                table: "rest_tables");

            migrationBuilder.DropIndex(
                name: "IX_rest_recipe_cards_store_id",
                schema: "nexo",
                table: "rest_recipe_cards");

            migrationBuilder.DropIndex(
                name: "ix_rest_recipe_cards_tenant_store_product",
                schema: "nexo",
                table: "rest_recipe_cards");

            migrationBuilder.DropIndex(
                name: "IX_rest_orders_store_id",
                schema: "nexo",
                table: "rest_orders");

            migrationBuilder.DropIndex(
                name: "ix_rest_orders_tenant_store_created_at",
                schema: "nexo",
                table: "rest_orders");

            migrationBuilder.DropIndex(
                name: "ix_rest_orders_tenant_store_number",
                schema: "nexo",
                table: "rest_orders");

            migrationBuilder.DropIndex(
                name: "ix_rest_orders_tenant_store_status",
                schema: "nexo",
                table: "rest_orders");

            migrationBuilder.DropIndex(
                name: "ix_rest_orders_tenant_store_table",
                schema: "nexo",
                table: "rest_orders");

            migrationBuilder.DropIndex(
                name: "IX_rest_areas_store_id",
                schema: "nexo",
                table: "rest_areas");

            migrationBuilder.DropIndex(
                name: "ix_rest_areas_tenant_store_name",
                schema: "nexo",
                table: "rest_areas");

            migrationBuilder.DropColumn(
                name: "store_id",
                schema: "nexo",
                table: "rest_tables");

            migrationBuilder.DropColumn(
                name: "store_id",
                schema: "nexo",
                table: "rest_recipe_cards");

            migrationBuilder.DropColumn(
                name: "store_id",
                schema: "nexo",
                table: "rest_orders");

            migrationBuilder.DropColumn(
                name: "store_id",
                schema: "nexo",
                table: "rest_areas");

            migrationBuilder.CreateIndex(
                name: "ix_rest_tables_tenant_area",
                schema: "nexo",
                table: "rest_tables",
                columns: new[] { "tenant_id", "area_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rest_tables_tenant_number",
                schema: "nexo",
                table: "rest_tables",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rest_tables_tenant_status",
                schema: "nexo",
                table: "rest_tables",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_rest_recipe_cards_tenant_product",
                schema: "nexo",
                table: "rest_recipe_cards",
                columns: new[] { "tenant_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rest_orders_tenant_created_at",
                schema: "nexo",
                table: "rest_orders",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_rest_orders_tenant_number",
                schema: "nexo",
                table: "rest_orders",
                columns: new[] { "tenant_id", "order_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rest_orders_tenant_status",
                schema: "nexo",
                table: "rest_orders",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_rest_orders_tenant_table",
                schema: "nexo",
                table: "rest_orders",
                columns: new[] { "tenant_id", "table_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rest_areas_tenant_id_name",
                schema: "nexo",
                table: "rest_areas",
                columns: new[] { "tenant_id", "name" },
                unique: true);
        }
    }
}
