using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [Migration("20260402000002_CoreHardening")]
    public partial class CoreHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── sales: drop payment_method, add confirmed_at / cancelled_at ─────────
            migrationBuilder.DropColumn(
                name:   "payment_method",
                table:  "sales",
                schema: "nexo");

            migrationBuilder.AddColumn<DateTime>(
                name:       "confirmed_at",
                table:      "sales",
                schema:     "nexo",
                type:       "timestamptz",
                nullable:   true);

            migrationBuilder.AddColumn<DateTime>(
                name:       "cancelled_at",
                table:      "sales",
                schema:     "nexo",
                type:       "timestamptz",
                nullable:   true);

            // ── sale_items: add cost_price snapshot ───────────────────────────────────
            migrationBuilder.AddColumn<decimal>(
                name:       "cost_price",
                table:      "sale_items",
                schema:     "nexo",
                type:       "numeric(18,4)",
                nullable:   false,
                defaultValue: 0m);

            // ── cash_sessions: replace (tenant_id, status) index with
            //                  (tenant_id, opened_by_user_id, status) ────────────────
            migrationBuilder.DropIndex(
                name:   "ix_cash_sessions_tenant_id_status",
                table:  "cash_sessions",
                schema: "nexo");

            migrationBuilder.CreateIndex(
                name:    "ix_cash_sessions_tenant_user_status",
                table:   "cash_sessions",
                schema:  "nexo",
                columns: new[] { "tenant_id", "opened_by_user_id", "status" });

            // ── sale_payments (new) ───────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name:   "sale_payments",
                schema: "nexo",
                columns: table => new
                {
                    id         = table.Column<Guid>(nullable: false),
                    tenant_id  = table.Column<Guid>(nullable: false),
                    sale_id    = table.Column<Guid>(nullable: false),
                    method     = table.Column<string>(maxLength: 20, nullable: false),
                    type       = table.Column<string>(maxLength: 20, nullable: false),
                    amount     = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    due_date   = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_payments", x => x.id);
                    table.ForeignKey("fk_sale_payments_tenants", x => x.tenant_id,
                        principalSchema: "nexo", principalTable: "tenants", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_sale_payments_sales", x => x.sale_id,
                        principalSchema: "nexo", principalTable: "sales", principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name:    "ix_sale_payments_tenant_id_sale_id",
                table:   "sale_payments",
                schema:  "nexo",
                columns: new[] { "tenant_id", "sale_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "sale_payments", schema: "nexo");

            migrationBuilder.DropIndex(
                name:   "ix_cash_sessions_tenant_user_status",
                table:  "cash_sessions",
                schema: "nexo");

            migrationBuilder.CreateIndex(
                name:    "ix_cash_sessions_tenant_id_status",
                table:   "cash_sessions",
                schema:  "nexo",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.DropColumn(name: "cost_price",    table: "sale_items", schema: "nexo");
            migrationBuilder.DropColumn(name: "confirmed_at",  table: "sales",      schema: "nexo");
            migrationBuilder.DropColumn(name: "cancelled_at",  table: "sales",      schema: "nexo");

            migrationBuilder.AddColumn<string>(
                name:      "payment_method",
                table:     "sales",
                schema:    "nexo",
                maxLength: 20,
                nullable:  true);
        }
    }
}
