using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnifiedFinancialLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category",
                schema: "nexo",
                table: "financial_transactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "competence_date",
                schema: "nexo",
                table: "financial_transactions",
                type: "timestamptz",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "reverses_transaction_id",
                schema: "nexo",
                table: "financial_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "store_id",
                schema: "nexo",
                table: "financial_transactions",
                type: "uuid",
                nullable: true);

            // ── Classify the lançamentos that already exist ─────────────────────────────
            migrationBuilder.Sql(@"
UPDATE nexo.financial_transactions SET category = CASE
    WHEN reference_type IN ('Sale', 'SvcPayment')                              THEN 'Revenue'
    WHEN reference_type IN ('SaleCancellation', 'SaleReturn', 'SvcPaymentVoid') THEN 'RevenueDeduction'
    WHEN reference_type IN ('Purchase', 'PurchaseCancellation')                THEN 'Purchase'
    WHEN reference_type = 'SvcCommissionPayout'                                THEN 'Commission'
    WHEN transaction_type = 'Receivable'                                       THEN 'OtherIncome'
    ELSE 'OperatingExpense' END;

-- Competência: when the fact happened. Module entries were written at the fact; Service at the
-- payment; a purchase at its receipt; a manual entry is best described by its due date.
UPDATE nexo.financial_transactions SET competence_date = CASE
    WHEN reference_type LIKE 'Svc%' AND paid_at IS NOT NULL THEN paid_at
    WHEN reference_type IS NULL OR reference_type NOT IN
         ('Sale', 'SaleCancellation', 'SaleReturn', 'Purchase', 'PurchaseCancellation') THEN due_date
    ELSE created_at END;
UPDATE nexo.financial_transactions ft SET competence_date = p.received_at
  FROM nexo.ret_purchases p
 WHERE ft.reference_type = 'Purchase' AND ft.reference_id = p.id AND p.received_at IS NOT NULL;

-- Counter-entries point at what they reverse
UPDATE nexo.financial_transactions c SET reverses_transaction_id = o.id
  FROM nexo.financial_transactions o
 WHERE c.reference_type = 'SvcPaymentVoid' AND o.reference_type = 'SvcPayment'
   AND o.reference_id = c.reference_id AND o.status = 'Paid';
UPDATE nexo.financial_transactions c SET reverses_transaction_id = o.id
  FROM nexo.financial_transactions o
 WHERE c.reference_type = 'SaleCancellation' AND o.reference_type = 'Sale'
   AND o.reference_id = c.reference_id AND c.description = 'Estorno — ' || o.description;");

            // ── Bring à-vista sale money into the livro ───────────────────────────────
            // Until now cash (and, before PR 43, every à-vista method) only reached the drawer.
            // One settled Revenue entry per payment of every sale still standing; a payment already
            // mirrored (Pix/card since PR 43) is skipped. The entry reuses the payment's id, so the
            // backfill is idempotent with its own rows and Down removes exactly them.
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
   AND (sp.method = 'Cash' OR NOT EXISTS (
            SELECT 1 FROM nexo.financial_transactions ft
             WHERE ft.reference_type = 'Sale' AND ft.reference_id = s.id AND ft.status = 'Paid'));

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
   AND NOT EXISTS (SELECT 1 FROM nexo.financial_transactions x WHERE x.id = m.id);");

            // ── Store dimension, from each record of origin ───────────────────────────
            migrationBuilder.Sql(@"
UPDATE nexo.financial_transactions ft SET store_id = s.store_id
  FROM nexo.sales s
 WHERE ft.store_id IS NULL AND ft.reference_type IN ('Sale', 'SaleCancellation') AND ft.reference_id = s.id;
UPDATE nexo.financial_transactions ft SET store_id = s.store_id
  FROM nexo.sale_returns r JOIN nexo.sales s ON s.id = r.sale_id
 WHERE ft.store_id IS NULL AND ft.reference_type = 'SaleReturn' AND ft.reference_id = r.id;
UPDATE nexo.financial_transactions ft SET store_id = p.store_id
  FROM nexo.ret_purchases p
 WHERE ft.store_id IS NULL AND ft.reference_type IN ('Purchase', 'PurchaseCancellation') AND ft.reference_id = p.id;
UPDATE nexo.financial_transactions ft SET store_id = p.store_id
  FROM nexo.svc_payments p
 WHERE ft.store_id IS NULL AND ft.reference_type IN ('SvcPayment', 'SvcPaymentVoid') AND ft.reference_id = p.id;
UPDATE nexo.financial_transactions ft SET store_id = p.store_id
  FROM nexo.svc_commission_payouts p
 WHERE ft.store_id IS NULL AND ft.reference_type = 'SvcCommissionPayout' AND ft.reference_id = p.id;");

            migrationBuilder.CreateIndex(
                name: "IX_financial_transactions_reverses_transaction_id",
                schema: "nexo",
                table: "financial_transactions",
                column: "reverses_transaction_id");

            migrationBuilder.CreateIndex(
                name: "IX_financial_transactions_tenant_id_competence_date",
                schema: "nexo",
                table: "financial_transactions",
                columns: new[] { "tenant_id", "competence_date" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_transactions_tenant_id_paid_at",
                schema: "nexo",
                table: "financial_transactions",
                columns: new[] { "tenant_id", "paid_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove exactly the backfilled entries (they carry the id of their payment / drawer movement)
            migrationBuilder.Sql(@"
DELETE FROM nexo.financial_transactions
 WHERE id IN (SELECT id FROM nexo.sale_payments) OR id IN (SELECT id FROM nexo.cash_movements);");

            migrationBuilder.DropIndex(
                name: "IX_financial_transactions_reverses_transaction_id",
                schema: "nexo",
                table: "financial_transactions");

            migrationBuilder.DropIndex(
                name: "IX_financial_transactions_tenant_id_competence_date",
                schema: "nexo",
                table: "financial_transactions");

            migrationBuilder.DropIndex(
                name: "IX_financial_transactions_tenant_id_paid_at",
                schema: "nexo",
                table: "financial_transactions");

            migrationBuilder.DropColumn(
                name: "category",
                schema: "nexo",
                table: "financial_transactions");

            migrationBuilder.DropColumn(
                name: "competence_date",
                schema: "nexo",
                table: "financial_transactions");

            migrationBuilder.DropColumn(
                name: "reverses_transaction_id",
                schema: "nexo",
                table: "financial_transactions");

            migrationBuilder.DropColumn(
                name: "store_id",
                schema: "nexo",
                table: "financial_transactions");
        }
    }
}
