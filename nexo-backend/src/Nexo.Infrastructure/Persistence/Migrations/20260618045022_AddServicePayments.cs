using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServicePayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "svc_payments",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    paid_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    external_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    voided_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_svc_payments_customer_packages",
                        column: x => x.customer_package_id,
                        principalSchema: "nexo",
                        principalTable: "svc_customer_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_payments_customers",
                        column: x => x.customer_id,
                        principalSchema: "nexo",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_payments_orders",
                        column: x => x.order_id,
                        principalSchema: "nexo",
                        principalTable: "svc_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_payments_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_payments_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_svc_payments_customer_id",
                schema: "nexo",
                table: "svc_payments",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_payments_customer_package_id",
                schema: "nexo",
                table: "svc_payments",
                column: "customer_package_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_payments_order_id",
                schema: "nexo",
                table: "svc_payments",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_payments_paid_at",
                schema: "nexo",
                table: "svc_payments",
                column: "paid_at");

            migrationBuilder.CreateIndex(
                name: "ix_svc_payments_store_id",
                schema: "nexo",
                table: "svc_payments",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_payments_tenant_store_status",
                schema: "nexo",
                table: "svc_payments",
                columns: new[] { "tenant_id", "store_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "svc_payments",
                schema: "nexo");
        }
    }
}
