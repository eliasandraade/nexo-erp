using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryZonesAndCoupons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "coupon_code",
                schema: "nexo",
                table: "rest_delivery_orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "discount_amount",
                schema: "nexo",
                table: "rest_delivery_orders",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "rest_coupons",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    discount_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    discount_value = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    min_order_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    min_delivery_fee = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    restrict_to_neighborhoods = table.Column<string>(type: "jsonb", nullable: true),
                    restrict_to_product_ids = table.Column<string>(type: "jsonb", nullable: true),
                    is_first_order_only = table.Column<bool>(type: "boolean", nullable: false),
                    restrict_to_customer_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    max_uses = table.Column<int>(type: "integer", nullable: true),
                    used_count = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    valid_until = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rest_coupons", x => x.id);
                    table.ForeignKey(
                        name: "fk_rest_coupons_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rest_coupons_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rest_delivery_zones",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    neighborhood = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    fee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rest_delivery_zones", x => x.id);
                    table.ForeignKey(
                        name: "fk_rest_delivery_zones_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rest_delivery_zones_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rest_coupon_usages",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    coupon_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    delivery_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    used_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rest_coupon_usages", x => x.id);
                    table.ForeignKey(
                        name: "fk_rest_coupon_usages_coupons",
                        column: x => x.coupon_id,
                        principalSchema: "nexo",
                        principalTable: "rest_coupons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rest_coupon_usages_delivery_orders",
                        column: x => x.delivery_order_id,
                        principalSchema: "nexo",
                        principalTable: "rest_delivery_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rest_coupon_usages_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rest_coupon_usages_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rest_coupon_usages_coupon",
                schema: "nexo",
                table: "rest_coupon_usages",
                column: "coupon_id");

            migrationBuilder.CreateIndex(
                name: "ix_rest_coupon_usages_coupon_phone",
                schema: "nexo",
                table: "rest_coupon_usages",
                columns: new[] { "coupon_id", "customer_phone" });

            migrationBuilder.CreateIndex(
                name: "IX_rest_coupon_usages_delivery_order_id",
                schema: "nexo",
                table: "rest_coupon_usages",
                column: "delivery_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_rest_coupon_usages_store_id",
                schema: "nexo",
                table: "rest_coupon_usages",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_rest_coupon_usages_tenant_id",
                schema: "nexo",
                table: "rest_coupon_usages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_rest_coupons_store",
                schema: "nexo",
                table: "rest_coupons",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rest_coupons_store_code",
                schema: "nexo",
                table: "rest_coupons",
                columns: new[] { "store_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rest_delivery_zones_store",
                schema: "nexo",
                table: "rest_delivery_zones",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rest_delivery_zones_store_neighborhood",
                schema: "nexo",
                table: "rest_delivery_zones",
                columns: new[] { "store_id", "neighborhood" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rest_coupon_usages",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "rest_delivery_zones",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "rest_coupons",
                schema: "nexo");

            migrationBuilder.DropColumn(
                name: "coupon_code",
                schema: "nexo",
                table: "rest_delivery_orders");

            migrationBuilder.DropColumn(
                name: "discount_amount",
                schema: "nexo",
                table: "rest_delivery_orders");
        }
    }
}
