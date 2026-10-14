using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                schema: "nexo",
                table: "sales",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cancelled_by_user_id",
                schema: "nexo",
                table: "sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "returned_amount",
                schema: "nexo",
                table: "sales",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "sale_returns",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    refund_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    refund_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: true),
                    cash_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_returns", x => x.id);
                    table.CheckConstraint("ck_sale_returns_refund_nonnegative", "refund_amount >= 0");
                    table.ForeignKey(
                        name: "FK_sale_returns_sales_sale_id",
                        column: x => x.sale_id,
                        principalSchema: "nexo",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_returns_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sale_return_items",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_return_items", x => x.id);
                    table.CheckConstraint("ck_sale_return_items_quantity_positive", "quantity > 0");
                    table.ForeignKey(
                        name: "FK_sale_return_items_sale_items_sale_item_id",
                        column: x => x.sale_item_id,
                        principalSchema: "nexo",
                        principalTable: "sale_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_return_items_sale_returns_sale_return_id",
                        column: x => x.sale_return_id,
                        principalSchema: "nexo",
                        principalTable: "sale_returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sale_return_items_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sale_return_items_sale_item_id",
                schema: "nexo",
                table: "sale_return_items",
                column: "sale_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_sale_return_items_sale_return_id",
                schema: "nexo",
                table: "sale_return_items",
                column: "sale_return_id");

            migrationBuilder.CreateIndex(
                name: "IX_sale_return_items_tenant_id_sale_item_id",
                schema: "nexo",
                table: "sale_return_items",
                columns: new[] { "tenant_id", "sale_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sale_returns_sale_id",
                schema: "nexo",
                table: "sale_returns",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "IX_sale_returns_tenant_id_sale_id",
                schema: "nexo",
                table: "sale_returns",
                columns: new[] { "tenant_id", "sale_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sale_returns_idempotency",
                schema: "nexo",
                table: "sale_returns",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sale_return_items",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "sale_returns",
                schema: "nexo");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                schema: "nexo",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "cancelled_by_user_id",
                schema: "nexo",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "returned_amount",
                schema: "nexo",
                table: "sales");
        }
    }
}
