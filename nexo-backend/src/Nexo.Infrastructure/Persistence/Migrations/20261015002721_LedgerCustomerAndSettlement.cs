using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LedgerCustomerAndSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "customer_id",
                schema: "nexo",
                table: "financial_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_transaction_id",
                schema: "nexo",
                table: "financial_transactions",
                type: "uuid",
                nullable: true);

            // Re-run the PR 49 à-vista backfill: cash sales confirmed by the old container while that
            // migration ran reached only the drawer. Idempotent per payment — what is already in the
            // livro (same sale, amount and description, or the same id) is skipped.
            migrationBuilder.Sql(@"
INSERT INTO nexo.financial_transactions
    (id, tenant_id, financial_account_id, transaction_type, category, amount, description,
     competence_date, due_date, paid_at, status, store_id, reference_type, reference_id,
     reverses_transaction_id, created_by_user_id, created_at, updated_at)
SELECT sp.id, sp.tenant_id, acc.id, 'Receivable', 'Revenue', sp.amount,
       'Venda #' || s.number || ' — ' || CASE sp.method
            WHEN 'Cash' THEN 'Dinheiro' WHEN 'Pix' THEN 'Pix' WHEN 'Debit' THEN 'Cartão de débito'
            WHEN 'Credit' THEN 'Cartão de crédito' WHEN 'Transfer' THEN 'Transferência'
            WHEN 'Check' THEN 'Cheque' ELSE 'Outro' END,
       COALESCE(s.confirmed_at, sp.created_at), COALESCE(s.confirmed_at, sp.created_at),
       COALESCE(s.confirmed_at, sp.created_at), 'Paid', s.store_id, 'Sale', s.id, NULL,
       COALESCE(s.sold_by_user_id, '00000000-0000-0000-0000-000000000000'::uuid), sp.created_at, sp.created_at
  FROM nexo.sale_payments sp
  JOIN nexo.sales s ON s.id = sp.sale_id
  JOIN LATERAL (SELECT a.id FROM nexo.financial_accounts a
                 WHERE a.tenant_id = sp.tenant_id AND a.is_active AND a.account_type = 'Receivable'
                 ORDER BY a.code LIMIT 1) acc ON true
 WHERE sp.type = 'Cash' AND sp.amount > 0
   AND s.status NOT IN ('Draft', 'Cancelled')
   AND NOT EXISTS (SELECT 1 FROM nexo.financial_transactions x WHERE x.id = sp.id)
   AND NOT EXISTS (
        SELECT 1 FROM nexo.financial_transactions ft
         WHERE ft.reference_type = 'Sale' AND ft.reference_id = s.id AND ft.amount = sp.amount
           AND ft.description = 'Venda #' || s.number || ' — ' || CASE sp.method
                WHEN 'Cash' THEN 'Dinheiro' WHEN 'Pix' THEN 'Pix' WHEN 'Debit' THEN 'Cartão de débito'
                WHEN 'Credit' THEN 'Cartão de crédito' WHEN 'Transfer' THEN 'Transferência'
                WHEN 'Check' THEN 'Cheque' ELSE 'Outro' END);

-- Cash refunds of devoluções left only the drawer: their revenue deduction joins the livro.
INSERT INTO nexo.financial_transactions
    (id, tenant_id, financial_account_id, transaction_type, category, amount, description,
     competence_date, due_date, paid_at, status, store_id, reference_type, reference_id,
     reverses_transaction_id, created_by_user_id, created_at, updated_at)
SELECT m.id, m.tenant_id, acc.id, 'Payable', 'RevenueDeduction', m.amount, m.description,
       m.created_at, m.created_at, m.created_at, 'Paid', s.store_id, 'SaleReturn', r.id, NULL,
       m.created_by_user_id, m.created_at, m.created_at
  FROM nexo.cash_movements m
  JOIN nexo.sale_returns r ON r.id = m.reference_id
  JOIN nexo.sales s ON s.id = r.sale_id
  JOIN LATERAL (SELECT a.id FROM nexo.financial_accounts a
                 WHERE a.tenant_id = m.tenant_id AND a.is_active AND a.account_type = 'Payable'
                 ORDER BY a.code LIMIT 1) acc ON true
 WHERE m.movement_type = 'SaleRefund' AND m.reference_type = 'SaleReturn' AND m.amount > 0
   AND NOT EXISTS (SELECT 1 FROM nexo.financial_transactions x WHERE x.id = m.id)
   AND NOT EXISTS (SELECT 1 FROM nexo.financial_transactions x
                    WHERE x.reference_type = 'SaleReturn' AND x.reference_id = r.id);");

            // Customer of each título, from its record of origin
            migrationBuilder.Sql(@"
UPDATE nexo.financial_transactions ft SET customer_id = s.customer_id
  FROM nexo.sales s
 WHERE ft.customer_id IS NULL AND s.customer_id IS NOT NULL
   AND ft.reference_type IN ('Sale', 'SaleCancellation') AND ft.reference_id = s.id;
UPDATE nexo.financial_transactions ft SET customer_id = s.customer_id
  FROM nexo.sale_returns r JOIN nexo.sales s ON s.id = r.sale_id
 WHERE ft.customer_id IS NULL AND s.customer_id IS NOT NULL
   AND ft.reference_type = 'SaleReturn' AND ft.reference_id = r.id;
UPDATE nexo.financial_transactions ft SET customer_id = p.customer_id
  FROM nexo.svc_payments p
 WHERE ft.customer_id IS NULL
   AND ft.reference_type IN ('SvcPayment', 'SvcPaymentVoid') AND ft.reference_id = p.id;");

            migrationBuilder.CreateIndex(
                name: "IX_financial_transactions_tenant_id_customer_id",
                schema: "nexo",
                table: "financial_transactions",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_transactions_tenant_id_transaction_type_status_du~",
                schema: "nexo",
                table: "financial_transactions",
                columns: new[] { "tenant_id", "transaction_type", "status", "due_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_financial_transactions_tenant_id_customer_id",
                schema: "nexo",
                table: "financial_transactions");

            migrationBuilder.DropIndex(
                name: "IX_financial_transactions_tenant_id_transaction_type_status_du~",
                schema: "nexo",
                table: "financial_transactions");

            migrationBuilder.DropColumn(
                name: "customer_id",
                schema: "nexo",
                table: "financial_transactions");

            migrationBuilder.DropColumn(
                name: "parent_transaction_id",
                schema: "nexo",
                table: "financial_transactions");
        }
    }
}
