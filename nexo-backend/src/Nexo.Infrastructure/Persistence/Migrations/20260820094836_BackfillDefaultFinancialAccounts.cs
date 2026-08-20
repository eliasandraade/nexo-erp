using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data-only migration: gives every EXISTING tenant the four default accounts (Caixa, Banco,
    /// Contas a Receber, Contas a Pagar).
    ///
    /// New tenants get them from DefaultFinancialAccountProvisioner at creation time, but tenants
    /// created before that provisioner existed have none — the demo seeder only ever covered the
    /// first tenant, and it does not run in Production. Without this backfill their first credit
    /// sale or Service payment fails looking up "Contas a Receber".
    ///
    /// Idempotent: inserts only the account types a tenant is missing, so re-running changes
    /// nothing and a tenant with a custom chart of accounts keeps it.
    /// </summary>
    public partial class BackfillDefaultFinancialAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // account_type is persisted as the enum NAME (HasConversion<string>), and the
            // (tenant_id, code) unique index is respected because a tenant missing a type is also
            // missing that type's default code.
            migrationBuilder.Sql(@"
                INSERT INTO nexo.financial_accounts
                    (id, tenant_id, code, name, account_type, is_active, created_at, updated_at)
                SELECT gen_random_uuid(), t.id, d.code, d.name, d.account_type, true, now(), now()
                FROM nexo.tenants t
                CROSS JOIN (VALUES
                    ('1.1', 'Caixa',            'Cash'),
                    ('1.2', 'Banco',            'Bank'),
                    ('2.1', 'Contas a Receber', 'Receivable'),
                    ('3.1', 'Contas a Pagar',   'Payable')
                ) AS d(code, name, account_type)
                WHERE NOT EXISTS (
                    SELECT 1 FROM nexo.financial_accounts a
                    WHERE a.tenant_id = t.id AND a.account_type = d.account_type
                )
                AND NOT EXISTS (
                    SELECT 1 FROM nexo.financial_accounts a
                    WHERE a.tenant_id = t.id AND a.code = d.code
                );");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. Deleting accounts on rollback would orphan any transaction
            // already posted against them, and the backfilled rows are indistinguishable from
            // accounts the tenant created for itself.
        }
    }
}
