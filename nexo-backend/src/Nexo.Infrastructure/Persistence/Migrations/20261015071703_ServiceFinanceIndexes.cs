using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceFinanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_svc_package_usages_tenant_id",
                schema: "nexo",
                table: "svc_package_usages");

            migrationBuilder.CreateIndex(
                name: "ix_svc_payments_voided_at",
                schema: "nexo",
                table: "svc_payments",
                column: "voided_at",
                filter: "voided_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_svc_package_usages_store_created",
                schema: "nexo",
                table: "svc_package_usages",
                columns: new[] { "tenant_id", "store_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_svc_customer_packages_store_created",
                schema: "nexo",
                table: "svc_customer_packages",
                columns: new[] { "tenant_id", "store_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_svc_commission_entries_store_recognized",
                schema: "nexo",
                table: "svc_commission_entries",
                columns: new[] { "tenant_id", "store_id", "recognized_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_svc_payments_voided_at",
                schema: "nexo",
                table: "svc_payments");

            migrationBuilder.DropIndex(
                name: "ix_svc_package_usages_store_created",
                schema: "nexo",
                table: "svc_package_usages");

            migrationBuilder.DropIndex(
                name: "ix_svc_customer_packages_store_created",
                schema: "nexo",
                table: "svc_customer_packages");

            migrationBuilder.DropIndex(
                name: "ix_svc_commission_entries_store_recognized",
                schema: "nexo",
                table: "svc_commission_entries");

            migrationBuilder.CreateIndex(
                name: "IX_svc_package_usages_tenant_id",
                schema: "nexo",
                table: "svc_package_usages",
                column: "tenant_id");
        }
    }
}
