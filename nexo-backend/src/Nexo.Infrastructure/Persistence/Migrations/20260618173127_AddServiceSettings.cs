using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "svc_settings",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    preset_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_svc_settings_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_settings_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_svc_settings_store_id",
                schema: "nexo",
                table: "svc_settings",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_settings_tenant_store_unique",
                schema: "nexo",
                table: "svc_settings",
                columns: new[] { "tenant_id", "store_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "svc_settings",
                schema: "nexo");
        }
    }
}
