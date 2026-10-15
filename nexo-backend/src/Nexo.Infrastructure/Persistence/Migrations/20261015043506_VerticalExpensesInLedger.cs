using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VerticalExpensesInLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Restaurant expenses and confirmed Build movements were outside the livro. Bring the
            // existing ones in, reusing the record's id (Down removes exactly these rows). Dates are
            // business days: noon in Brazil, like everything the application posts.
            migrationBuilder.Sql(@"
INSERT INTO nexo.financial_transactions
    (id, tenant_id, financial_account_id, transaction_type, category, amount, description,
     competence_date, due_date, paid_at, status, store_id, reference_type, reference_id,
     created_by_user_id, created_at, updated_at)
SELECT e.id, e.tenant_id, acc.id, 'Payable',
       CASE WHEN lower(e.category) LIKE '%imposto%' OR lower(e.category) LIKE '%taxa%' THEN 'Tax'
            WHEN lower(e.category) LIKE '%folha%' OR lower(e.category) LIKE '%sal_rio%' THEN 'Payroll'
            WHEN lower(trim(e.category)) IN ('outros', 'outro') THEN 'OtherExpense'
            ELSE 'OperatingExpense' END,
       round(e.amount, 2),
       left(e.category || ' — ' || e.description, 500),
       (e.competence_date + time '12:00') AT TIME ZONE 'America/Sao_Paulo',
       (coalesce(e.payment_date, e.competence_date) + time '12:00') AT TIME ZONE 'America/Sao_Paulo',
       CASE WHEN e.payment_date IS NULL THEN NULL
            ELSE (e.payment_date + time '12:00') AT TIME ZONE 'America/Sao_Paulo' END,
       CASE WHEN e.payment_date IS NULL THEN 'Pending' ELSE 'Paid' END,
       e.store_id, 'RestExpense', e.id,
       '00000000-0000-0000-0000-000000000000'::uuid, e.created_at, e.created_at
  FROM nexo.rest_expenses e
  JOIN LATERAL (SELECT a.id FROM nexo.financial_accounts a
                 WHERE a.tenant_id = e.tenant_id AND a.is_active AND a.account_type = 'Payable'
                 ORDER BY a.code LIMIT 1) acc ON true
 WHERE round(e.amount, 2) > 0
   AND NOT EXISTS (SELECT 1 FROM nexo.financial_transactions x
                    WHERE x.id = e.id OR (x.reference_type = 'RestExpense' AND x.reference_id = e.id));

-- Confirmed movements (status 2), except transfers (nature 2): settled on their date.
-- direction 1 = In (outras entradas), 2 = Out (expense nature 1 = despesa operacional, else outras saídas)
INSERT INTO nexo.financial_transactions
    (id, tenant_id, financial_account_id, transaction_type, category, amount, description,
     competence_date, due_date, paid_at, status, store_id, supplier_id, reference_type, reference_id,
     created_by_user_id, created_at, updated_at)
SELECT m.id, m.tenant_id, acc.id,
       CASE WHEN m.direction = 1 THEN 'Receivable' ELSE 'Payable' END,
       CASE WHEN m.direction = 1 THEN 'OtherIncome'
            WHEN m.nature = 1 THEN 'OperatingExpense' ELSE 'OtherExpense' END,
       round(m.amount, 2), left(m.description, 500),
       (m.date + time '12:00') AT TIME ZONE 'America/Sao_Paulo',
       (m.date + time '12:00') AT TIME ZONE 'America/Sao_Paulo',
       (m.date + time '12:00') AT TIME ZONE 'America/Sao_Paulo',
       'Paid',
       CASE WHEN m.context_type = 2 THEN m.context_id ELSE NULL END,
       CASE WHEN m.direction = 2 THEN m.supplier_id ELSE NULL END,
       'FinancialMovement', m.id, m.created_by, m.created_at, m.created_at
  FROM nexo.int_movements m
  JOIN LATERAL (SELECT a.id FROM nexo.financial_accounts a
                 WHERE a.tenant_id = m.tenant_id AND a.is_active
                   AND a.account_type = CASE WHEN m.direction = 1 THEN 'Receivable' ELSE 'Payable' END
                 ORDER BY a.code LIMIT 1) acc ON true
 WHERE m.status = 2 AND m.nature <> 2 AND round(m.amount, 2) > 0
   AND NOT EXISTS (SELECT 1 FROM nexo.financial_transactions x
                    WHERE x.id = m.id OR (x.reference_type = 'FinancialMovement' AND x.reference_id = m.id));");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE FROM nexo.financial_transactions
 WHERE (reference_type = 'RestExpense' AND id IN (SELECT id FROM nexo.rest_expenses))
    OR (reference_type = 'FinancialMovement' AND id IN (SELECT id FROM nexo.int_movements));");

        }
    }
}
