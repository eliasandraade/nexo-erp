using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;

namespace Nexo.Infrastructure.Persistence.Provisioning;

/// <summary>
/// Gives a tenant the four accounts every money-touching flow assumes exist: Caixa, Banco,
/// Contas a Receber, Contas a Pagar.
///
/// Why this is not the seeder's job: SeedDefaultFinancialAccountsAsync creates them for the FIRST
/// tenant only and aborts as soon as any account row exists, and Program.cs does not run the demo
/// seeder in Production at all. So every tenant created after the first one — self-service
/// registration or platform admin — had no accounts, and the first credit sale or Service payment
/// would fail with "No active 'Contas a Receber' account found for this tenant."
///
/// Idempotent per account type: only missing types are created, so it is safe to call on every
/// tenant creation and safe to backfill an existing tenant.
/// </summary>
public class DefaultFinancialAccountProvisioner
{
    private readonly NexoDbContext _db;
    private readonly ILogger<DefaultFinancialAccountProvisioner> _logger;

    public DefaultFinancialAccountProvisioner(
        NexoDbContext db, ILogger<DefaultFinancialAccountProvisioner> logger)
    {
        _db     = db;
        _logger = logger;
    }

    private static readonly (string Code, string Name, FinancialAccountType Type)[] Defaults =
    {
        ("1.1", "Caixa",            FinancialAccountType.Cash),
        ("1.2", "Banco",            FinancialAccountType.Bank),
        ("2.1", "Contas a Receber", FinancialAccountType.Receivable),
        ("3.1", "Contas a Pagar",   FinancialAccountType.Payable),
    };

    /// <summary>
    /// Adds the missing default accounts for <paramref name="tenantId"/> to the change tracker.
    /// Does NOT call SaveChanges — the caller owns the unit of work, so this composes with the
    /// tenant/store/user batch that creates the tenant in the first place.
    /// </summary>
    public async Task EnsureAsync(Guid tenantId, CancellationToken ct = default)
    {
        // IgnoreQueryFilters: provisioning runs without (or before) a resolved tenant context.
        var existing = await _db.FinancialAccounts
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId)
            .Select(a => a.AccountType)
            .ToListAsync(ct);

        var missing = Defaults.Where(d => !existing.Contains(d.Type)).ToArray();
        if (missing.Length == 0) return;

        foreach (var d in missing)
            _db.FinancialAccounts.Add(FinancialAccount.Create(tenantId, d.Code, d.Name, d.Type));

        _logger.LogInformation(
            "Provisioned {Count} default financial account(s) for tenant {TenantId}.",
            missing.Length, tenantId);
    }
}
