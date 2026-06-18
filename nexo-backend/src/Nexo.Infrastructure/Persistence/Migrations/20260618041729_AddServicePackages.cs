using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServicePackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "svc_packages",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    validity_days = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_packages", x => x.id);
                    table.ForeignKey(
                        name: "fk_svc_packages_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_packages_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "svc_customer_packages",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    starts_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    price_snapshot = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_customer_packages", x => x.id);
                    table.ForeignKey(
                        name: "fk_svc_customer_packages_customers",
                        column: x => x.customer_id,
                        principalSchema: "nexo",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_customer_packages_packages",
                        column: x => x.package_id,
                        principalSchema: "nexo",
                        principalTable: "svc_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_customer_packages_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_customer_packages_subjects",
                        column: x => x.subject_id,
                        principalSchema: "nexo",
                        principalTable: "svc_subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_customer_packages_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "svc_package_items",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    included_quantity = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_package_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_svc_package_items_catalog_items",
                        column: x => x.catalog_item_id,
                        principalSchema: "nexo",
                        principalTable: "svc_catalog_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_package_items_package",
                        column: x => x.package_id,
                        principalSchema: "nexo",
                        principalTable: "svc_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_svc_package_items_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_package_items_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "svc_customer_package_items",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    total_quantity = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    remaining_quantity = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_customer_package_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_svc_customer_package_items_catalog_items",
                        column: x => x.catalog_item_id,
                        principalSchema: "nexo",
                        principalTable: "svc_catalog_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_customer_package_items_cp",
                        column: x => x.customer_package_id,
                        principalSchema: "nexo",
                        principalTable: "svc_customer_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_svc_customer_package_items_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_customer_package_items_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "svc_package_usages",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_package_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_package_usages", x => x.id);
                    table.ForeignKey(
                        name: "fk_svc_package_usages_catalog_items",
                        column: x => x.catalog_item_id,
                        principalSchema: "nexo",
                        principalTable: "svc_catalog_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_package_usages_cp",
                        column: x => x.customer_package_id,
                        principalSchema: "nexo",
                        principalTable: "svc_customer_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_package_usages_cp_item",
                        column: x => x.customer_package_item_id,
                        principalSchema: "nexo",
                        principalTable: "svc_customer_package_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_package_usages_order_items",
                        column: x => x.order_item_id,
                        principalSchema: "nexo",
                        principalTable: "svc_order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_package_usages_orders",
                        column: x => x.order_id,
                        principalSchema: "nexo",
                        principalTable: "svc_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_package_usages_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_package_usages_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_svc_customer_package_items_catalog_id",
                schema: "nexo",
                table: "svc_customer_package_items",
                column: "catalog_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_customer_package_items_cp_id",
                schema: "nexo",
                table: "svc_customer_package_items",
                column: "customer_package_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_customer_package_items_store_id",
                schema: "nexo",
                table: "svc_customer_package_items",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_svc_customer_package_items_tenant_id",
                schema: "nexo",
                table: "svc_customer_package_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_customer_packages_customer_id",
                schema: "nexo",
                table: "svc_customer_packages",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_customer_packages_package_id",
                schema: "nexo",
                table: "svc_customer_packages",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_customer_packages_store_id",
                schema: "nexo",
                table: "svc_customer_packages",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_customer_packages_subject_id",
                schema: "nexo",
                table: "svc_customer_packages",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_customer_packages_tenant_store_status",
                schema: "nexo",
                table: "svc_customer_packages",
                columns: new[] { "tenant_id", "store_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_svc_package_items_catalog_item_id",
                schema: "nexo",
                table: "svc_package_items",
                column: "catalog_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_package_items_package_id",
                schema: "nexo",
                table: "svc_package_items",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_package_items_store_id",
                schema: "nexo",
                table: "svc_package_items",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_svc_package_items_tenant_id",
                schema: "nexo",
                table: "svc_package_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_svc_package_usages_catalog_item_id",
                schema: "nexo",
                table: "svc_package_usages",
                column: "catalog_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_package_usages_cp_id",
                schema: "nexo",
                table: "svc_package_usages",
                column: "customer_package_id");

            migrationBuilder.CreateIndex(
                name: "IX_svc_package_usages_customer_package_item_id",
                schema: "nexo",
                table: "svc_package_usages",
                column: "customer_package_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_svc_package_usages_order_id",
                schema: "nexo",
                table: "svc_package_usages",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_svc_package_usages_order_item_id",
                schema: "nexo",
                table: "svc_package_usages",
                column: "order_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_package_usages_store_id",
                schema: "nexo",
                table: "svc_package_usages",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_svc_package_usages_tenant_id",
                schema: "nexo",
                table: "svc_package_usages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_packages_store_id",
                schema: "nexo",
                table: "svc_packages",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_packages_tenant_store_active",
                schema: "nexo",
                table: "svc_packages",
                columns: new[] { "tenant_id", "store_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "svc_package_items",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "svc_package_usages",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "svc_customer_package_items",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "svc_customer_packages",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "svc_packages",
                schema: "nexo");
        }
    }
}
