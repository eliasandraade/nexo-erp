using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRestOrderItemModifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_rest_order_items_order_id",
                schema: "nexo",
                table: "rest_order_items",
                newName: "ix_rest_order_items_order_id");

            migrationBuilder.CreateTable(
                name: "rest_order_item_modifiers",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modifier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label_snapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    price_snapshot = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rest_order_item_modifiers", x => x.id);
                    table.ForeignKey(
                        name: "fk_rest_order_item_modifiers_items",
                        column: x => x.order_item_id,
                        principalSchema: "nexo",
                        principalTable: "rest_order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rest_order_item_modifiers_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rest_order_item_modifiers_item",
                schema: "nexo",
                table: "rest_order_item_modifiers",
                columns: new[] { "tenant_id", "order_item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rest_order_item_modifiers_order_item_id",
                schema: "nexo",
                table: "rest_order_item_modifiers",
                column: "order_item_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rest_order_item_modifiers",
                schema: "nexo");

            migrationBuilder.RenameIndex(
                name: "ix_rest_order_items_order_id",
                schema: "nexo",
                table: "rest_order_items",
                newName: "IX_rest_order_items_order_id");
        }
    }
}
