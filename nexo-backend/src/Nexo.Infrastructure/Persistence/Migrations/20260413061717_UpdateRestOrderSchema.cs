using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateRestOrderSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_rest_orders_tenant_store_table",
                schema: "nexo",
                table: "rest_orders");

            migrationBuilder.AlterColumn<Guid>(
                name: "table_id",
                schema: "nexo",
                table: "rest_orders",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<decimal>(
                name: "couvert_amount",
                schema: "nexo",
                table: "rest_orders",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "order_type",
                schema: "nexo",
                table: "rest_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "DineIn");

            migrationBuilder.AddColumn<int>(
                name: "party_size",
                schema: "nexo",
                table: "rest_orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "service_fee_amount",
                schema: "nexo",
                table: "rest_orders",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "ix_rest_orders_one_active_per_table",
                schema: "nexo",
                table: "rest_orders",
                columns: new[] { "tenant_id", "store_id", "table_id" },
                unique: true,
                filter: "table_id IS NOT NULL AND status NOT IN ('Closed','Paid','Cancelled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_rest_orders_one_active_per_table",
                schema: "nexo",
                table: "rest_orders");

            migrationBuilder.DropColumn(
                name: "couvert_amount",
                schema: "nexo",
                table: "rest_orders");

            migrationBuilder.DropColumn(
                name: "order_type",
                schema: "nexo",
                table: "rest_orders");

            migrationBuilder.DropColumn(
                name: "party_size",
                schema: "nexo",
                table: "rest_orders");

            migrationBuilder.DropColumn(
                name: "service_fee_amount",
                schema: "nexo",
                table: "rest_orders");

            migrationBuilder.AlterColumn<Guid>(
                name: "table_id",
                schema: "nexo",
                table: "rest_orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_rest_orders_tenant_store_table",
                schema: "nexo",
                table: "rest_orders",
                columns: new[] { "tenant_id", "store_id", "table_id" });
        }
    }
}
