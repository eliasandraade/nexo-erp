using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FiscalFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "consumer_document",
                schema: "nexo",
                table: "sales",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cest",
                schema: "nexo",
                table: "products",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cfop",
                schema: "nexo",
                table: "products",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "fiscal_origin",
                schema: "nexo",
                table: "products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ncm",
                schema: "nexo",
                table: "products",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_situation",
                schema: "nexo",
                table: "products",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fiscal_profiles",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trade_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cnpj = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    state_registration = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    municipal_registration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    tax_regime = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    complement = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    district = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    city_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    city_ibge_code = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    zip_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_fiscal_profiles_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_profiles_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_profiles_store_id",
                schema: "nexo",
                table: "fiscal_profiles",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_profiles_tenant_store",
                schema: "nexo",
                table: "fiscal_profiles",
                columns: new[] { "tenant_id", "store_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fiscal_profiles",
                schema: "nexo");

            migrationBuilder.DropColumn(
                name: "consumer_document",
                schema: "nexo",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "cest",
                schema: "nexo",
                table: "products");

            migrationBuilder.DropColumn(
                name: "cfop",
                schema: "nexo",
                table: "products");

            migrationBuilder.DropColumn(
                name: "fiscal_origin",
                schema: "nexo",
                table: "products");

            migrationBuilder.DropColumn(
                name: "ncm",
                schema: "nexo",
                table: "products");

            migrationBuilder.DropColumn(
                name: "tax_situation",
                schema: "nexo",
                table: "products");
        }
    }
}
