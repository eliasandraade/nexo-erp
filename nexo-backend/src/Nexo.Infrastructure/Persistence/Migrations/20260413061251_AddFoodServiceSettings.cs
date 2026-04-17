using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFoodServiceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "food_service_settings",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "restaurant"),
                    couvert_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    couvert_price_per_person = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    couvert_automatic = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    service_fee_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    service_fee_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    order_types_enabled = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "DineIn,Counter,Takeaway"),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_food_service_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_food_service_settings_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_food_service_settings_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_food_service_settings_store_id",
                schema: "nexo",
                table: "food_service_settings",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_food_service_settings_tenant_id",
                schema: "nexo",
                table: "food_service_settings",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_food_service_settings_tenant_store",
                schema: "nexo",
                table: "food_service_settings",
                columns: new[] { "tenant_id", "store_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "food_service_settings",
                schema: "nexo");
        }
    }
}
