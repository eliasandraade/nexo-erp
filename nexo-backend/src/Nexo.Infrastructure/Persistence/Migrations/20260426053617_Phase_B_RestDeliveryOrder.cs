using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase_B_RestDeliveryOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rest_delivery_orders",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<int>(type: "integer", nullable: false),
                    tracking_token = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_order_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    external_event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    raw_payload = table.Column<string>(type: "jsonb", nullable: true),
                    channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    order_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    customer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customer_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    customer_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery_address_json = table.Column<string>(type: "jsonb", nullable: true),
                    delivery_fee = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    estimated_minutes = table.Column<int>(type: "integer", nullable: true),
                    rider_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    rider_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    rest_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    received_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    accepted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    ready_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    dispatched_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    delivered_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rest_delivery_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_rest_delivery_orders_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rest_delivery_orders_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rest_delivery_order_items",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_product_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    product_name_snapshot = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    unit_price_snapshot = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rest_delivery_order_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_rest_delivery_order_items_rest_delivery_orders_delivery_ord~",
                        column: x => x.delivery_order_id,
                        principalSchema: "nexo",
                        principalTable: "rest_delivery_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_rest_delivery_order_items_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rest_delivery_order_item_modifiers",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modifier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_modifier_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    label_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    price_snapshot = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rest_delivery_order_item_modifiers", x => x.id);
                    table.ForeignKey(
                        name: "FK_rest_delivery_order_item_modifiers_rest_delivery_order_item~",
                        column: x => x.delivery_order_item_id,
                        principalSchema: "nexo",
                        principalTable: "rest_delivery_order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_rest_delivery_order_item_modifiers_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rest_delivery_order_item_modifiers_item_id",
                schema: "nexo",
                table: "rest_delivery_order_item_modifiers",
                column: "delivery_order_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_rest_delivery_order_item_modifiers_tenant_id",
                schema: "nexo",
                table: "rest_delivery_order_item_modifiers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_rest_delivery_order_items_delivery_order_id",
                schema: "nexo",
                table: "rest_delivery_order_items",
                column: "delivery_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_rest_delivery_order_items_tenant_id",
                schema: "nexo",
                table: "rest_delivery_order_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_rest_delivery_orders_external_dedup",
                schema: "nexo",
                table: "rest_delivery_orders",
                columns: new[] { "tenant_id", "store_id", "channel", "external_order_id" },
                filter: "external_order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_rest_delivery_orders_store_channel",
                schema: "nexo",
                table: "rest_delivery_orders",
                columns: new[] { "tenant_id", "store_id", "channel" });

            migrationBuilder.CreateIndex(
                name: "IX_rest_delivery_orders_store_id",
                schema: "nexo",
                table: "rest_delivery_orders",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_rest_delivery_orders_store_number",
                schema: "nexo",
                table: "rest_delivery_orders",
                columns: new[] { "tenant_id", "store_id", "order_number" });

            migrationBuilder.CreateIndex(
                name: "ix_rest_delivery_orders_store_status",
                schema: "nexo",
                table: "rest_delivery_orders",
                columns: new[] { "tenant_id", "store_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_rest_delivery_orders_tenant_id",
                schema: "nexo",
                table: "rest_delivery_orders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_rest_delivery_orders_tracking_token",
                schema: "nexo",
                table: "rest_delivery_orders",
                column: "tracking_token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rest_delivery_order_item_modifiers",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "rest_delivery_order_items",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "rest_delivery_orders",
                schema: "nexo");
        }
    }
}
