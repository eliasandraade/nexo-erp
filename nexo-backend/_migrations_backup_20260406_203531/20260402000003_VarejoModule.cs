using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [Migration("20260402000003_VarejoModule")]
    public partial class VarejoModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── ret_price_lists ───────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name:   "ret_price_lists",
                schema: "nexo",
                columns: table => new
                {
                    id          = table.Column<Guid>(nullable: false),
                    tenant_id   = table.Column<Guid>(nullable: false),
                    name        = table.Column<string>(maxLength: 150, nullable: false),
                    description = table.Column<string>(maxLength: 500, nullable: true),
                    is_default  = table.Column<bool>(nullable: false, defaultValue: false),
                    is_active   = table.Column<bool>(nullable: false, defaultValue: true),
                    created_at  = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at  = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ret_price_lists", x => x.id);
                    table.ForeignKey("fk_ret_price_lists_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("ix_ret_price_lists_tenant_id",
                "ret_price_lists", "tenant_id", schema: "nexo");
            migrationBuilder.CreateIndex("ix_ret_price_lists_tenant_id_is_default",
                "ret_price_lists", new[] { "tenant_id", "is_default" }, schema: "nexo");

            // ── ret_price_list_items ──────────────────────────────────────────
            migrationBuilder.CreateTable(
                name:   "ret_price_list_items",
                schema: "nexo",
                columns: table => new
                {
                    id            = table.Column<Guid>(nullable: false),
                    tenant_id     = table.Column<Guid>(nullable: false),
                    price_list_id = table.Column<Guid>(nullable: false),
                    product_id    = table.Column<Guid>(nullable: false),
                    price         = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    created_at    = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at    = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ret_price_list_items", x => x.id);
                    table.ForeignKey("fk_ret_price_list_items_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_ret_price_list_items_price_lists", x => x.price_list_id,
                        principalSchema: "nexo", principalTable: "ret_price_lists", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_ret_price_list_items_products", x => x.product_id,
                        principalSchema: "nexo", principalTable: "products", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("ix_ret_price_list_items_list_product",
                "ret_price_list_items", new[] { "price_list_id", "product_id" },
                schema: "nexo", unique: true);
            migrationBuilder.CreateIndex("ix_ret_price_list_items_tenant_id_product",
                "ret_price_list_items", new[] { "tenant_id", "product_id" }, schema: "nexo");

            // ── ret_customer_price_lists ──────────────────────────────────────
            migrationBuilder.CreateTable(
                name:   "ret_customer_price_lists",
                schema: "nexo",
                columns: table => new
                {
                    id            = table.Column<Guid>(nullable: false),
                    tenant_id     = table.Column<Guid>(nullable: false),
                    customer_id   = table.Column<Guid>(nullable: false),
                    price_list_id = table.Column<Guid>(nullable: false),
                    created_at    = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at    = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ret_customer_price_lists", x => x.id);
                    table.ForeignKey("fk_ret_customer_price_lists_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_ret_customer_price_lists_customers", x => x.customer_id,
                        principalSchema: "nexo", principalTable: "customers", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_ret_customer_price_lists_price_lists", x => x.price_list_id,
                        principalSchema: "nexo", principalTable: "ret_price_lists", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("ix_ret_customer_price_lists_tenant_customer",
                "ret_customer_price_lists", new[] { "tenant_id", "customer_id" },
                schema: "nexo", unique: true);

            // ── ret_purchases ────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name:   "ret_purchases",
                schema: "nexo",
                columns: table => new
                {
                    id              = table.Column<Guid>(nullable: false),
                    tenant_id       = table.Column<Guid>(nullable: false),
                    purchase_number = table.Column<int>(nullable: false),
                    status          = table.Column<string>(maxLength: 20, nullable: false),
                    supplier_id     = table.Column<Guid>(nullable: false),
                    user_id         = table.Column<Guid>(nullable: true),
                    total_amount    = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    invoice_number  = table.Column<string>(maxLength: 100, nullable: true),
                    received_at     = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    confirmed_at    = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    cancelled_at    = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    notes           = table.Column<string>(maxLength: 1000, nullable: true),
                    created_at      = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at      = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ret_purchases", x => x.id);
                    table.ForeignKey("fk_ret_purchases_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_ret_purchases_suppliers", x => x.supplier_id,
                        principalSchema: "nexo", principalTable: "suppliers", principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex("ix_ret_purchases_tenant_id_number",
                "ret_purchases", new[] { "tenant_id", "purchase_number" },
                schema: "nexo", unique: true);
            migrationBuilder.CreateIndex("ix_ret_purchases_tenant_id_status",
                "ret_purchases", new[] { "tenant_id", "status" }, schema: "nexo");
            migrationBuilder.CreateIndex("ix_ret_purchases_tenant_id_supplier",
                "ret_purchases", new[] { "tenant_id", "supplier_id" }, schema: "nexo");
            migrationBuilder.CreateIndex("ix_ret_purchases_tenant_id_created_at",
                "ret_purchases", new[] { "tenant_id", "created_at" }, schema: "nexo");

            // ── ret_purchase_items ────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name:   "ret_purchase_items",
                schema: "nexo",
                columns: table => new
                {
                    id          = table.Column<Guid>(nullable: false),
                    tenant_id   = table.Column<Guid>(nullable: false),
                    purchase_id = table.Column<Guid>(nullable: false),
                    product_id  = table.Column<Guid>(nullable: false),
                    quantity    = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit_cost   = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total       = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    notes       = table.Column<string>(maxLength: 500, nullable: true),
                    created_at  = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at  = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ret_purchase_items", x => x.id);
                    table.ForeignKey("fk_ret_purchase_items_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_ret_purchase_items_purchases", x => x.purchase_id,
                        principalSchema: "nexo", principalTable: "ret_purchases", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_ret_purchase_items_products", x => x.product_id,
                        principalSchema: "nexo", principalTable: "products", principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex("ix_ret_purchase_items_tenant_id_purchase_id",
                "ret_purchase_items", new[] { "tenant_id", "purchase_id" }, schema: "nexo");
            migrationBuilder.CreateIndex("ix_ret_purchase_items_tenant_id_product_id",
                "ret_purchase_items", new[] { "tenant_id", "product_id" }, schema: "nexo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ret_purchase_items",        schema: "nexo");
            migrationBuilder.DropTable(name: "ret_purchases",             schema: "nexo");
            migrationBuilder.DropTable(name: "ret_customer_price_lists",  schema: "nexo");
            migrationBuilder.DropTable(name: "ret_price_list_items",      schema: "nexo");
            migrationBuilder.DropTable(name: "ret_price_lists",           schema: "nexo");
        }
    }
}
