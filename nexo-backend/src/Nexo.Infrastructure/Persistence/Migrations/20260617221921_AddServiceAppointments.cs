using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceAppointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "svc_appointments",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    starts_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    ends_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    price_snapshot = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_appointments", x => x.id);
                    table.ForeignKey(
                        name: "fk_svc_appointments_catalog_items",
                        column: x => x.catalog_item_id,
                        principalSchema: "nexo",
                        principalTable: "svc_catalog_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_appointments_customers",
                        column: x => x.customer_id,
                        principalSchema: "nexo",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_appointments_professionals",
                        column: x => x.professional_id,
                        principalSchema: "nexo",
                        principalTable: "svc_professionals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_appointments_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_appointments_subjects",
                        column: x => x.subject_id,
                        principalSchema: "nexo",
                        principalTable: "svc_subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_appointments_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_svc_appointments_catalog_item_id",
                schema: "nexo",
                table: "svc_appointments",
                column: "catalog_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_appointments_customer_id",
                schema: "nexo",
                table: "svc_appointments",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_svc_appointments_professional_id",
                schema: "nexo",
                table: "svc_appointments",
                column: "professional_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_appointments_professional_starts",
                schema: "nexo",
                table: "svc_appointments",
                columns: new[] { "tenant_id", "store_id", "professional_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_svc_appointments_store_id",
                schema: "nexo",
                table: "svc_appointments",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_svc_appointments_subject_id",
                schema: "nexo",
                table: "svc_appointments",
                column: "subject_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "svc_appointments",
                schema: "nexo");
        }
    }
}
