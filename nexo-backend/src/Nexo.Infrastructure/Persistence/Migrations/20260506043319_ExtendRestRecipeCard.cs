using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtendRestRecipeCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "assembly_notes",
                schema: "nexo",
                table: "rest_recipe_cards",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "has_prep",
                schema: "nexo",
                table: "rest_recipe_cards",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "image_url",
                schema: "nexo",
                table: "rest_recipe_cards",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "packaging_product_id",
                schema: "nexo",
                table: "rest_recipe_cards",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "prep_steps_json",
                schema: "nexo",
                table: "rest_recipe_cards",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "requires_packaging",
                schema: "nexo",
                table: "rest_recipe_cards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "total_prep_time_min",
                schema: "nexo",
                table: "rest_recipe_cards",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_rest_recipe_cards_packaging_product_id",
                schema: "nexo",
                table: "rest_recipe_cards",
                column: "packaging_product_id");

            migrationBuilder.AddForeignKey(
                name: "fk_rest_recipe_cards_packaging_product",
                schema: "nexo",
                table: "rest_recipe_cards",
                column: "packaging_product_id",
                principalSchema: "nexo",
                principalTable: "products",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_rest_recipe_cards_packaging_product",
                schema: "nexo",
                table: "rest_recipe_cards");

            migrationBuilder.DropIndex(
                name: "IX_rest_recipe_cards_packaging_product_id",
                schema: "nexo",
                table: "rest_recipe_cards");

            migrationBuilder.DropColumn(
                name: "assembly_notes",
                schema: "nexo",
                table: "rest_recipe_cards");

            migrationBuilder.DropColumn(
                name: "has_prep",
                schema: "nexo",
                table: "rest_recipe_cards");

            migrationBuilder.DropColumn(
                name: "image_url",
                schema: "nexo",
                table: "rest_recipe_cards");

            migrationBuilder.DropColumn(
                name: "packaging_product_id",
                schema: "nexo",
                table: "rest_recipe_cards");

            migrationBuilder.DropColumn(
                name: "prep_steps_json",
                schema: "nexo",
                table: "rest_recipe_cards");

            migrationBuilder.DropColumn(
                name: "requires_packaging",
                schema: "nexo",
                table: "rest_recipe_cards");

            migrationBuilder.DropColumn(
                name: "total_prep_time_min",
                schema: "nexo",
                table: "rest_recipe_cards");
        }
    }
}
