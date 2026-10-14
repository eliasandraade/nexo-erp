using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PriceListValidityAndPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "priority",
                schema: "nexo",
                table: "ret_price_lists",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "valid_from",
                schema: "nexo",
                table: "ret_price_lists",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "valid_until",
                schema: "nexo",
                table: "ret_price_lists",
                type: "timestamptz",
                nullable: true);

            // A tenant could end up with two default lists (an inactive default kept its flag).
            // Keep the most recently created one as default before enforcing uniqueness.
            migrationBuilder.Sql(@"
UPDATE nexo.ret_price_lists p
   SET is_default = false
 WHERE p.is_default
   AND EXISTS (SELECT 1 FROM nexo.ret_price_lists q
                WHERE q.tenant_id = p.tenant_id AND q.is_default
                  AND (q.created_at > p.created_at OR (q.created_at = p.created_at AND q.id > p.id)));");

            migrationBuilder.CreateIndex(
                name: "ux_ret_price_lists_one_default",
                schema: "nexo",
                table: "ret_price_lists",
                column: "tenant_id",
                unique: true,
                filter: "is_default");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_ret_price_lists_one_default",
                schema: "nexo",
                table: "ret_price_lists");

            migrationBuilder.DropColumn(
                name: "priority",
                schema: "nexo",
                table: "ret_price_lists");

            migrationBuilder.DropColumn(
                name: "valid_from",
                schema: "nexo",
                table: "ret_price_lists");

            migrationBuilder.DropColumn(
                name: "valid_until",
                schema: "nexo",
                table: "ret_price_lists");
        }
    }
}
