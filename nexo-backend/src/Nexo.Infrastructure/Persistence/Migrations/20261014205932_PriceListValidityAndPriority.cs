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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
