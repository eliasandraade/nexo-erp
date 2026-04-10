using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [Migration("20260402000000_InitialMultiTenant")]
    public partial class InitialMultiTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "nexo");

            // ── Platform tables (no tenant_id) ────────────────────────────────

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "nexo",
                columns: table => new
                {
                    id              = table.Column<Guid>(nullable: false),
                    slug            = table.Column<string>(maxLength: 100, nullable: false),
                    company_name    = table.Column<string>(maxLength: 200, nullable: false),
                    trade_name      = table.Column<string>(maxLength: 200, nullable: true),
                    tax_id          = table.Column<string>(maxLength: 20, nullable: false),
                    email           = table.Column<string>(maxLength: 200, nullable: false),
                    phone           = table.Column<string>(maxLength: 30, nullable: true),
                    business_type   = table.Column<string>(maxLength: 50, nullable: false),
                    stripe_customer_id = table.Column<string>(maxLength: 100, nullable: true),
                    status          = table.Column<string>(maxLength: 20, nullable: false),
                    trial_ends_at   = table.Column<DateTime>(nullable: true),
                    created_at      = table.Column<DateTime>(nullable: false),
                    updated_at      = table.Column<DateTime>(nullable: false),
                },
                constraints: table => table.PrimaryKey("pk_tenants", x => x.id));

            migrationBuilder.CreateIndex(
                name: "ix_tenants_slug",
                schema: "nexo",
                table: "tenants",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenants_tax_id",
                schema: "nexo",
                table: "tenants",
                column: "tax_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenants_stripe_customer_id",
                schema: "nexo",
                table: "tenants",
                column: "stripe_customer_id",
                unique: true,
                filter: "stripe_customer_id IS NOT NULL");

            migrationBuilder.CreateTable(
                name: "platform_users",
                schema: "nexo",
                columns: table => new
                {
                    id            = table.Column<Guid>(nullable: false),
                    email         = table.Column<string>(maxLength: 200, nullable: false),
                    password_hash = table.Column<string>(maxLength: 200, nullable: false),
                    role          = table.Column<string>(maxLength: 50, nullable: false),
                    created_at    = table.Column<DateTime>(nullable: false),
                    updated_at    = table.Column<DateTime>(nullable: false),
                },
                constraints: table => table.PrimaryKey("pk_platform_users", x => x.id));

            migrationBuilder.CreateIndex(
                name: "ix_platform_users_email",
                schema: "nexo",
                table: "platform_users",
                column: "email",
                unique: true);

            migrationBuilder.CreateTable(
                name: "module_definitions",
                schema: "nexo",
                columns: table => new
                {
                    id                       = table.Column<Guid>(nullable: false),
                    key                      = table.Column<string>(maxLength: 100, nullable: false),
                    name                     = table.Column<string>(maxLength: 150, nullable: false),
                    version                  = table.Column<string>(maxLength: 20, nullable: false),
                    is_published             = table.Column<bool>(nullable: false),
                    stripe_price_id_monthly  = table.Column<string>(maxLength: 100, nullable: true),
                    stripe_price_id_annual   = table.Column<string>(maxLength: 100, nullable: true),
                    reference_price_monthly  = table.Column<decimal>(precision: 10, scale: 2, nullable: false),
                    reference_price_annual   = table.Column<decimal>(precision: 10, scale: 2, nullable: false),
                    created_at               = table.Column<DateTime>(nullable: false),
                    updated_at               = table.Column<DateTime>(nullable: false),
                },
                constraints: table => table.PrimaryKey("pk_module_definitions", x => x.id));

            migrationBuilder.CreateIndex(
                name: "ix_module_definitions_key",
                schema: "nexo",
                table: "module_definitions",
                column: "key",
                unique: true);

            migrationBuilder.CreateTable(
                name: "module_subscriptions",
                schema: "nexo",
                columns: table => new
                {
                    id                    = table.Column<Guid>(nullable: false),
                    tenant_id             = table.Column<Guid>(nullable: false),
                    module_key            = table.Column<string>(maxLength: 100, nullable: false),
                    status                = table.Column<string>(maxLength: 30, nullable: false),
                    plan_type             = table.Column<string>(maxLength: 30, nullable: false),
                    stripe_subscription_id = table.Column<string>(maxLength: 100, nullable: true),
                    current_period_start  = table.Column<DateTime>(nullable: true),
                    current_period_end    = table.Column<DateTime>(nullable: true),
                    granted_by_id         = table.Column<Guid>(nullable: true),
                    created_at            = table.Column<DateTime>(nullable: false),
                    updated_at            = table.Column<DateTime>(nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_module_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_module_subscriptions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_module_subscriptions_platform_users_granted_by_id",
                        column: x => x.granted_by_id,
                        principalSchema: "nexo",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_module_subscriptions_tenant_id_module_key",
                schema: "nexo",
                table: "module_subscriptions",
                columns: ["tenant_id", "module_key"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_module_subscriptions_tenant_id",
                schema: "nexo",
                table: "module_subscriptions",
                column: "tenant_id");

            // ── Tenant-scoped tables ──────────────────────────────────────────

            migrationBuilder.CreateTable(
                name: "users",
                schema: "nexo",
                columns: table => new
                {
                    id                      = table.Column<Guid>(nullable: false),
                    tenant_id               = table.Column<Guid>(nullable: false),
                    full_name               = table.Column<string>(maxLength: 150, nullable: false),
                    email                   = table.Column<string>(maxLength: 200, nullable: false),
                    login                   = table.Column<string>(maxLength: 50, nullable: false),
                    password_hash           = table.Column<string>(maxLength: 200, nullable: false),
                    phone                   = table.Column<string>(maxLength: 30, nullable: true),
                    role                    = table.Column<string>(maxLength: 30, nullable: false),
                    status                  = table.Column<string>(maxLength: 20, nullable: false),
                    require_password_change = table.Column<bool>(nullable: false),
                    notes                   = table.Column<string>(maxLength: 500, nullable: true),
                    last_access_at          = table.Column<DateTime>(nullable: true),
                    password_changed_at     = table.Column<DateTime>(nullable: true),
                    created_at              = table.Column<DateTime>(nullable: false),
                    updated_at              = table.Column<DateTime>(nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_users_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id_login",
                schema: "nexo",
                table: "users",
                columns: ["tenant_id", "login"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id_email",
                schema: "nexo",
                table: "users",
                columns: ["tenant_id", "email"],
                unique: true,
                filter: "email != ''");

            migrationBuilder.CreateTable(
                name: "app_settings",
                schema: "nexo",
                columns: table => new
                {
                    id                      = table.Column<Guid>(nullable: false),
                    tenant_id               = table.Column<Guid>(nullable: false),
                    company_settings_json   = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    operation_settings_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    inventory_settings_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    commission_settings_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    pos_settings_json       = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    system_settings_json    = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    created_at              = table.Column<DateTime>(nullable: false),
                    updated_at              = table.Column<DateTime>(nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_app_settings_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_app_settings_tenant_id",
                schema: "nexo",
                table: "app_settings",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateTable(
                name: "audit_records",
                schema: "nexo",
                columns: table => new
                {
                    id           = table.Column<Guid>(nullable: false),
                    tenant_id    = table.Column<Guid>(nullable: true),
                    action_type  = table.Column<string>(maxLength: 100, nullable: false),
                    severity     = table.Column<string>(maxLength: 20, nullable: false),
                    entity_type  = table.Column<string>(maxLength: 100, nullable: true),
                    entity_id    = table.Column<string>(maxLength: 100, nullable: true),
                    actor_id     = table.Column<string>(maxLength: 100, nullable: true),
                    actor_name   = table.Column<string>(maxLength: 150, nullable: true),
                    actor_type   = table.Column<string>(maxLength: 50, nullable: true),
                    description  = table.Column<string>(maxLength: 500, nullable: true),
                    ip_address   = table.Column<string>(maxLength: 50, nullable: true),
                    created_at   = table.Column<DateTime>(nullable: false),
                },
                constraints: table => table.PrimaryKey("pk_audit_records", x => x.id));

            migrationBuilder.CreateIndex(
                name: "ix_audit_records_tenant_id_created_at",
                schema: "nexo",
                table: "audit_records",
                columns: ["tenant_id", "created_at"]);

            migrationBuilder.CreateIndex(
                name: "ix_audit_records_action_type",
                schema: "nexo",
                table: "audit_records",
                column: "action_type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "audit_records",       schema: "nexo");
            migrationBuilder.DropTable(name: "app_settings",        schema: "nexo");
            migrationBuilder.DropTable(name: "users",               schema: "nexo");
            migrationBuilder.DropTable(name: "module_subscriptions", schema: "nexo");
            migrationBuilder.DropTable(name: "module_definitions",  schema: "nexo");
            migrationBuilder.DropTable(name: "platform_users",      schema: "nexo");
            migrationBuilder.DropTable(name: "tenants",             schema: "nexo");
        }
    }
}
