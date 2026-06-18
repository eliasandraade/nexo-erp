using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "svc_orders",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: true),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_svc_orders_appointments",
                        column: x => x.appointment_id,
                        principalSchema: "nexo",
                        principalTable: "svc_appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_orders_customers",
                        column: x => x.customer_id,
                        principalSchema: "nexo",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_orders_professionals",
                        column: x => x.professional_id,
                        principalSchema: "nexo",
                        principalTable: "svc_professionals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_orders_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_orders_subjects",
                        column: x => x.subject_id,
                        principalSchema: "nexo",
                        principalTable: "svc_subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_orders_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "svc_order_items",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description_snapshot = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(12,3)", nullable: false),
                    unit_price_snapshot = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    commission_percent_snapshot = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_order_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_svc_order_items_catalog_items",
                        column: x => x.catalog_item_id,
                        principalSchema: "nexo",
                        principalTable: "svc_catalog_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_order_items_order",
                        column: x => x.order_id,
                        principalSchema: "nexo",
                        principalTable: "svc_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_svc_order_items_professionals",
                        column: x => x.professional_id,
                        principalSchema: "nexo",
                        principalTable: "svc_professionals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_order_items_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_order_items_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_svc_order_items_catalog_item_id",
                schema: "nexo",
                table: "svc_order_items",
                column: "catalog_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_order_items_order_id",
                schema: "nexo",
                table: "svc_order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_svc_order_items_professional_id",
                schema: "nexo",
                table: "svc_order_items",
                column: "professional_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_order_items_store_id",
                schema: "nexo",
                table: "svc_order_items",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_svc_order_items_tenant_id",
                schema: "nexo",
                table: "svc_order_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_orders_customer_id",
                schema: "nexo",
                table: "svc_orders",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_orders_professional_id",
                schema: "nexo",
                table: "svc_orders",
                column: "professional_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_orders_store_id",
                schema: "nexo",
                table: "svc_orders",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_orders_subject_id",
                schema: "nexo",
                table: "svc_orders",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_orders_tenant_store_status",
                schema: "nexo",
                table: "svc_orders",
                columns: new[] { "tenant_id", "store_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_svc_orders_appointment_id",
                schema: "nexo",
                table: "svc_orders",
                column: "appointment_id",
                unique: true,
                filter: "appointment_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "svc_order_items",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "svc_orders",
                schema: "nexo");
        }
    }
}
