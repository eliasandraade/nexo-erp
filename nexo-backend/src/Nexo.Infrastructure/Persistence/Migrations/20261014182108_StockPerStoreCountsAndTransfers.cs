using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockPerStoreCountsAndTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stock_items_product_id",
                schema: "nexo",
                table: "stock_items");

            migrationBuilder.CreateTable(
                name: "inventory_counts",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_counts", x => x.id);
                    table.ForeignKey(
                        name: "FK_inventory_counts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfers",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    from_store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_transfers", x => x.id);
                    table.CheckConstraint("ck_stock_transfers_distinct_stores", "from_store_id <> to_store_id");
                    table.ForeignKey(
                        name: "FK_stock_transfers_stores_from_store_id",
                        column: x => x.from_store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_transfers_stores_to_store_id",
                        column: x => x.to_store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_transfers_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_count_items",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_count_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    counted_quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    system_quantity_at_count = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    system_quantity_at_apply = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    difference = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_count_items", x => x.id);
                    table.CheckConstraint("ck_inventory_count_items_counted_nonnegative", "counted_quantity IS NULL OR counted_quantity >= 0");
                    table.ForeignKey(
                        name: "FK_inventory_count_items_inventory_counts_inventory_count_id",
                        column: x => x.inventory_count_id,
                        principalSchema: "nexo",
                        principalTable: "inventory_counts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_inventory_count_items_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "nexo",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_count_items_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_items",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_transfer_items", x => x.id);
                    table.CheckConstraint("ck_stock_transfer_items_quantity_positive", "quantity > 0");
                    table.ForeignKey(
                        name: "FK_stock_transfer_items_products_destination_product_id",
                        column: x => x.destination_product_id,
                        principalSchema: "nexo",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_transfer_items_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "nexo",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_transfer_items_stock_transfers_stock_transfer_id",
                        column: x => x.stock_transfer_id,
                        principalSchema: "nexo",
                        principalTable: "stock_transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_stock_transfer_items_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stock_items_product_id",
                schema: "nexo",
                table: "stock_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_count_items_inventory_count_id_product_id",
                schema: "nexo",
                table: "inventory_count_items",
                columns: new[] { "inventory_count_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_count_items_product_id",
                schema: "nexo",
                table: "inventory_count_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_count_items_tenant_id",
                schema: "nexo",
                table: "inventory_count_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_counts_tenant_id_store_id_number",
                schema: "nexo",
                table: "inventory_counts",
                columns: new[] { "tenant_id", "store_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfer_items_destination_product_id",
                schema: "nexo",
                table: "stock_transfer_items",
                column: "destination_product_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfer_items_product_id",
                schema: "nexo",
                table: "stock_transfer_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfer_items_stock_transfer_id_product_id",
                schema: "nexo",
                table: "stock_transfer_items",
                columns: new[] { "stock_transfer_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfer_items_tenant_id",
                schema: "nexo",
                table: "stock_transfer_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_from_store_id",
                schema: "nexo",
                table: "stock_transfers",
                column: "from_store_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_tenant_id_from_store_id",
                schema: "nexo",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "from_store_id" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_tenant_id_number",
                schema: "nexo",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_tenant_id_to_store_id",
                schema: "nexo",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "to_store_id" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_to_store_id",
                schema: "nexo",
                table: "stock_transfers",
                column: "to_store_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_count_items",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "stock_transfer_items",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "inventory_counts",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "stock_transfers",
                schema: "nexo");

            migrationBuilder.DropIndex(
                name: "IX_stock_items_product_id",
                schema: "nexo",
                table: "stock_items");

            migrationBuilder.CreateIndex(
                name: "IX_stock_items_product_id",
                schema: "nexo",
                table: "stock_items",
                column: "product_id",
                unique: true);
        }
    }
}
