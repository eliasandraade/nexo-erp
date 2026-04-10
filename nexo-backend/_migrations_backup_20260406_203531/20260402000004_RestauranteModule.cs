using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [Migration("20260402000004_RestauranteModule")]
    public partial class RestauranteModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── stock_movements: add cost_price_snapshot (CORE extension) ─────
            migrationBuilder.AddColumn<decimal>(
                name:       "cost_price_snapshot",
                schema:     "nexo",
                table:      "stock_movements",
                type:       "numeric(18,4)",
                nullable:   true);

            // ── rest_areas ────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name:   "rest_areas",
                schema: "nexo",
                columns: table => new
                {
                    id          = table.Column<Guid>(nullable: false),
                    tenant_id   = table.Column<Guid>(nullable: false),
                    name        = table.Column<string>(maxLength: 100, nullable: false),
                    description = table.Column<string>(maxLength: 500, nullable: true),
                    is_active   = table.Column<bool>(nullable: false, defaultValue: true),
                    created_at  = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at  = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rest_areas", x => x.id);
                    table.ForeignKey("fk_rest_areas_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("ix_rest_areas_tenant_id",
                "rest_areas", "tenant_id", schema: "nexo");

            // ── rest_tables ───────────────────────────────────────────────────
            // NOTE: "number" is varchar(50) — supports "1", "A3", "Varanda 2", etc.
            migrationBuilder.CreateTable(
                name:   "rest_tables",
                schema: "nexo",
                columns: table => new
                {
                    id         = table.Column<Guid>(nullable: false),
                    tenant_id  = table.Column<Guid>(nullable: false),
                    area_id    = table.Column<Guid>(nullable: false),
                    number     = table.Column<string>(maxLength: 50, nullable: false),
                    capacity   = table.Column<int>(nullable: false),
                    status     = table.Column<string>(maxLength: 20, nullable: false),
                    is_active  = table.Column<bool>(nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rest_tables", x => x.id);
                    table.ForeignKey("fk_rest_tables_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_rest_tables_areas", x => x.area_id,
                        principalSchema: "nexo", principalTable: "rest_areas", principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex("ix_rest_tables_tenant_id",
                "rest_tables", "tenant_id", schema: "nexo");
            migrationBuilder.CreateIndex("ix_rest_tables_tenant_id_number",
                "rest_tables", new[] { "tenant_id", "number" }, schema: "nexo", unique: true);
            migrationBuilder.CreateIndex("ix_rest_tables_tenant_id_area_id",
                "rest_tables", new[] { "tenant_id", "area_id" }, schema: "nexo");
            migrationBuilder.CreateIndex("ix_rest_tables_tenant_id_status",
                "rest_tables", new[] { "tenant_id", "status" }, schema: "nexo");

            // ── rest_recipe_cards ─────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name:   "rest_recipe_cards",
                schema: "nexo",
                columns: table => new
                {
                    id         = table.Column<Guid>(nullable: false),
                    tenant_id  = table.Column<Guid>(nullable: false),
                    product_id = table.Column<Guid>(nullable: false),
                    yield      = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    yield_unit = table.Column<string>(maxLength: 50, nullable: false),
                    is_active  = table.Column<bool>(nullable: false, defaultValue: true),
                    notes      = table.Column<string>(maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rest_recipe_cards", x => x.id);
                    table.ForeignKey("fk_rest_recipe_cards_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_rest_recipe_cards_products", x => x.product_id,
                        principalSchema: "nexo", principalTable: "products", principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex("ix_rest_recipe_cards_tenant_id",
                "rest_recipe_cards", "tenant_id", schema: "nexo");
            migrationBuilder.CreateIndex("ix_rest_recipe_cards_tenant_id_product_id",
                "rest_recipe_cards", new[] { "tenant_id", "product_id" }, schema: "nexo", unique: true);

            // ── rest_recipe_ingredients ───────────────────────────────────────
            migrationBuilder.CreateTable(
                name:   "rest_recipe_ingredients",
                schema: "nexo",
                columns: table => new
                {
                    id                    = table.Column<Guid>(nullable: false),
                    tenant_id             = table.Column<Guid>(nullable: false),
                    recipe_card_id        = table.Column<Guid>(nullable: false),
                    ingredient_product_id = table.Column<Guid>(nullable: false),
                    quantity              = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit                  = table.Column<string>(maxLength: 20, nullable: false),
                    created_at            = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at            = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rest_recipe_ingredients", x => x.id);
                    table.ForeignKey("fk_rest_recipe_ingredients_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_rest_recipe_ingredients_recipe_cards", x => x.recipe_card_id,
                        principalSchema: "nexo", principalTable: "rest_recipe_cards", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_rest_recipe_ingredients_products", x => x.ingredient_product_id,
                        principalSchema: "nexo", principalTable: "products", principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex("ix_rest_recipe_ingredients_card_product",
                "rest_recipe_ingredients", new[] { "recipe_card_id", "ingredient_product_id" },
                schema: "nexo", unique: true);
            migrationBuilder.CreateIndex("ix_rest_recipe_ingredients_tenant_id",
                "rest_recipe_ingredients", "tenant_id", schema: "nexo");

            // ── rest_orders ───────────────────────────────────────────────────
            // NOTE: waiter_id (not opened_by) — matches EF config mapping of RestOrder.WaiterId
            //       customer_id is nullable — optional link to a registered customer
            migrationBuilder.CreateTable(
                name:   "rest_orders",
                schema: "nexo",
                columns: table => new
                {
                    id           = table.Column<Guid>(nullable: false),
                    tenant_id    = table.Column<Guid>(nullable: false),
                    table_id     = table.Column<Guid>(nullable: false),
                    waiter_id    = table.Column<Guid>(nullable: false),
                    customer_id  = table.Column<Guid>(nullable: true),
                    order_number = table.Column<int>(nullable: false),
                    status       = table.Column<string>(maxLength: 20, nullable: false),
                    sale_id      = table.Column<Guid>(nullable: true),
                    notes        = table.Column<string>(maxLength: 500, nullable: true),
                    opened_at    = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    closed_at    = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at   = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at   = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rest_orders", x => x.id);
                    table.ForeignKey("fk_rest_orders_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_rest_orders_tables", x => x.table_id,
                        principalSchema: "nexo", principalTable: "rest_tables", principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    // sale_id is intentionally NOT a FK — avoids cross-module DB constraint.
                    // Referential integrity is enforced at the application layer.
                });

            migrationBuilder.CreateIndex("ix_rest_orders_tenant_id",
                "rest_orders", "tenant_id", schema: "nexo");
            migrationBuilder.CreateIndex("ix_rest_orders_tenant_id_order_number",
                "rest_orders", new[] { "tenant_id", "order_number" }, schema: "nexo", unique: true);
            migrationBuilder.CreateIndex("ix_rest_orders_tenant_id_table_id",
                "rest_orders", new[] { "tenant_id", "table_id" }, schema: "nexo");
            migrationBuilder.CreateIndex("ix_rest_orders_tenant_id_status",
                "rest_orders", new[] { "tenant_id", "status" }, schema: "nexo");

            // Partial unique index: one active order per table per tenant.
            // 'Closed' is excluded because it means "awaiting payment" — table is still Occupied.
            // 'Paid' and 'Cancelled' are terminal states — table has already been freed.
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ix_rest_orders_tenant_table_open
                ON nexo.rest_orders (tenant_id, table_id)
                WHERE status NOT IN ('Closed', 'Paid', 'Cancelled');
            ");

            // ── rest_order_items ──────────────────────────────────────────────
            // NOTE: total is persisted (quantity × unit_price snapshot at AddItem time).
            //       Kitchen timestamps are nullable — set as items progress through the flow.
            migrationBuilder.CreateTable(
                name:   "rest_order_items",
                schema: "nexo",
                columns: table => new
                {
                    id                  = table.Column<Guid>(nullable: false),
                    tenant_id           = table.Column<Guid>(nullable: false),
                    order_id            = table.Column<Guid>(nullable: false),
                    product_id          = table.Column<Guid>(nullable: false),
                    quantity            = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit_price          = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total               = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    status              = table.Column<string>(maxLength: 20, nullable: false),
                    notes               = table.Column<string>(maxLength: 500, nullable: true),
                    sent_to_kitchen_at  = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    prepared_at         = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    delivered_at        = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    cancelled_at        = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at          = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at          = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rest_order_items", x => x.id);
                    table.ForeignKey("fk_rest_order_items_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_rest_order_items_orders", x => x.order_id,
                        principalSchema: "nexo", principalTable: "rest_orders", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_rest_order_items_products", x => x.product_id,
                        principalSchema: "nexo", principalTable: "products", principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex("ix_rest_order_items_tenant_id",
                "rest_order_items", "tenant_id", schema: "nexo");
            migrationBuilder.CreateIndex("ix_rest_order_items_order_id",
                "rest_order_items", "order_id", schema: "nexo");
            migrationBuilder.CreateIndex("ix_rest_order_items_tenant_id_product_id",
                "rest_order_items", new[] { "tenant_id", "product_id" }, schema: "nexo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "rest_order_items",        schema: "nexo");
            migrationBuilder.DropTable(name: "rest_orders",             schema: "nexo");
            migrationBuilder.DropTable(name: "rest_recipe_ingredients", schema: "nexo");
            migrationBuilder.DropTable(name: "rest_recipe_cards",       schema: "nexo");
            migrationBuilder.DropTable(name: "rest_tables",             schema: "nexo");
            migrationBuilder.DropTable(name: "rest_areas",              schema: "nexo");

            migrationBuilder.DropColumn(
                name:   "cost_price_snapshot",
                schema: "nexo",
                table:  "stock_movements");
        }
    }
}
