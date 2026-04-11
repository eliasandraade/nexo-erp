using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stock_movements_tenant_id_created_at",
                schema: "nexo",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "IX_stock_movements_tenant_id_product_id",
                schema: "nexo",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "IX_stock_items_tenant_id_product_id",
                schema: "nexo",
                table: "stock_items");

            migrationBuilder.DropIndex(
                name: "IX_sales_tenant_id_created_at",
                schema: "nexo",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "IX_sales_tenant_id_customer_id",
                schema: "nexo",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "IX_sales_tenant_id_number",
                schema: "nexo",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "IX_sales_tenant_id_status",
                schema: "nexo",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "IX_products_tenant_id",
                schema: "nexo",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_tenant_id_barcode",
                schema: "nexo",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_tenant_id_code",
                schema: "nexo",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_cash_sessions_tenant_id",
                schema: "nexo",
                table: "cash_sessions");

            migrationBuilder.DropIndex(
                name: "ix_cash_sessions_tenant_user_status",
                schema: "nexo",
                table: "cash_sessions");

            migrationBuilder.AddColumn<Guid>(
                name: "store_id",
                schema: "nexo",
                table: "stock_movements",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "store_id",
                schema: "nexo",
                table: "stock_items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "store_id",
                schema: "nexo",
                table: "sales",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "store_id",
                schema: "nexo",
                table: "products",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "store_id",
                schema: "nexo",
                table: "customers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "store_id",
                schema: "nexo",
                table: "cash_sessions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "stores",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_subscription_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    settings_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stores", x => x.id);
                    table.ForeignKey(
                        name: "FK_stores_module_subscriptions_module_subscription_id",
                        column: x => x.module_subscription_id,
                        principalSchema: "nexo",
                        principalTable: "module_subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_stores_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_store_id",
                schema: "nexo",
                table: "stock_movements",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_tenant_id_store_id_created_at",
                schema: "nexo",
                table: "stock_movements",
                columns: new[] { "tenant_id", "store_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_tenant_id_store_id_product_id",
                schema: "nexo",
                table: "stock_movements",
                columns: new[] { "tenant_id", "store_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_items_store_id",
                schema: "nexo",
                table: "stock_items",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_items_tenant_id_store_id_product_id",
                schema: "nexo",
                table: "stock_items",
                columns: new[] { "tenant_id", "store_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_store_id",
                schema: "nexo",
                table: "sales",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_tenant_id_store_id_created_at",
                schema: "nexo",
                table: "sales",
                columns: new[] { "tenant_id", "store_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_tenant_id_store_id_customer_id",
                schema: "nexo",
                table: "sales",
                columns: new[] { "tenant_id", "store_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_tenant_id_store_id_number",
                schema: "nexo",
                table: "sales",
                columns: new[] { "tenant_id", "store_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_tenant_id_store_id_status",
                schema: "nexo",
                table: "sales",
                columns: new[] { "tenant_id", "store_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_products_store_id",
                schema: "nexo",
                table: "products",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_products_tenant_id_store_id",
                schema: "nexo",
                table: "products",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "IX_products_tenant_id_store_id_barcode",
                schema: "nexo",
                table: "products",
                columns: new[] { "tenant_id", "store_id", "barcode" });

            migrationBuilder.CreateIndex(
                name: "IX_products_tenant_id_store_id_code",
                schema: "nexo",
                table: "products",
                columns: new[] { "tenant_id", "store_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customers_store_id",
                schema: "nexo",
                table: "customers",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_cash_sessions_store_id",
                schema: "nexo",
                table: "cash_sessions",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_sessions_store_user_status",
                schema: "nexo",
                table: "cash_sessions",
                columns: new[] { "tenant_id", "store_id", "opened_by_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_sessions_tenant_id_store_id",
                schema: "nexo",
                table: "cash_sessions",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "IX_stores_module_subscription_id",
                schema: "nexo",
                table: "stores",
                column: "module_subscription_id");

            migrationBuilder.CreateIndex(
                name: "IX_stores_tenant_id",
                schema: "nexo",
                table: "stores",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_stores_tenant_id_slug",
                schema: "nexo",
                table: "stores",
                columns: new[] { "tenant_id", "slug" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_cash_sessions_stores_store_id",
                schema: "nexo",
                table: "cash_sessions",
                column: "store_id",
                principalSchema: "nexo",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_customers_stores_store_id",
                schema: "nexo",
                table: "customers",
                column: "store_id",
                principalSchema: "nexo",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_products_stores_store_id",
                schema: "nexo",
                table: "products",
                column: "store_id",
                principalSchema: "nexo",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_stores_store_id",
                schema: "nexo",
                table: "sales",
                column: "store_id",
                principalSchema: "nexo",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_items_stores_store_id",
                schema: "nexo",
                table: "stock_items",
                column: "store_id",
                principalSchema: "nexo",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_movements_stores_store_id",
                schema: "nexo",
                table: "stock_movements",
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
                name: "FK_cash_sessions_stores_store_id",
                schema: "nexo",
                table: "cash_sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_customers_stores_store_id",
                schema: "nexo",
                table: "customers");

            migrationBuilder.DropForeignKey(
                name: "FK_products_stores_store_id",
                schema: "nexo",
                table: "products");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_stores_store_id",
                schema: "nexo",
                table: "sales");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_items_stores_store_id",
                schema: "nexo",
                table: "stock_items");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_movements_stores_store_id",
                schema: "nexo",
                table: "stock_movements");

            migrationBuilder.DropTable(
                name: "stores",
                schema: "nexo");

            migrationBuilder.DropIndex(
                name: "IX_stock_movements_store_id",
                schema: "nexo",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "IX_stock_movements_tenant_id_store_id_created_at",
                schema: "nexo",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "IX_stock_movements_tenant_id_store_id_product_id",
                schema: "nexo",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "IX_stock_items_store_id",
                schema: "nexo",
                table: "stock_items");

            migrationBuilder.DropIndex(
                name: "IX_stock_items_tenant_id_store_id_product_id",
                schema: "nexo",
                table: "stock_items");

            migrationBuilder.DropIndex(
                name: "IX_sales_store_id",
                schema: "nexo",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "IX_sales_tenant_id_store_id_created_at",
                schema: "nexo",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "IX_sales_tenant_id_store_id_customer_id",
                schema: "nexo",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "IX_sales_tenant_id_store_id_number",
                schema: "nexo",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "IX_sales_tenant_id_store_id_status",
                schema: "nexo",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "IX_products_store_id",
                schema: "nexo",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_tenant_id_store_id",
                schema: "nexo",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_tenant_id_store_id_barcode",
                schema: "nexo",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_tenant_id_store_id_code",
                schema: "nexo",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_customers_store_id",
                schema: "nexo",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "IX_cash_sessions_store_id",
                schema: "nexo",
                table: "cash_sessions");

            migrationBuilder.DropIndex(
                name: "ix_cash_sessions_store_user_status",
                schema: "nexo",
                table: "cash_sessions");

            migrationBuilder.DropIndex(
                name: "IX_cash_sessions_tenant_id_store_id",
                schema: "nexo",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "store_id",
                schema: "nexo",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "store_id",
                schema: "nexo",
                table: "stock_items");

            migrationBuilder.DropColumn(
                name: "store_id",
                schema: "nexo",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "store_id",
                schema: "nexo",
                table: "products");

            migrationBuilder.DropColumn(
                name: "store_id",
                schema: "nexo",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "store_id",
                schema: "nexo",
                table: "cash_sessions");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_tenant_id_created_at",
                schema: "nexo",
                table: "stock_movements",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_tenant_id_product_id",
                schema: "nexo",
                table: "stock_movements",
                columns: new[] { "tenant_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_items_tenant_id_product_id",
                schema: "nexo",
                table: "stock_items",
                columns: new[] { "tenant_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_tenant_id_created_at",
                schema: "nexo",
                table: "sales",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_tenant_id_customer_id",
                schema: "nexo",
                table: "sales",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_tenant_id_number",
                schema: "nexo",
                table: "sales",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_tenant_id_status",
                schema: "nexo",
                table: "sales",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_products_tenant_id",
                schema: "nexo",
                table: "products",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_products_tenant_id_barcode",
                schema: "nexo",
                table: "products",
                columns: new[] { "tenant_id", "barcode" });

            migrationBuilder.CreateIndex(
                name: "IX_products_tenant_id_code",
                schema: "nexo",
                table: "products",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cash_sessions_tenant_id",
                schema: "nexo",
                table: "cash_sessions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_sessions_tenant_user_status",
                schema: "nexo",
                table: "cash_sessions",
                columns: new[] { "tenant_id", "opened_by_user_id", "status" });
        }
    }
}
