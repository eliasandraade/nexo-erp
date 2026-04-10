using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [Migration("20260402000001_CoreEntities")]
    public partial class CoreEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── customers ────────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "customers",
                schema: "nexo",
                columns: table => new
                {
                    id               = table.Column<Guid>(nullable: false),
                    tenant_id        = table.Column<Guid>(nullable: false),
                    person_type      = table.Column<string>(maxLength: 20, nullable: false),
                    name             = table.Column<string>(maxLength: 200, nullable: false),
                    trade_name       = table.Column<string>(maxLength: 200, nullable: true),
                    document_type    = table.Column<string>(maxLength: 10, nullable: false),
                    document_number  = table.Column<string>(maxLength: 20, nullable: false),
                    email            = table.Column<string>(maxLength: 200, nullable: true),
                    phone            = table.Column<string>(maxLength: 30, nullable: true),
                    whatsapp         = table.Column<string>(maxLength: 30, nullable: true),
                    address_json     = table.Column<string>(type: "jsonb", nullable: true),
                    credit_limit     = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    notes            = table.Column<string>(maxLength: 1000, nullable: true),
                    is_active        = table.Column<bool>(nullable: false, defaultValue: true),
                    created_at       = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at       = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                    table.ForeignKey("fk_customers_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex("ix_customers_tenant_id", "customers", "tenant_id", schema: "nexo");
            migrationBuilder.CreateIndex("ix_customers_tenant_id_document_number", "customers",
                new[] { "tenant_id", "document_number" }, schema: "nexo", unique: true);
            migrationBuilder.CreateIndex("ix_customers_tenant_id_name", "customers",
                new[] { "tenant_id", "name" }, schema: "nexo");

            // ── suppliers ────────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "suppliers",
                schema: "nexo",
                columns: table => new
                {
                    id                 = table.Column<Guid>(nullable: false),
                    tenant_id          = table.Column<Guid>(nullable: false),
                    person_type        = table.Column<string>(maxLength: 20, nullable: false),
                    name               = table.Column<string>(maxLength: 200, nullable: false),
                    trade_name         = table.Column<string>(maxLength: 200, nullable: true),
                    document_type      = table.Column<string>(maxLength: 10, nullable: false),
                    document_number    = table.Column<string>(maxLength: 20, nullable: false),
                    email              = table.Column<string>(maxLength: 200, nullable: true),
                    phone              = table.Column<string>(maxLength: 30, nullable: true),
                    contact_name       = table.Column<string>(maxLength: 150, nullable: true),
                    address_json       = table.Column<string>(type: "jsonb", nullable: true),
                    payment_terms_days = table.Column<int>(nullable: true),
                    bank_info_json     = table.Column<string>(type: "jsonb", nullable: true),
                    notes              = table.Column<string>(maxLength: 1000, nullable: true),
                    is_active          = table.Column<bool>(nullable: false, defaultValue: true),
                    created_at         = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at         = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.id);
                    table.ForeignKey("fk_suppliers_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex("ix_suppliers_tenant_id", "suppliers", "tenant_id", schema: "nexo");
            migrationBuilder.CreateIndex("ix_suppliers_tenant_id_document_number", "suppliers",
                new[] { "tenant_id", "document_number" }, schema: "nexo", unique: true);

            // ── categories ───────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "categories",
                schema: "nexo",
                columns: table => new
                {
                    id                 = table.Column<Guid>(nullable: false),
                    tenant_id          = table.Column<Guid>(nullable: false),
                    name               = table.Column<string>(maxLength: 150, nullable: false),
                    description        = table.Column<string>(maxLength: 500, nullable: true),
                    parent_category_id = table.Column<Guid>(nullable: true),
                    is_active          = table.Column<bool>(nullable: false, defaultValue: true),
                    created_at         = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at         = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.ForeignKey("fk_categories_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_categories_parent", x => x.parent_category_id,
                        principalSchema: "nexo", principalTable: "categories", principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex("ix_categories_tenant_id", "categories", "tenant_id", schema: "nexo");
            migrationBuilder.CreateIndex("ix_categories_tenant_id_name", "categories",
                new[] { "tenant_id", "name" }, schema: "nexo");

            // ── products ─────────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "products",
                schema: "nexo",
                columns: table => new
                {
                    id                  = table.Column<Guid>(nullable: false),
                    tenant_id           = table.Column<Guid>(nullable: false),
                    code                = table.Column<string>(maxLength: 50, nullable: false),
                    barcode             = table.Column<string>(maxLength: 50, nullable: true),
                    name                = table.Column<string>(maxLength: 200, nullable: false),
                    description         = table.Column<string>(maxLength: 1000, nullable: true),
                    category_id         = table.Column<Guid>(nullable: true),
                    unit                = table.Column<string>(maxLength: 10, nullable: false),
                    cost_price          = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    sale_price          = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    track_stock         = table.Column<bool>(nullable: false, defaultValue: true),
                    min_stock_quantity  = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    max_stock_quantity  = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    is_active           = table.Column<bool>(nullable: false, defaultValue: true),
                    created_at          = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at          = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.ForeignKey("fk_products_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_products_categories", x => x.category_id,
                        principalSchema: "nexo", principalTable: "categories", principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });
            migrationBuilder.CreateIndex("ix_products_tenant_id", "products", "tenant_id", schema: "nexo");
            migrationBuilder.CreateIndex("ix_products_tenant_id_code", "products",
                new[] { "tenant_id", "code" }, schema: "nexo", unique: true);
            migrationBuilder.CreateIndex("ix_products_tenant_id_barcode", "products",
                new[] { "tenant_id", "barcode" }, schema: "nexo");

            // ── stock_items ──────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "stock_items",
                schema: "nexo",
                columns: table => new
                {
                    id                = table.Column<Guid>(nullable: false),
                    tenant_id         = table.Column<Guid>(nullable: false),
                    product_id        = table.Column<Guid>(nullable: false),
                    current_quantity  = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    reserved_quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    last_movement_at  = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at        = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at        = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_items", x => x.id);
                    table.ForeignKey("fk_stock_items_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_stock_items_products", x => x.product_id,
                        principalSchema: "nexo", principalTable: "products", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex("ix_stock_items_tenant_id_product_id", "stock_items",
                new[] { "tenant_id", "product_id" }, schema: "nexo", unique: true);

            // ── stock_movements ──────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "nexo",
                columns: table => new
                {
                    id                   = table.Column<Guid>(nullable: false),
                    tenant_id            = table.Column<Guid>(nullable: false),
                    product_id           = table.Column<Guid>(nullable: false),
                    movement_type        = table.Column<string>(maxLength: 20, nullable: false),
                    quantity             = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    quantity_before      = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    quantity_after       = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    reference_type       = table.Column<string>(maxLength: 50, nullable: true),
                    reference_id         = table.Column<Guid>(nullable: true),
                    notes                = table.Column<string>(maxLength: 500, nullable: true),
                    created_by_user_id   = table.Column<Guid>(nullable: false),
                    created_at           = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at           = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                    table.ForeignKey("fk_stock_movements_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_stock_movements_products", x => x.product_id,
                        principalSchema: "nexo", principalTable: "products", principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex("ix_stock_movements_tenant_id_product_id", "stock_movements",
                new[] { "tenant_id", "product_id" }, schema: "nexo");
            migrationBuilder.CreateIndex("ix_stock_movements_tenant_id_created_at", "stock_movements",
                new[] { "tenant_id", "created_at" }, schema: "nexo");
            migrationBuilder.CreateIndex("ix_stock_movements_reference", "stock_movements",
                new[] { "reference_type", "reference_id" }, schema: "nexo");

            // ── cash_sessions ────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "cash_sessions",
                schema: "nexo",
                columns: table => new
                {
                    id                  = table.Column<Guid>(nullable: false),
                    tenant_id           = table.Column<Guid>(nullable: false),
                    status              = table.Column<string>(maxLength: 20, nullable: false),
                    opened_by_user_id   = table.Column<Guid>(nullable: false),
                    closed_by_user_id   = table.Column<Guid>(nullable: true),
                    opening_balance     = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    closing_balance     = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    opened_at           = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    closed_at           = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    notes               = table.Column<string>(maxLength: 500, nullable: true),
                    created_at          = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at          = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_sessions", x => x.id);
                    table.ForeignKey("fk_cash_sessions_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_cash_sessions_opened_by", x => x.opened_by_user_id,
                        principalSchema: "nexo", principalTable: "users", principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("fk_cash_sessions_closed_by", x => x.closed_by_user_id,
                        principalSchema: "nexo", principalTable: "users", principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex("ix_cash_sessions_tenant_id", "cash_sessions", "tenant_id", schema: "nexo");
            migrationBuilder.CreateIndex("ix_cash_sessions_tenant_id_status", "cash_sessions",
                new[] { "tenant_id", "status" }, schema: "nexo");

            // ── sales ────────────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "sales",
                schema: "nexo",
                columns: table => new
                {
                    id               = table.Column<Guid>(nullable: false),
                    tenant_id        = table.Column<Guid>(nullable: false),
                    number           = table.Column<int>(nullable: false),
                    status           = table.Column<string>(maxLength: 20, nullable: false),
                    customer_id      = table.Column<Guid>(nullable: true),
                    sold_by_user_id  = table.Column<Guid>(nullable: false),
                    cash_session_id  = table.Column<Guid>(nullable: true),
                    subtotal         = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    discount_amount  = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    tax_amount       = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    total            = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    payment_method   = table.Column<string>(maxLength: 20, nullable: true),
                    notes            = table.Column<string>(maxLength: 500, nullable: true),
                    paid_at          = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at       = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at       = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales", x => x.id);
                    table.ForeignKey("fk_sales_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_sales_customers", x => x.customer_id,
                        principalSchema: "nexo", principalTable: "customers", principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("fk_sales_users", x => x.sold_by_user_id,
                        principalSchema: "nexo", principalTable: "users", principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("fk_sales_cash_sessions", x => x.cash_session_id,
                        principalSchema: "nexo", principalTable: "cash_sessions", principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex("ix_sales_tenant_id_number", "sales",
                new[] { "tenant_id", "number" }, schema: "nexo", unique: true);
            migrationBuilder.CreateIndex("ix_sales_tenant_id_status", "sales",
                new[] { "tenant_id", "status" }, schema: "nexo");
            migrationBuilder.CreateIndex("ix_sales_tenant_id_created_at", "sales",
                new[] { "tenant_id", "created_at" }, schema: "nexo");
            migrationBuilder.CreateIndex("ix_sales_tenant_id_customer_id", "sales",
                new[] { "tenant_id", "customer_id" }, schema: "nexo");

            // ── sale_items ───────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "sale_items",
                schema: "nexo",
                columns: table => new
                {
                    id               = table.Column<Guid>(nullable: false),
                    tenant_id        = table.Column<Guid>(nullable: false),
                    sale_id          = table.Column<Guid>(nullable: false),
                    product_id       = table.Column<Guid>(nullable: false),
                    quantity         = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit_price       = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    discount_amount  = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    total            = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    notes            = table.Column<string>(maxLength: 500, nullable: true),
                    created_at       = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at       = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_items", x => x.id);
                    table.ForeignKey("fk_sale_items_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_sale_items_sales", x => x.sale_id,
                        principalSchema: "nexo", principalTable: "sales", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_sale_items_products", x => x.product_id,
                        principalSchema: "nexo", principalTable: "products", principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex("ix_sale_items_tenant_id_sale_id", "sale_items",
                new[] { "tenant_id", "sale_id" }, schema: "nexo");
            migrationBuilder.CreateIndex("ix_sale_items_tenant_id_product_id", "sale_items",
                new[] { "tenant_id", "product_id" }, schema: "nexo");

            // ── cash_movements ───────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "cash_movements",
                schema: "nexo",
                columns: table => new
                {
                    id                 = table.Column<Guid>(nullable: false),
                    tenant_id          = table.Column<Guid>(nullable: false),
                    cash_session_id    = table.Column<Guid>(nullable: false),
                    movement_type      = table.Column<string>(maxLength: 20, nullable: false),
                    amount             = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    description        = table.Column<string>(maxLength: 500, nullable: false),
                    reference_type     = table.Column<string>(maxLength: 50, nullable: true),
                    reference_id       = table.Column<Guid>(nullable: true),
                    created_by_user_id = table.Column<Guid>(nullable: false),
                    created_at         = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at         = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_movements", x => x.id);
                    table.ForeignKey("fk_cash_movements_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_cash_movements_cash_sessions", x => x.cash_session_id,
                        principalSchema: "nexo", principalTable: "cash_sessions", principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex("ix_cash_movements_tenant_id_cash_session_id", "cash_movements",
                new[] { "tenant_id", "cash_session_id" }, schema: "nexo");

            // ── financial_accounts ───────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "financial_accounts",
                schema: "nexo",
                columns: table => new
                {
                    id                = table.Column<Guid>(nullable: false),
                    tenant_id         = table.Column<Guid>(nullable: false),
                    code              = table.Column<string>(maxLength: 20, nullable: false),
                    name              = table.Column<string>(maxLength: 150, nullable: false),
                    account_type      = table.Column<string>(maxLength: 20, nullable: false),
                    parent_account_id = table.Column<Guid>(nullable: true),
                    is_active         = table.Column<bool>(nullable: false, defaultValue: true),
                    created_at        = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at        = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_accounts", x => x.id);
                    table.ForeignKey("fk_financial_accounts_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_financial_accounts_parent", x => x.parent_account_id,
                        principalSchema: "nexo", principalTable: "financial_accounts", principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex("ix_financial_accounts_tenant_id", "financial_accounts",
                "tenant_id", schema: "nexo");
            migrationBuilder.CreateIndex("ix_financial_accounts_tenant_id_code", "financial_accounts",
                new[] { "tenant_id", "code" }, schema: "nexo", unique: true);

            // ── financial_transactions ───────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "financial_transactions",
                schema: "nexo",
                columns: table => new
                {
                    id                    = table.Column<Guid>(nullable: false),
                    tenant_id             = table.Column<Guid>(nullable: false),
                    financial_account_id  = table.Column<Guid>(nullable: false),
                    transaction_type      = table.Column<string>(maxLength: 20, nullable: false),
                    amount                = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    description           = table.Column<string>(maxLength: 500, nullable: false),
                    due_date              = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    paid_at               = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    status                = table.Column<string>(maxLength: 20, nullable: false),
                    reference_type        = table.Column<string>(maxLength: 50, nullable: true),
                    reference_id          = table.Column<Guid>(nullable: true),
                    created_by_user_id    = table.Column<Guid>(nullable: false),
                    created_at            = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at            = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_transactions", x => x.id);
                    table.ForeignKey("fk_financial_transactions_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_financial_transactions_accounts", x => x.financial_account_id,
                        principalSchema: "nexo", principalTable: "financial_accounts", principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex("ix_financial_transactions_tenant_id_status", "financial_transactions",
                new[] { "tenant_id", "status" }, schema: "nexo");
            migrationBuilder.CreateIndex("ix_financial_transactions_tenant_id_due_date", "financial_transactions",
                new[] { "tenant_id", "due_date" }, schema: "nexo");
            migrationBuilder.CreateIndex("ix_financial_transactions_tenant_id_account", "financial_transactions",
                new[] { "tenant_id", "financial_account_id" }, schema: "nexo");
            migrationBuilder.CreateIndex("ix_financial_transactions_reference", "financial_transactions",
                new[] { "reference_type", "reference_id" }, schema: "nexo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "financial_transactions", schema: "nexo");
            migrationBuilder.DropTable(name: "financial_accounts", schema: "nexo");
            migrationBuilder.DropTable(name: "cash_movements", schema: "nexo");
            migrationBuilder.DropTable(name: "sale_items", schema: "nexo");
            migrationBuilder.DropTable(name: "sales", schema: "nexo");
            migrationBuilder.DropTable(name: "cash_sessions", schema: "nexo");
            migrationBuilder.DropTable(name: "stock_movements", schema: "nexo");
            migrationBuilder.DropTable(name: "stock_items", schema: "nexo");
            migrationBuilder.DropTable(name: "products", schema: "nexo");
            migrationBuilder.DropTable(name: "categories", schema: "nexo");
            migrationBuilder.DropTable(name: "suppliers", schema: "nexo");
            migrationBuilder.DropTable(name: "customers", schema: "nexo");
        }
    }
}
