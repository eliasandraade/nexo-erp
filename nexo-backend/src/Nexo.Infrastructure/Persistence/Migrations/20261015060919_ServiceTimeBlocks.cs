using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceTimeBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "svc_time_blocks",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: true),
                    starts_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    ends_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_time_blocks", x => x.id);
                    table.ForeignKey(
                        name: "fk_svc_time_blocks_professionals",
                        column: x => x.professional_id,
                        principalSchema: "nexo",
                        principalTable: "svc_professionals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_svc_time_blocks_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_time_blocks_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_svc_time_blocks_professional_id",
                schema: "nexo",
                table: "svc_time_blocks",
                column: "professional_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_time_blocks_store_id",
                schema: "nexo",
                table: "svc_time_blocks",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_time_blocks_store_starts",
                schema: "nexo",
                table: "svc_time_blocks",
                columns: new[] { "tenant_id", "store_id", "starts_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "svc_time_blocks",
                schema: "nexo");
        }
    }
}
