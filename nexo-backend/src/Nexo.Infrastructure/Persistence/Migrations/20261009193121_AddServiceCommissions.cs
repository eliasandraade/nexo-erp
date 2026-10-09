using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceCommissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_svc_package_usages_order_id",
                schema: "nexo",
                table: "svc_package_usages",
                newName: "ix_svc_package_usages_order_id");

            migrationBuilder.AddColumn<Guid>(
                name: "appointment_id",
                schema: "nexo",
                table: "svc_package_usages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "base_amount_snapshot",
                schema: "nexo",
                table: "svc_package_usages",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "commission_percent_snapshot",
                schema: "nexo",
                table: "svc_package_usages",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "professional_id",
                schema: "nexo",
                table: "svc_package_usages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "commission_percent_snapshot",
                schema: "nexo",
                table: "svc_appointments",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "svc_commission_payouts",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    period_end = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    entry_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    paid_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_commission_payouts", x => x.id);
                    table.CheckConstraint("ck_svc_commission_payouts_paid_at", "(status = 'Paid') = (paid_at IS NOT NULL)");
                    table.CheckConstraint("ck_svc_commission_payouts_period", "period_start <= period_end");
                    table.CheckConstraint("ck_svc_commission_payouts_totals", "total_amount > 0 AND entry_count > 0");
                    table.ForeignKey(
                        name: "fk_svc_commission_payouts_professionals",
                        column: x => x.professional_id,
                        principalSchema: "nexo",
                        principalTable: "svc_professionals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_commission_payouts_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_commission_payouts_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "svc_commission_entries",
                schema: "nexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    commission_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    commission_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    recognized_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    payout_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_svc_commission_entries", x => x.id);
                    table.CheckConstraint("ck_svc_commission_entries_amounts", "base_amount >= 0 AND commission_amount >= 0 AND commission_percent > 0 AND commission_percent <= 100");
                    table.ForeignKey(
                        name: "fk_svc_commission_entries_customers",
                        column: x => x.customer_id,
                        principalSchema: "nexo",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_commission_entries_payouts",
                        column: x => x.payout_id,
                        principalSchema: "nexo",
                        principalTable: "svc_commission_payouts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_commission_entries_professionals",
                        column: x => x.professional_id,
                        principalSchema: "nexo",
                        principalTable: "svc_professionals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_commission_entries_stores",
                        column: x => x.store_id,
                        principalSchema: "nexo",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_svc_commission_entries_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "nexo",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_svc_package_usages_appointment_id",
                schema: "nexo",
                table: "svc_package_usages",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "IX_svc_package_usages_professional_id",
                schema: "nexo",
                table: "svc_package_usages",
                column: "professional_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_commission_entries_customer_id",
                schema: "nexo",
                table: "svc_commission_entries",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_commission_entries_payout_id",
                schema: "nexo",
                table: "svc_commission_entries",
                column: "payout_id");

            migrationBuilder.CreateIndex(
                name: "ix_svc_commission_entries_professional_recognized",
                schema: "nexo",
                table: "svc_commission_entries",
                columns: new[] { "professional_id", "recognized_at" });

            migrationBuilder.CreateIndex(
                name: "ix_svc_commission_entries_store_id",
                schema: "nexo",
                table: "svc_commission_entries",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ux_svc_commission_entries_source",
                schema: "nexo",
                table: "svc_commission_entries",
                columns: new[] { "tenant_id", "source", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_svc_commission_payouts_professional_period",
                schema: "nexo",
                table: "svc_commission_payouts",
                columns: new[] { "professional_id", "period_end" });

            migrationBuilder.CreateIndex(
                name: "ix_svc_commission_payouts_status",
                schema: "nexo",
                table: "svc_commission_payouts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_svc_commission_payouts_store_id",
                schema: "nexo",
                table: "svc_commission_payouts",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_svc_commission_payouts_tenant_id",
                schema: "nexo",
                table: "svc_commission_payouts",
                column: "tenant_id");

            migrationBuilder.AddForeignKey(
                name: "fk_svc_package_usages_appointments",
                schema: "nexo",
                table: "svc_package_usages",
                column: "appointment_id",
                principalSchema: "nexo",
                principalTable: "svc_appointments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_svc_package_usages_professionals",
                schema: "nexo",
                table: "svc_package_usages",
                column: "professional_id",
                principalSchema: "nexo",
                principalTable: "svc_professionals",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_svc_package_usages_appointments",
                schema: "nexo",
                table: "svc_package_usages");

            migrationBuilder.DropForeignKey(
                name: "fk_svc_package_usages_professionals",
                schema: "nexo",
                table: "svc_package_usages");

            migrationBuilder.DropTable(
                name: "svc_commission_entries",
                schema: "nexo");

            migrationBuilder.DropTable(
                name: "svc_commission_payouts",
                schema: "nexo");

            migrationBuilder.DropIndex(
                name: "ix_svc_package_usages_appointment_id",
                schema: "nexo",
                table: "svc_package_usages");

            migrationBuilder.DropIndex(
                name: "IX_svc_package_usages_professional_id",
                schema: "nexo",
                table: "svc_package_usages");

            migrationBuilder.DropColumn(
                name: "appointment_id",
                schema: "nexo",
                table: "svc_package_usages");

            migrationBuilder.DropColumn(
                name: "base_amount_snapshot",
                schema: "nexo",
                table: "svc_package_usages");

            migrationBuilder.DropColumn(
                name: "commission_percent_snapshot",
                schema: "nexo",
                table: "svc_package_usages");

            migrationBuilder.DropColumn(
                name: "professional_id",
                schema: "nexo",
                table: "svc_package_usages");

            migrationBuilder.DropColumn(
                name: "commission_percent_snapshot",
                schema: "nexo",
                table: "svc_appointments");

            migrationBuilder.RenameIndex(
                name: "ix_svc_package_usages_order_id",
                schema: "nexo",
                table: "svc_package_usages",
                newName: "IX_svc_package_usages_order_id");
        }
    }
}
