using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Modules.Interpreter;
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
        await SeedDefaultStoreAsync(ct);
        await SeedTestUsersAsync(ct);
        await SeedPlatformUserAsync(ct);
        await SeedAiProvidersAsync(ct);
        await SeedStoredPromptVersionsAsync(ct);
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

    // ── Test users ────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds 3 test tenants covering the main multi-store/multi-module scenarios:
    ///
    ///   1. clara.boutique  / boutique@123  — 1 módulo ativo (varejo)
    ///   2. lucas.mix       / lucas@123     — 2 módulos diferentes (varejo + restaurante)
    ///   3. ana.norte       / ana@123       — 2 lojas com o mesmo módulo (varejo × 2 filiais)
    ///
    /// Idempotent — skipped if any user with these logins already exists.
    /// </summary>
    private async Task SeedTestUsersAsync(CancellationToken ct)
    {
        var existingLogins = new[] { "clara.boutique", "lucas.mix", "ana.norte" };
        var alreadySeeded = await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => existingLogins.Contains(u.Login), ct);

        if (alreadySeeded)
        {
            _logger.LogDebug("Seed: test users already exist, skipping.");
            return;
        }

        // ── Tenant 1: Boutique Clara — 1 módulo ativo (varejo) ───────────────
        var t1 = Tenant.Create("Boutique Clara", "11.111.111/0001-11",
            "contato@boutiqueclara.com.br", "Boutique Clara", "(11) 1111-1111", "varejo");
        _context.Tenants.Add(t1);
        await _context.SaveChangesAsync(ct);

        var sub1 = ModuleSubscription.CreateAdminGrant(t1.Id, "varejo");
        _context.ModuleSubscriptions.Add(sub1);
        await _context.SaveChangesAsync(ct);

        _context.Stores.Add(Domain.Entities.Store.Create(t1.Id, "Boutique Clara", "boutique-clara", sub1.Id));
        _context.Users.Add(User.Create(t1.Id, "Clara Mendes", "clara@boutiqueclara.com.br",
            "clara.boutique", _hasher.Hash("boutique@123"), UserRole.Gerente));
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Seed: Tenant 'Boutique Clara' created (1 módulo, 1 loja). Login: clara.boutique / boutique@123");

        // ── Tenant 2: Grupo Mix — 2 módulos diferentes (varejo + restaurante) ─
        var t2 = Tenant.Create("Grupo Mix", "22.222.222/0001-22",
            "contato@grupomix.com.br", "Grupo Mix", "(22) 2222-2222", "varejo");
        _context.Tenants.Add(t2);
        await _context.SaveChangesAsync(ct);

        var sub2v = ModuleSubscription.CreateAdminGrant(t2.Id, "varejo");
        var sub2r = ModuleSubscription.CreateAdminGrant(t2.Id, "restaurante");
        _context.ModuleSubscriptions.AddRange(sub2v, sub2r);
        await _context.SaveChangesAsync(ct);

        _context.Stores.Add(Domain.Entities.Store.Create(t2.Id, "Mix Loja", "mix-loja", sub2v.Id));
        _context.Stores.Add(Domain.Entities.Store.Create(t2.Id, "Mix Restaurante", "mix-restaurante", sub2r.Id));
        _context.Users.Add(User.Create(t2.Id, "Lucas Ferreira", "lucas@grupomix.com.br",
            "lucas.mix", _hasher.Hash("lucas@123"), UserRole.Diretoria));
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Seed: Tenant 'Grupo Mix' created (2 módulos diferentes, 2 lojas). Login: lucas.mix / lucas@123");

        // ── Tenant 3: Rede Norte — 2 lojas com o mesmo módulo (varejo × 2) ───
        var t3 = Tenant.Create("Rede Norte", "33.333.333/0001-33",
            "contato@redenorte.com.br", "Rede Norte", "(33) 3333-3333", "varejo");
        _context.Tenants.Add(t3);
        await _context.SaveChangesAsync(ct);

        var sub3 = ModuleSubscription.CreateAdminGrant(t3.Id, "varejo");
        _context.ModuleSubscriptions.Add(sub3);
        await _context.SaveChangesAsync(ct);

        _context.Stores.Add(Domain.Entities.Store.Create(t3.Id, "Filial Centro", "filial-centro", sub3.Id));
        _context.Stores.Add(Domain.Entities.Store.Create(t3.Id, "Filial Sul", "filial-sul", sub3.Id));
        _context.Users.Add(User.Create(t3.Id, "Ana Souza", "ana@redenorte.com.br",
            "ana.norte", _hasher.Hash("ana@123"), UserRole.Gerente));
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Seed: Tenant 'Rede Norte' created (1 módulo, 2 lojas/filiais). Login: ana.norte / ana@123");
    }

    // ── Platform user (superusuário NexoERP) ─────────────────────────────────

    private async Task SeedPlatformUserAsync(CancellationToken ct)
    {
        if (await _context.PlatformUsers.AnyAsync(u => u.Email == "elias@nexo.com", ct))
        {
            _logger.LogDebug("Seed: platform user already exists, skipping.");
            return;
        }

        var platformUser = PlatformUser.Create(
            email:        "elias@nexo.com",
            passwordHash: _hasher.Hash("elias@2026"),
            role:         "super_admin");

        _context.PlatformUsers.Add(platformUser);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Seed: platform superuser created. Email: elias@nexo.com / elias@2026");
    }

    // ── AI Providers ─────────────────────────────────────────────────────────

    private async Task SeedAiProvidersAsync(CancellationToken ct)
    {
        if (await _context.AiProviders.AnyAsync(ct))
        {
            _logger.LogDebug("Seed: AI providers already exist, skipping.");
            return;
        }

        var providers = new[]
        {
            AiProvider.Create(
                name: "RuleBased (Motor de Regras)", provider: "RuleBased",
                isEnabled: true, isDefault: true, priority: 1,
                modelId: null, costPerInputTokenMicros: 0, costPerOutputTokenMicros: 0),
            AiProvider.Create(
                name: "Claude 3 Haiku (Anthropic)", provider: "Claude",
                isEnabled: false, isDefault: false, priority: 2,
                modelId: "claude-haiku-20240307", costPerInputTokenMicros: 250, costPerOutputTokenMicros: 1250),
            AiProvider.Create(
                name: "GPT-4o Mini (OpenAI)", provider: "OpenAI",
                isEnabled: false, isDefault: false, priority: 3,
                modelId: "gpt-4o-mini", costPerInputTokenMicros: 150, costPerOutputTokenMicros: 600),
        };

        _context.AiProviders.AddRange(providers);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Seed: {Count} AI providers created.", providers.Length);
    }

    // ── Stored prompt versions ────────────────────────────────────────────────

    private async Task SeedStoredPromptVersionsAsync(CancellationToken ct)
    {
        if (await _context.StoredPromptVersions.AnyAsync(ct))
        {
            _logger.LogDebug("Seed: stored prompt versions already exist, skipping.");
            return;
        }

        var prompts = new[]
        {
            StoredPromptVersion.Create(
                promptType:  "extraction",
                version:     "1.0.0",
                hash:        "seed0001",
                content:     "Extraia do texto: valor numérico, data no formato ISO, nome do pagador/recebedor e conta bancária. Responda em JSON com campos: amount, date, payee, account.",
                description: "Prompt base de extração de dados financeiros v1",
                createdBy:   "system",
                isActive:    true),
            StoredPromptVersion.Create(
                promptType:  "interpretation",
                version:     "1.0.0",
                hash:        "seed0002",
                content:     "Com base nos dados extraídos e no contexto do tenant, sugira: direção (entrada/saída), natureza, categoria e conta contábil. Responda em JSON com campos: direction, nature, categoryId, accountId.",
                description: "Prompt base de interpretação de movimentos v1",
                createdBy:   "system",
                isActive:    true),
            StoredPromptVersion.Create(
                promptType:  "memory",
                version:     "1.0.0",
                hash:        "seed0003",
                content:     "Resumo compacto do perfil de uso do tenant para contextualizar sugestões de categorias e contas frequentes.",
                description: "Prompt base de memória contextual v1",
                createdBy:   "system",
                isActive:    true),
        };
        // isActive already set to true via Create() param above — no further Activate() call needed.

        _context.StoredPromptVersions.AddRange(prompts);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Seed: {Count} stored prompt versions created (all active).", prompts.Length);
    }

    // ── Default store ─────────────────────────────────────────────────────────

    private async Task SeedDefaultStoreAsync(CancellationToken ct)
    {
        // IgnoreQueryFilters: seeder has no tenant context
        if (await _context.Stores.IgnoreQueryFilters().AnyAsync(ct))
        {
            _logger.LogDebug("Seed: stores already exist, skipping.");
            return;
        }

        var tenant = await _context.Tenants.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Seed: no tenant found for default store.");

        var subscription = await _context.ModuleSubscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenant.Id && s.ModuleKey == "varejo", ct);

        var store = Domain.Entities.Store.Create(
            tenantId:             tenant.Id,
            name:                 "Loja Principal",
            slug:                 "loja-principal",
            moduleSubscriptionId: subscription?.Id,
            settingsJson:         null);

        _context.Stores.Add(store);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Seed: default store created (Id: {StoreId}) for tenant {TenantId}.",
            store.Id, tenant.Id);
    }
}
