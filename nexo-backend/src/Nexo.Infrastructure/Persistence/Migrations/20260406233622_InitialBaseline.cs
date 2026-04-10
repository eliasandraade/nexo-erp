using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "nexo");

            migrationBuilder.CreateTable(
                name: "audit_records",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    actor_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "user"),
                    entity_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "module_definitions",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    stripe_product_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    stripe_price_monthly = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    stripe_price_quarterly = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    stripe_price_semiannual = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    stripe_price_annual = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    stripe_price_lifetime = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    price_monthly = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    price_quarterly = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    price_semiannual = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    price_annual = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    price_lifetime = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_module_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "platform_users",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    company_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trade_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tax_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    business_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    stripe_customer_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    trial_ends_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "app_settings",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_settings_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    operation_settings_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    inventory_settings_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    commission_settings_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    pos_settings_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    system_settings_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    TenantId1 = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_settings", x => x.id);
                    table.ForeignKey(
                        name: "FK_app_settings_tenants_TenantId1",
                        column: x => x.TenantId1,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_app_settings_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    parent_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.id);
                    table.ForeignKey(
                        name: "FK_categories_categories_parent_category_id",
                        column: x => x.parent_category_id,
                        principalSchema: "nexo",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_categories_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trade_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    document_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    document_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    whatsapp = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    address_json = table.Column<string>(type: "jsonb", nullable: true),
                    credit_limit = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.id);
                    table.ForeignKey(
                        name: "FK_customers_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "financial_accounts",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    account_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    parent_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_accounts", x => x.id);
                    table.ForeignKey(
                        name: "FK_financial_accounts_financial_accounts_parent_account_id",
                        column: x => x.parent_account_id,
                        principalSchema: "nexo",
                        principalTable: "financial_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_accounts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "module_subscriptions",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    stripe_subscription_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    stripe_price_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    plan_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    current_period_start = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    current_period_end = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    cancel_at_period_end = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    canceled_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    granted_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId1 = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_module_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "FK_module_subscriptions_platform_users_granted_by_id",
                        column: x => x.granted_by_id,
                        principalSchema: "nexo",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_module_subscriptions_tenants_TenantId1",
                        column: x => x.TenantId1,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_module_subscriptions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rest_areas",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rest_areas", x => x.id);
                    table.ForeignKey(
                        name: "fk_rest_areas_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ret_price_lists",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ret_price_lists", x => x.id);
                    table.ForeignKey(
                        name: "fk_ret_price_lists_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trade_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    document_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    document_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    contact_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    address_json = table.Column<string>(type: "jsonb", nullable: true),
                    payment_terms_days = table.Column<int>(type: "integer", nullable: true),
                    bank_info_json = table.Column<string>(type: "jsonb", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.id);
                    table.ForeignKey(
                        name: "FK_suppliers_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    login = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    require_password_change = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_access_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    password_changed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    TenantId1 = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                    table.ForeignKey(
                        name: "FK_users_tenants_TenantId1",
                        column: x => x.TenantId1,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_users_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    barcode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    cost_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    sale_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    track_stock = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    min_stock_quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    max_stock_quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                    table.ForeignKey(
                        name: "FK_products_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "nexo",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_products_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "financial_transactions",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    due_date = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    paid_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reference_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_transactions", x => x.id);
                    table.ForeignKey(
                        name: "FK_financial_transactions_financial_accounts_financial_account~",
                        column: x => x.financial_account_id,
                        principalSchema: "nexo",
                        principalTable: "financial_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_transactions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rest_tables",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rest_tables", x => x.id);
                    table.ForeignKey(
                        name: "fk_rest_tables_areas",
                        column: x => x.area_id,
                        principalSchema: "nexo",
                        principalTable: "rest_areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rest_tables_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ret_customer_price_lists",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ret_customer_price_lists", x => x.id);
                    table.ForeignKey(
                        name: "fk_ret_customer_price_lists_customers",
                        column: x => x.customer_id,
                        principalSchema: "nexo",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ret_customer_price_lists_price_lists",
                        column: x => x.price_list_id,
                        principalSchema: "nexo",
                        principalTable: "ret_price_lists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ret_customer_price_lists_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ret_purchases",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    invoice_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    received_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ret_purchases", x => x.id);
                    table.ForeignKey(
                        name: "fk_ret_purchases_suppliers",
                        column: x => x.supplier_id,
                        principalSchema: "nexo",
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ret_purchases_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cash_sessions",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    opened_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    opening_balance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    closing_balance = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    opened_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    closed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_cash_sessions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_cash_sessions_users_closed_by_user_id",
                        column: x => x.closed_by_user_id,
                        principalSchema: "nexo",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cash_sessions_users_opened_by_user_id",
                        column: x => x.opened_by_user_id,
                        principalSchema: "nexo",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rest_recipe_cards",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    yield = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    yield_unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rest_recipe_cards", x => x.id);
                    table.ForeignKey(
                        name: "fk_rest_recipe_cards_products",
                        column: x => x.product_id,
                        principalSchema: "nexo",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rest_recipe_cards_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ret_price_list_items",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ret_price_list_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_ret_price_list_items_ret_price_lists_price_list_id",
                        column: x => x.price_list_id,
                        principalSchema: "nexo",
                        principalTable: "ret_price_lists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ret_price_list_items_products",
                        column: x => x.product_id,
                        principalSchema: "nexo",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ret_price_list_items_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_items",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    reserved_quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    last_movement_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_stock_items_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "nexo",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_stock_items_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    quantity_before = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    quantity_after = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    reference_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_price_snapshot = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.id);
                    table.ForeignKey(
                        name: "FK_stock_movements_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "nexo",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_movements_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ret_purchase_items",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ret_purchase_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_ret_purchase_items_ret_purchases_purchase_id",
                        column: x => x.purchase_id,
                        principalSchema: "nexo",
                        principalTable: "ret_purchases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ret_purchase_items_products",
                        column: x => x.product_id,
                        principalSchema: "nexo",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ret_purchase_items_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cash_movements",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    reference_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_movements", x => x.id);
                    table.ForeignKey(
                        name: "FK_cash_movements_cash_sessions_cash_session_id",
                        column: x => x.cash_session_id,
                        principalSchema: "nexo",
                        principalTable: "cash_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cash_movements_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sold_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    paid_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales", x => x.id);
                    table.ForeignKey(
                        name: "FK_sales_cash_sessions_cash_session_id",
                        column: x => x.cash_session_id,
                        principalSchema: "nexo",
                        principalTable: "cash_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "nexo",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sales_users_sold_by_user_id",
                        column: x => x.sold_by_user_id,
                        principalSchema: "nexo",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rest_recipe_ingredients",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipe_card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rest_recipe_ingredients", x => x.id);
                    table.ForeignKey(
                        name: "FK_rest_recipe_ingredients_rest_recipe_cards_recipe_card_id",
                        column: x => x.recipe_card_id,
                        principalSchema: "nexo",
                        principalTable: "rest_recipe_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rest_recipe_ingredients_products",
                        column: x => x.ingredient_product_id,
                        principalSchema: "nexo",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rest_recipe_ingredients_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rest_orders",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    waiter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    opened_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    closed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rest_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_rest_orders_sales",
                        column: x => x.sale_id,
                        principalSchema: "nexo",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rest_orders_tables",
                        column: x => x.table_id,
                        principalSchema: "nexo",
                        principalTable: "rest_tables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rest_orders_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sale_items",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    cost_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_sale_items_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "nexo",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_items_sales_sale_id",
                        column: x => x.sale_id,
                        principalSchema: "nexo",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sale_items_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sale_payments",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    due_date = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_payments", x => x.id);
                    table.ForeignKey(
                        name: "FK_sale_payments_sales_sale_id",
                        column: x => x.sale_id,
                        principalSchema: "nexo",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sale_payments_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rest_order_items",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sent_to_kitchen_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    prepared_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    delivered_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    RestOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rest_order_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_rest_order_items_rest_orders_RestOrderId",
                        column: x => x.RestOrderId,
                        principalSchema: "nexo",
                        principalTable: "rest_orders",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_rest_order_items_rest_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "nexo",
                        principalTable: "rest_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rest_order_items_products",
                        column: x => x.product_id,
                        principalSchema: "nexo",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rest_order_items_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_app_settings_tenant_id",
                schema: "nexo",
                table: "app_settings",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_app_settings_TenantId1",
                schema: "nexo",
                table: "app_settings",
                column: "TenantId1");

            migrationBuilder.CreateIndex(
                name: "IX_audit_records_action_type",
                schema: "nexo",
                table: "audit_records",
                column: "action_type");

            migrationBuilder.CreateIndex(
                name: "IX_audit_records_actor_id",
                schema: "nexo",
                table: "audit_records",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "IX_audit_records_entity_type_entity_id",
                schema: "nexo",
                table: "audit_records",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_records_tenant_id_created_at",
                schema: "nexo",
                table: "audit_records",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_cash_session_id",
                schema: "nexo",
                table: "cash_movements",
                column: "cash_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_tenant_id_cash_session_id",
                schema: "nexo",
                table: "cash_movements",
                columns: new[] { "tenant_id", "cash_session_id" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_sessions_closed_by_user_id",
                schema: "nexo",
                table: "cash_sessions",
                column: "closed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_cash_sessions_opened_by_user_id",
                schema: "nexo",
                table: "cash_sessions",
                column: "opened_by_user_id");

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

            migrationBuilder.CreateIndex(
                name: "IX_categories_parent_category_id",
                schema: "nexo",
                table: "categories",
                column: "parent_category_id");

            migrationBuilder.CreateIndex(
                name: "IX_categories_tenant_id",
                schema: "nexo",
                table: "categories",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_categories_tenant_id_name",
                schema: "nexo",
                table: "categories",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "IX_customers_tenant_id",
                schema: "nexo",
                table: "customers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_customers_tenant_id_document_number",
                schema: "nexo",
                table: "customers",
                columns: new[] { "tenant_id", "document_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customers_tenant_id_name",
                schema: "nexo",
                table: "customers",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_accounts_parent_account_id",
                schema: "nexo",
                table: "financial_accounts",
                column: "parent_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_financial_accounts_tenant_id",
                schema: "nexo",
                table: "financial_accounts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_financial_accounts_tenant_id_code",
                schema: "nexo",
                table: "financial_accounts",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_transactions_financial_account_id",
                schema: "nexo",
                table: "financial_transactions",
                column: "financial_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_financial_transactions_reference_type_reference_id",
                schema: "nexo",
                table: "financial_transactions",
                columns: new[] { "reference_type", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_transactions_tenant_id_due_date",
                schema: "nexo",
                table: "financial_transactions",
                columns: new[] { "tenant_id", "due_date" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_transactions_tenant_id_financial_account_id",
                schema: "nexo",
                table: "financial_transactions",
                columns: new[] { "tenant_id", "financial_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_transactions_tenant_id_status",
                schema: "nexo",
                table: "financial_transactions",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_module_definitions_key",
                schema: "nexo",
                table: "module_definitions",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_module_subscriptions_granted_by_id",
                schema: "nexo",
                table: "module_subscriptions",
                column: "granted_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_module_subscriptions_status_current_period_end",
                schema: "nexo",
                table: "module_subscriptions",
                columns: new[] { "status", "current_period_end" });

            migrationBuilder.CreateIndex(
                name: "IX_module_subscriptions_tenant_id",
                schema: "nexo",
                table: "module_subscriptions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_module_subscriptions_tenant_id_module_key",
                schema: "nexo",
                table: "module_subscriptions",
                columns: new[] { "tenant_id", "module_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_module_subscriptions_TenantId1",
                schema: "nexo",
                table: "module_subscriptions",
                column: "TenantId1");

            migrationBuilder.CreateIndex(
                name: "IX_platform_users_email",
                schema: "nexo",
                table: "platform_users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_category_id",
                schema: "nexo",
                table: "products",
                column: "category_id");

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
                name: "ix_rest_areas_tenant_id",
                schema: "nexo",
                table: "rest_areas",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_rest_areas_tenant_id_name",
                schema: "nexo",
                table: "rest_areas",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rest_order_items_order_id",
                schema: "nexo",
                table: "rest_order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_rest_order_items_product_id",
                schema: "nexo",
                table: "rest_order_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_rest_order_items_RestOrderId",
                schema: "nexo",
                table: "rest_order_items",
                column: "RestOrderId");

            migrationBuilder.CreateIndex(
                name: "ix_rest_order_items_tenant_order",
                schema: "nexo",
                table: "rest_order_items",
                columns: new[] { "tenant_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rest_order_items_tenant_product",
                schema: "nexo",
                table: "rest_order_items",
                columns: new[] { "tenant_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rest_order_items_tenant_status",
                schema: "nexo",
                table: "rest_order_items",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_rest_orders_sale_id",
                schema: "nexo",
                table: "rest_orders",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "IX_rest_orders_table_id",
                schema: "nexo",
                table: "rest_orders",
                column: "table_id");

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
                name: "IX_rest_recipe_cards_product_id",
                schema: "nexo",
                table: "rest_recipe_cards",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_rest_recipe_cards_tenant_product",
                schema: "nexo",
                table: "rest_recipe_cards",
                columns: new[] { "tenant_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rest_recipe_ingredients_card_product",
                schema: "nexo",
                table: "rest_recipe_ingredients",
                columns: new[] { "recipe_card_id", "ingredient_product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rest_recipe_ingredients_ingredient_product_id",
                schema: "nexo",
                table: "rest_recipe_ingredients",
                column: "ingredient_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_rest_recipe_ingredients_tenant_card",
                schema: "nexo",
                table: "rest_recipe_ingredients",
                columns: new[] { "tenant_id", "recipe_card_id" });

            migrationBuilder.CreateIndex(
                name: "IX_rest_tables_area_id",
                schema: "nexo",
                table: "rest_tables",
                column: "area_id");

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
                name: "IX_ret_customer_price_lists_customer_id",
                schema: "nexo",
                table: "ret_customer_price_lists",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_ret_customer_price_lists_price_list_id",
                schema: "nexo",
                table: "ret_customer_price_lists",
                column: "price_list_id");

            migrationBuilder.CreateIndex(
                name: "ix_ret_customer_price_lists_tenant_customer",
                schema: "nexo",
                table: "ret_customer_price_lists",
                columns: new[] { "tenant_id", "customer_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ret_price_list_items_list_product",
                schema: "nexo",
                table: "ret_price_list_items",
                columns: new[] { "price_list_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ret_price_list_items_product_id",
                schema: "nexo",
                table: "ret_price_list_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_ret_price_list_items_tenant_id_product",
                schema: "nexo",
                table: "ret_price_list_items",
                columns: new[] { "tenant_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ret_price_lists_tenant_id",
                schema: "nexo",
                table: "ret_price_lists",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ret_price_lists_tenant_id_is_default",
                schema: "nexo",
                table: "ret_price_lists",
                columns: new[] { "tenant_id", "is_default" });

            migrationBuilder.CreateIndex(
                name: "IX_ret_purchase_items_product_id",
                schema: "nexo",
                table: "ret_purchase_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_ret_purchase_items_purchase_id",
                schema: "nexo",
                table: "ret_purchase_items",
                column: "purchase_id");

            migrationBuilder.CreateIndex(
                name: "ix_ret_purchase_items_tenant_id_product_id",
                schema: "nexo",
                table: "ret_purchase_items",
                columns: new[] { "tenant_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ret_purchase_items_tenant_id_purchase_id",
                schema: "nexo",
                table: "ret_purchase_items",
                columns: new[] { "tenant_id", "purchase_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ret_purchases_supplier_id",
                schema: "nexo",
                table: "ret_purchases",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_ret_purchases_tenant_id_created_at",
                schema: "nexo",
                table: "ret_purchases",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ret_purchases_tenant_id_number",
                schema: "nexo",
                table: "ret_purchases",
                columns: new[] { "tenant_id", "purchase_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ret_purchases_tenant_id_status",
                schema: "nexo",
                table: "ret_purchases",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_ret_purchases_tenant_id_supplier",
                schema: "nexo",
                table: "ret_purchases",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sale_items_product_id",
                schema: "nexo",
                table: "sale_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_sale_items_sale_id",
                schema: "nexo",
                table: "sale_items",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "IX_sale_items_tenant_id_product_id",
                schema: "nexo",
                table: "sale_items",
                columns: new[] { "tenant_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sale_items_tenant_id_sale_id",
                schema: "nexo",
                table: "sale_items",
                columns: new[] { "tenant_id", "sale_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sale_payments_sale_id",
                schema: "nexo",
                table: "sale_payments",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "IX_sale_payments_tenant_id_sale_id",
                schema: "nexo",
                table: "sale_payments",
                columns: new[] { "tenant_id", "sale_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_cash_session_id",
                schema: "nexo",
                table: "sales",
                column: "cash_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_customer_id",
                schema: "nexo",
                table: "sales",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_sold_by_user_id",
                schema: "nexo",
                table: "sales",
                column: "sold_by_user_id");

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
                name: "IX_stock_items_product_id",
                schema: "nexo",
                table: "stock_items",
                column: "product_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_items_tenant_id_product_id",
                schema: "nexo",
                table: "stock_items",
                columns: new[] { "tenant_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_product_id",
                schema: "nexo",
                table: "stock_movements",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_reference_type_reference_id",
                schema: "nexo",
                table: "stock_movements",
                columns: new[] { "reference_type", "reference_id" });

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
                name: "IX_suppliers_tenant_id",
                schema: "nexo",
                table: "suppliers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_tenant_id_document_number",
                schema: "nexo",
                table: "suppliers",
                columns: new[] { "tenant_id", "document_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_slug",
                schema: "nexo",
                table: "tenants",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_status",
                schema: "nexo",
                table: "tenants",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_tenants_stripe_customer_id",
                schema: "nexo",
                table: "tenants",
                column: "stripe_customer_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_tax_id",
                schema: "nexo",
                table: "tenants",
                column: "tax_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_tenant_id",
                schema: "nexo",
                table: "users",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_tenant_id_email",
                schema: "nexo",
                table: "users",
                columns: new[] { "tenant_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_tenant_id_login",
                schema: "nexo",
                table: "users",
                columns: new[] { "tenant_id", "login" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_TenantId1",
                schema: "nexo",
                table: "users",
                column: "TenantId1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_settings",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "audit_records",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "cash_movements",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "financial_transactions",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "module_definitions",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "module_subscriptions",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "rest_order_items",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "rest_recipe_ingredients",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "ret_customer_price_lists",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "ret_price_list_items",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "ret_purchase_items",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "sale_items",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "sale_payments",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "stock_items",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "stock_movements",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "financial_accounts",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "platform_users",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "rest_orders",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "rest_recipe_cards",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "ret_price_lists",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "ret_purchases",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "sales",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "rest_tables",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "products",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "suppliers",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "cash_sessions",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "rest_areas",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "users",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "nexo");
        }
    }
}
