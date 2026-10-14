using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashSessionClosingDifference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "closing_notes",
                schema: "nexo",
                table: "cash_sessions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "difference_amount",
                schema: "nexo",
                table: "cash_sessions",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "expected_balance",
                schema: "nexo",
                table: "cash_sessions",
                type: "numeric(18,2)",
                nullable: true);

            // Backfill the history: expected and difference of sessions closed before this change,
            // computed from their own movements (only fills the new, empty columns). Legacy Pix/card
            // receipts — recorded as SaleReceipt before PR 43 — are left out of the drawer.
            migrationBuilder.Sql(@"
                UPDATE nexo.cash_sessions s
                SET expected_balance  = s.opening_balance + COALESCE(m.net, 0),
                    difference_amount = s.closing_balance - (s.opening_balance + COALESCE(m.net, 0))
                FROM (
                    SELECT cs.id,
                           (SELECT SUM(CASE
                                       WHEN mv.movement_type = 'Deposit'                     THEN mv.amount
                                       -- before PR 43 Pix/card also became 'SaleReceipt', tagged
                                       -- with the method in the description: only physical cash counts
                                       WHEN mv.movement_type = 'SaleReceipt'
                                            AND mv.description !~ '— (Pix|Debit|Credit|Transfer|Check|Mixed|Other)$'
                                                                                             THEN mv.amount
                                       WHEN mv.movement_type IN ('Withdrawal', 'SaleRefund') THEN -mv.amount
                                       ELSE 0 END)
                            FROM nexo.cash_movements mv
                            WHERE mv.cash_session_id = cs.id) AS net
                    FROM nexo.cash_sessions cs
                ) m
                WHERE m.id = s.id
                  AND s.status = 'Closed'
                  AND s.closing_balance IS NOT NULL
                  AND s.expected_balance IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "closing_notes",
                schema: "nexo",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "difference_amount",
                schema: "nexo",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "expected_balance",
                schema: "nexo",
                table: "cash_sessions");
        }
    }
}
