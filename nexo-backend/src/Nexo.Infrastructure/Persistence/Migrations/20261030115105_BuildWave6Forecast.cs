using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BuildWave6Forecast : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "weather_sensitive",
                schema: "nexo",
                table: "bld_stages",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bld_forecasts",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    generated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    methodology = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    forecast_delivery = table.Column<DateOnly>(type: "date", nullable: true),
                    planned_delivery = table.Column<DateOnly>(type: "date", nullable: true),
                    confidence = table.Column<decimal>(type: "numeric(4,3)", nullable: false),
                    factors = table.Column<string>(type: "json", nullable: false),
                    stages = table.Column<string>(type: "json", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bld_forecasts", x => x.id);
                    table.ForeignKey(
                        name: "fk_bld_forecasts_project",
                        column: x => x.project_id,
                        principalSchema: "nexo",
                        principalTable: "bld_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bld_forecasts_project_date",
                schema: "nexo",
                table: "bld_forecasts",
                columns: new[] { "project_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_bld_forecasts_project_generated",
                schema: "nexo",
                table: "bld_forecasts",
                columns: new[] { "project_id", "generated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bld_forecasts_tenant_id",
                schema: "nexo",
                table: "bld_forecasts",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bld_forecasts",
                schema: "nexo");

            migrationBuilder.DropColumn(
                name: "weather_sensitive",
                schema: "nexo",
                table: "bld_stages");
        }
    }
}
