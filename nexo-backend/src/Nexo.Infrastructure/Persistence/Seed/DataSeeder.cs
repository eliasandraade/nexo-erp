using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotent seeder — safe to call on every startup in non-production environments.
/// Creates: default tenant, admin user, default settings, initial module definitions.
///
/// NOTE: Seeder bypasses Global Query Filters intentionally (no tenant context at startup).
/// </summary>
public class DataSeeder
{
    private readonly NexoDbContext _context;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<DataSeeder> _logger;

    public DataSeeder(NexoDbContext context, IPasswordHasher hasher, ILogger<DataSeeder> logger)
    {
        _context = context;
        _hasher = hasher;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedTenantAsync(ct);
        await SeedAdminUserAsync(ct);
        await SeedDefaultSettingsAsync(ct);
        await SeedModuleDefinitionsAsync(ct);
        await SeedDefaultFinancialAccountsAsync(ct);
        await SeedDefaultModuleSubscriptionsAsync(ct);
    }

    // ── Tenant ────────────────────────────────────────────────────────────────

    private async Task SeedTenantAsync(CancellationToken ct)
    {
        if (await _context.Tenants.AnyAsync(ct))
        {
            _logger.LogDebug("Seed: tenants already exist, skipping.");
            return;
        }

        var tenant = Tenant.Create(
            companyName:  "Andrade Systems",
            taxId:        "00.000.000/0001-00",
            email:        "contato@andradesystems.com.br",
            tradeName:    "Andrade Systems",
            phone:        "(00) 0000-0000",
            businessType: "varejo");

        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Seed: default tenant created (Id: {TenantId}).", tenant.Id);
    }

    // ── Admin user ────────────────────────────────────────────────────────────

    private async Task SeedAdminUserAsync(CancellationToken ct)
    {
        // IgnoreQueryFilters: seeder has no tenant context
        if (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Login == "admin", ct))
        {
            _logger.LogDebug("Seed: admin user already exists, skipping.");
            return;
        }

        var tenant = await _context.Tenants.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Seed: no tenant found for admin user.");

        var admin = User.Create(
            tenantId:               tenant.Id,
            fullName:               "Administrador do Sistema",
            email:                  "admin@nexo.local",
            login:                  "admin",
            passwordHash:           _hasher.Hash("nexo@2026"),
            role:                   UserRole.Diretoria,
            notes:                  "Usuário administrador criado automaticamente pelo sistema.");

        _context.Users.Add(admin);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Seed: admin user created (Id: {UserId}). Login: admin / Password: nexo@2026",
            admin.Id);
    }

    // ── Default settings ──────────────────────────────────────────────────────

    private async Task SeedDefaultSettingsAsync(CancellationToken ct)
    {
        // IgnoreQueryFilters: seeder has no tenant context
        if (await _context.AppSettings.IgnoreQueryFilters().AnyAsync(ct))
        {
            _logger.LogDebug("Seed: app settings already exist, skipping.");
            return;
        }

        var tenant = await _context.Tenants.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Seed: no tenant found for settings.");

        var settings = AppSettings.CreateForTenant(
            tenantId:      tenant.Id,
            companyJson:   """{"name":"Andrade Systems","tradeName":"Andrade Systems","cnpj":"","email":"","phone":""}""",
            operationJson: """{"defaultOperator":""}""",
            inventoryJson: """{"noMovementAlertDays":30,"minStockBehavior":"alert","enableLowStockAlerts":true,"enableZeroStockAlerts":true,"enableHighRotationAlerts":false}""",
            commissionJson:"""{"defaultCommissionRate":3,"enableProductCommission":false,"policyNotes":""}""",
            posJson:       """{"allowValueDiscount":true,"allowPercentDiscount":true,"requireManagerAuth":true,"maxDiscountPercent":20}""",
            systemJson:    """{"language":"pt-BR","dateFormat":"dd/MM/yyyy","currencySymbol":"R$"}""");

        _context.AppSettings.Add(settings);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Seed: default settings created for tenant {TenantId}.", tenant.Id);
    }

    // ── Module definitions ────────────────────────────────────────────────────

    private async Task SeedModuleDefinitionsAsync(CancellationToken ct)
    {
        if (await _context.ModuleDefinitions.AnyAsync(ct))
        {
            _logger.LogDebug("Seed: module definitions already exist, skipping.");
            return;
        }

        var modules = new[]
        {
            ModuleDefinition.Create("varejo",               "Comércio em Geral (Varejo)",        priceMonthly: 79m,  priceAnnual: 710m,  priceLifetime: 1290m),
            ModuleDefinition.Create("restaurante",          "Restaurantes e Bares",               priceMonthly: 97m,  priceAnnual: 870m,  priceLifetime: 1490m),
            ModuleDefinition.Create("academia-musculacao",  "Academias de Musculação",            priceMonthly: 79m,  priceAnnual: 710m,  priceLifetime: 1290m),
            ModuleDefinition.Create("academia-artes-marciais", "Academias de Artes Marciais",    priceMonthly: 79m,  priceAnnual: 710m,  priceLifetime: 1290m),
            ModuleDefinition.Create("clinica-medica",       "Clínicas Médicas e Odontológicas",   priceMonthly: 97m,  priceAnnual: 870m,  priceLifetime: 1490m),
            ModuleDefinition.Create("salao-beleza",         "Salões de Beleza",                   priceMonthly: 69m,  priceAnnual: 620m,  priceLifetime: 1090m),
            ModuleDefinition.Create("pet-shop",             "Pet Shops + Clínicas Veterinárias",  priceMonthly: 79m,  priceAnnual: 710m,  priceLifetime: 1290m),
            ModuleDefinition.Create("oficina-mecanica",     "Oficinas Mecânicas",                 priceMonthly: 79m,  priceAnnual: 710m,  priceLifetime: 1290m),
            ModuleDefinition.Create("pousada-hotel",        "Pousadas e Hotéis",                  priceMonthly: 97m,  priceAnnual: 870m,  priceLifetime: 1490m),
            ModuleDefinition.Create("imobiliaria",          "Imobiliárias",                       priceMonthly: 97m,  priceAnnual: 870m,  priceLifetime: 1490m),
        };

        _context.ModuleDefinitions.AddRange(modules);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Seed: {Count} module definitions created.", modules.Length);
    }

    // ── Default financial accounts ────────────────────────────────────────────

    private async Task SeedDefaultFinancialAccountsAsync(CancellationToken ct)
    {
        // IgnoreQueryFilters: seeder has no tenant context
        if (await _context.FinancialAccounts.IgnoreQueryFilters().AnyAsync(ct))
        {
            _logger.LogDebug("Seed: financial accounts already exist, skipping.");
            return;
        }

        var tenant = await _context.Tenants.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Seed: no tenant found for financial accounts.");

        var accounts = new[]
        {
            FinancialAccount.Create(tenant.Id, "1.1", "Caixa",             FinancialAccountType.Cash),
            FinancialAccount.Create(tenant.Id, "1.2", "Banco",             FinancialAccountType.Bank),
            FinancialAccount.Create(tenant.Id, "2.1", "Contas a Receber",  FinancialAccountType.Receivable),
            FinancialAccount.Create(tenant.Id, "3.1", "Contas a Pagar",    FinancialAccountType.Payable),
        };

        _context.FinancialAccounts.AddRange(accounts);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Seed: {Count} default financial accounts created for tenant {TenantId}.",
            accounts.Length, tenant.Id);
    }

    // ── Default module subscriptions ──────────────────────────────────────────

    private async Task SeedDefaultModuleSubscriptionsAsync(CancellationToken ct)
    {
        // IgnoreQueryFilters: seeder has no tenant context
        if (await _context.ModuleSubscriptions.IgnoreQueryFilters().AnyAsync(ct))
        {
            _logger.LogDebug("Seed: module subscriptions already exist, skipping.");
            return;
        }

        var tenant = await _context.Tenants.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Seed: no tenant found for module subscriptions.");

        // Grant the "varejo" module to the default tenant as an admin grant
        // grantedById is null for system-seeded grants (no platform user exists at seed time)
        var subscription = ModuleSubscription.CreateAdminGrant(
            tenantId:  tenant.Id,
            moduleKey: "varejo");

        _context.ModuleSubscriptions.Add(subscription);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Seed: 'varejo' module subscription granted to tenant {TenantId}.",
            tenant.Id);
    }
}
