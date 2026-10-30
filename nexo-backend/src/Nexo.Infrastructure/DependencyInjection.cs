using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Application.Modules.Varejo.Interfaces;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Audit;
using Nexo.Infrastructure.Auth;
using Nexo.Infrastructure.Email;
using Nexo.Infrastructure.Cache;
using Nexo.Infrastructure.Modules;
using Nexo.Infrastructure.MultiTenancy;
using Nexo.Infrastructure.Persistence;
using Nexo.Infrastructure.Persistence.Seed;
using Nexo.Infrastructure.Persistence.Provisioning;
using Nexo.Infrastructure.Repositories;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Application.Modules.Build.Interfaces;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Infrastructure.Hubs;
using Nexo.Infrastructure.Modules.Build;
using Nexo.Infrastructure.Modules.Interpreter;
using Nexo.Infrastructure.Repositories.Modules.Build;
using Nexo.Infrastructure.Repositories.Modules.Service;
using Nexo.Infrastructure.Repositories.Modules.Interpreter;
using Nexo.Infrastructure.Repositories.Modules.Restaurante;
using Nexo.Infrastructure.Repositories.Modules.Varejo;
using Nexo.Infrastructure.Integrations;
using StackExchange.Redis;

namespace Nexo.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Multi-tenancy ─────────────────────────────────────────────────────
        // ICurrentTenant is scoped: one instance per HTTP request.
        services.AddScoped<ICurrentTenant, CurrentTenantService>();

        // ICurrentStore is scoped: reads storeId JWT claim per request.
        services.AddScoped<ICurrentStore, CurrentStoreService>();

        // TenantSaveChangesInterceptor must be singleton (EF interceptors are registered once).
        services.AddSingleton<TenantSaveChangesInterceptor>();

        // ── EF Core / PostgreSQL ─────────────────────────────────────────────
        services.AddDbContext<NexoDbContext>((sp, options) =>
        {
            options.UseNpgsql(
                configuration.GetConnectionString("Default"),
                npgsql =>
                {
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", "nexo");
                    npgsql.MigrationsAssembly(typeof(NexoDbContext).Assembly.GetName().Name);
                });

            // Register the tenant isolation interceptor
            var interceptor = sp.GetRequiredService<TenantSaveChangesInterceptor>();
            options.AddInterceptors(interceptor);

            // Extra diagnostics in non-production
            // Parameter values (PII) in logs only on a developer machine or the test host — never
            // by default. Before, an UNSET ASPNETCORE_ENVIRONMENT (which the host treats as
            // Production) turned sensitive logging ON.
            if (IsDevelopmentOrTesting(configuration))
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        // ── Redis ─────────────────────────────────────────────────────────────
        var redisConnectionString = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<RedisCacheService>>();
                try
                {
                    var options = ConfigurationOptions.Parse(redisConnectionString);
                    // Fail-open: if Redis is unreachable, WaitAsync in RedisCacheService bails at 2s.
                    options.AbortOnConnectFail = false;
                    options.ConnectTimeout     = 3000;
                    options.AsyncTimeout       = 2500;
                    return ConnectionMultiplexer.Connect(options);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Redis connection failed. Cache will be degraded (fail-open).");
                    throw;
                }
            });
            services.AddScoped<ICacheService, RedisCacheService>();
        }
        else
        {
            // No Redis configured — register a no-op cache for local dev without Redis.
            services.AddScoped<ICacheService, NoOpCacheService>();
        }

        // ── Unit of Work ─────────────────────────────────────────────────────
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // ── Repositories ─────────────────────────────────────────────────────
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IAppSettingsRepository, AppSettingsRepository>();

        // Store repository (platform-level, bypasses query filters)
        services.AddScoped<IStoreRepository, StoreEntityRepository>();

        // CORE business repositories
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductPurchasePriceRepository, ProductPurchasePriceRepository>();
        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<IStockOperationsRepository, StockOperationsRepository>();
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<ICashRepository, CashRepository>();
        services.AddScoped<IFinancialRepository, FinancialRepository>();

        // ── Módulo Restaurante ────────────────────────────────────────────────
        services.AddScoped<IAreaRepository, AreaRepository>();
        services.AddScoped<ITableRepository, TableRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IRecipeCardRepository, RecipeCardRepository>();
        services.AddScoped<IModifierGroupRepository, ModifierGroupRepository>();
        services.AddScoped<IFoodServiceSettingsRepository, FoodServiceSettingsRepository>();
        services.AddScoped<IDeliveryOrderRepository, DeliveryOrderRepository>();
        services.AddScoped<IDeliveryZoneRepository, DeliveryZoneRepository>();
        services.AddScoped<ICouponRepository, CouponRepository>();
        // Single instance per scope — shared between controller injection and IDeliveryOrderSyncService.
        services.AddScoped<Nexo.Application.Modules.Restaurante.DeliveryOrderService>();
        services.AddScoped<IDeliveryOrderSyncService>(sp =>
            sp.GetRequiredService<Nexo.Application.Modules.Restaurante.DeliveryOrderService>());
        services.AddScoped<Nexo.Application.Modules.Restaurante.PublicMenuService>();
        services.AddScoped<Nexo.Application.Modules.Restaurante.DeliveryZoneService>();
        services.AddScoped<Nexo.Application.Modules.Restaurante.CouponService>();

        // ── Módulo Varejo ─────────────────────────────────────────────────────
        services.AddScoped<IPurchaseRepository, PurchaseRepository>();
        services.AddScoped<IPriceListRepository, PriceListRepository>();

        // ── Módulo Build (Orken Build — Gestão de Obras) ─────────────────────
        services.AddScoped<IBuildProjectRepository, BuildProjectRepository>();
        services.AddScoped<IBuildStageRepository, BuildStageRepository>();
        services.AddScoped<IBuildBudgetRepository, BuildBudgetRepository>();
        services.AddScoped<IBuildBudgetItemRepository, BuildBudgetItemRepository>();
        services.AddScoped<IBuildDailyLogRepository, BuildDailyLogRepository>();
        services.AddScoped<IBuildDailyLogPhotoRepository, BuildDailyLogPhotoRepository>();
        services.AddScoped<IBuildFinancialQueryService, BuildFinancialQueryService>();
        services.AddScoped<IBuildDashboardQueryService, BuildDashboardQueryService>();
        // Wave 1: weighted progress + snapshots, cost per stage, client portal (internal + public)
        services.AddScoped<BuildAuditTrail>();
        services.AddScoped<BuildPortalMedia>();
        services.AddScoped<BuildProgressEngine>();
        services.AddScoped<BuildProgressService>();
        services.AddScoped<Nexo.Application.Modules.Build.IBuildProgressRecorder>(sp => sp.GetRequiredService<BuildProgressService>());
        services.AddScoped<BuildCostService>();
        services.AddScoped<BuildPortalAdminService>();
        services.AddScoped<BuildPublicPortalService>();
        // Wave 2 — commercial (templates, versions, PDF, change orders, selections, materials, measurements)
        services.AddScoped<BuildBudgetCommercialService>();
        services.AddHttpClient(BuildBudgetCommercialService.LogoHttpClient, c => c.Timeout = TimeSpan.FromSeconds(5));
        services.AddScoped<BuildChangeOrderEffects>();
        services.AddScoped<BuildChangeOrderService>();
        services.AddScoped<BuildSelectionService>();
        services.AddScoped<BuildMaterialService>();
        services.AddScoped<BuildMeasurementService>();
        services.AddScoped<BuildQualityService>();
        services.AddScoped<BuildPostConstructionService>();
        // Wave 4 — digital twin
        services.AddSingleton<BuildTwinOptions>();
        services.AddScoped<BuildTenantSettingsService>();
        services.AddScoped<BuildTwinStateReader>();
        services.AddScoped<BuildModelService>();
        services.AddScoped<BuildTwinService>();
        services.AddScoped<BuildRealityCaptureService>();
        services.AddScoped<IBuildModelProcessor, BuildModelProcessor>();
        // Wave 5 — AI (reuses the platform AI runtime), deterministic risk, project documents
        services.AddScoped<BuildRiskService>();
        services.AddScoped<BuildDocumentService>();
        services.AddScoped<BuildAiService>();

        // ── Service module (Orken Service) repositories ──────────────────────
        services.AddScoped<ISvcProfessionalRepository, SvcProfessionalRepository>();
        services.AddScoped<ISvcCatalogItemRepository, SvcCatalogItemRepository>();
        services.AddScoped<ISvcSubjectRepository, SvcSubjectRepository>();
        services.AddScoped<ISvcRecordEntryRepository, SvcRecordEntryRepository>();
        services.AddScoped<ISvcAppointmentRepository, SvcAppointmentRepository>();
        services.AddScoped<ISvcTimeBlockRepository, SvcTimeBlockRepository>();
        services.AddScoped<ISvcFinanceQueries, SvcFinanceQueries>();
        services.AddScoped<ISvcOrderRepository, SvcOrderRepository>();
        services.AddScoped<ISvcOrderItemRepository, SvcOrderItemRepository>();
        services.AddScoped<ISvcPackageRepository, SvcPackageRepository>();
        services.AddScoped<ISvcPackageItemRepository, SvcPackageItemRepository>();
        services.AddScoped<ISvcCustomerPackageRepository, SvcCustomerPackageRepository>();
        services.AddScoped<ISvcCustomerPackageItemRepository, SvcCustomerPackageItemRepository>();
        services.AddScoped<ISvcPackageUsageRepository, SvcPackageUsageRepository>();
        services.AddScoped<ISvcPaymentRepository, SvcPaymentRepository>();
        services.AddScoped<ISvcCommissionRepository, SvcCommissionRepository>();
        services.AddScoped<ISvcSettingsRepository, SvcSettingsRepository>();

        // ── Operational Interpretation Engine ─────────────────────────────────
        // Repositories
        services.AddScoped<IFinancialMovementRepository, FinancialMovementRepository>();
        services.AddScoped<IExtractionResultRepository, ExtractionResultRepository>();
        services.AddScoped<IInterpretationSuggestionRepository, InterpretationSuggestionRepository>();
        services.AddScoped<IUserCorrectionRepository, UserCorrectionRepository>();
        services.AddScoped<IReprocessLogRepository, ReprocessLogRepository>();
        services.AddScoped<IMovementMemoryProfileRepository, MovementMemoryProfileRepository>();
        services.AddScoped<ITenantStopwordRepository, TenantStopwordRepository>();
        services.AddScoped<IMovementAttachmentRepository, MovementAttachmentRepository>();
        services.AddScoped<IMovementAuditLogRepository, MovementAuditLogRepository>();

        // Domain services
        services.AddScoped<IDescriptionNormalizer, DescriptionNormalizer>();
        services.AddScoped<IMovementMemoryService, MovementMemoryServiceImpl>();

        // AI runtime: the SuperAdmin's provider configuration (ai_providers) is the source of truth. Analyzers are
        // registered individually (IEnumerable<IDocumentAnalyzer>); a provider without one shows as "não disponível".
        services.AddSingleton<Nexo.Application.Common.Interfaces.ISecretProtector, Nexo.Infrastructure.Security.SecretProtector>();
        services.AddHttpClient(AnthropicClientFactory.HttpClientName);
        services.AddSingleton<AnthropicClientFactory>();
        services.AddScoped<IDocumentAnalyzer, RuleBasedAnalyzer>();
        services.AddScoped<IDocumentAnalyzer, ClaudeAnalyzer>();
        services.AddScoped<IActivePromptProvider, ActivePromptProvider>();
        services.AddScoped<IAiProviderRegistry, AiProviderRegistry>();
        services.AddScoped<IAnalyzerSelector, AnalyzerSelectorService>();
        services.AddScoped<IAnalysisRunner, AnalysisRunner>();
        services.AddScoped<IAiStructuredCompletion, AiStructuredCompletion>();
        services.AddSingleton<IAiUsageGuard, AiUsageGuard>();
        services.AddScoped<AiProviderAdminService>();
        services.AddScoped<AiGovernanceAdminService>();
        services.AddScoped<IMovementReferenceGuard, MovementReferenceGuard>();
        services.AddScoped<IInterpretationService, RuleBasedInterpretationService>();

        // Telemetry writer — singleton so it can hold IServiceProvider safely
        services.AddSingleton<ITelemetryWriter, TelemetryWriterService>();

        // Attachment storage — local filesystem for MVP
        var attachmentsDir = configuration["Interpreter:AttachmentsDir"]
                             ?? Path.Combine(AppContext.BaseDirectory, "wwwroot", "attachments");
        services.AddSingleton<IAttachmentStorage>(new LocalAttachmentStorage(attachmentsDir));

        // Health checks (PR 68). "ready" = may this instance take traffic: only PostgreSQL can say
        // no. Redis and storage degrade the report but never take the API out of rotation.
        services.AddSingleton<Nexo.Infrastructure.Health.ObjectStorageHealthCheck>();

        // Observability (PR 69): own metrics + error tracking in our database
        services.AddSingleton<Nexo.Infrastructure.Observability.OrkenMetrics>();
        services.AddSingleton<Nexo.Application.Common.Observability.IErrorTracker, Nexo.Infrastructure.Observability.ErrorTracker>();
        services.AddHealthChecks()
            .AddCheck<Nexo.Infrastructure.Health.DatabaseHealthCheck>(
                "postgres", HealthStatus.Unhealthy, tags: [Nexo.Infrastructure.Health.HealthTags.Ready])
            .AddCheck<Nexo.Infrastructure.Health.RedisHealthCheck>(
                "redis", HealthStatus.Degraded, tags: [Nexo.Infrastructure.Health.HealthTags.Ready])
            .AddCheck<Nexo.Infrastructure.Health.ObjectStorageHealthCheck>(
                "object-storage", HealthStatus.Degraded, tags: [Nexo.Infrastructure.Health.HealthTags.Ready])
            .AddCheck<InterpreterStorageHealthCheck>(
                "interpreter-storage",
                HealthStatus.Degraded,
                tags: ["interpreter", "storage"]);

        // ── Module Access (cache em memória + DB) ─────────────────────────────
        services.AddMemoryCache();
        services.AddScoped<IModuleAccessService, ModuleAccessService>();

        // ── Auth ─────────────────────────────────────────────────────────────
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<ISessionStore, SessionStoreService>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUserService>();
        // Use Resend when API key is configured; fall back to console logging.
        var resendApiKey = configuration["Resend:ApiKey"];
        if (!string.IsNullOrWhiteSpace(resendApiKey))
        {
            // short timeout: a slow provider must not hold the e-mails job (or a sign-up) for 100s
            services.AddHttpClient<ResendEmailService>(c => c.Timeout = TimeSpan.FromSeconds(10));
            services.AddScoped<IEmailService>(sp => sp.GetRequiredService<ResendEmailService>());
        }
        else
        {
            services.AddScoped<IEmailService, ConsoleEmailService>();
        }
        services.AddScoped<DefaultFinancialAccountProvisioner>();
        services.AddScoped<RegistrationService>();
        services.AddScoped<AccountSecurityService>();
        services.AddSingleton<IMfaSecretProtector, MfaSecretProtector>();
        services.AddScoped<Nexo.Infrastructure.Auth.PlatformAuthService>();
        services.AddScoped<IUserSessionRevoker>(sp => sp.GetRequiredService<AccountSecurityService>());
        services.AddScoped<IUserInviter>(sp => sp.GetRequiredService<AccountSecurityService>());

        // ── Audit ─────────────────────────────────────────────────────────────
        services.AddScoped<IAuditWriter, AuditWriterService>();
        services.AddScoped<AuditQueryService>();

        // ── Reports ───────────────────────────────────────────────────────────
        services.AddScoped<Nexo.Infrastructure.Reports.ReportsService>();
        services.AddScoped<Nexo.Infrastructure.Reports.SalesAnalytics>();
        services.AddScoped<Nexo.Infrastructure.Fiscal.FiscalService>();
        services.AddScoped<Nexo.Application.Integrations.Pdf.IReceiptHeaderProvider>(sp => sp.GetRequiredService<Nexo.Infrastructure.Fiscal.FiscalService>());

        // Tax Intelligence: versioned catalogue, deterministic engine, opportunities, credits, split payment.
        // Engine versions this build can run: new calculations use the current one; a stored calculation
        // is only ever reproduced by the exact version it records.
        services.AddSingleton(Nexo.Application.Modules.TaxIntelligence.Engine.TaxEngineRegistry.Default);
        // Fail closed unless configured: the module is off (Enabled=false) and no approval mode is set
        // (Mode=Disabled). The pilot needs explicit configuration plus a platform-kept scope. Development and
        // Testing relax only the approval enforcement — never Enabled or Mode — so the workflow can be
        // exercised with any catalogue.
        services.Configure<Nexo.Infrastructure.TaxIntelligence.TaxIntelligencePolicyOptions>(o =>
        {
            o.RequireApprovedRulesForDecisions = !IsDevelopmentOrTesting(configuration);
            o.RequireApprovedRulesForCalculations = !IsDevelopmentOrTesting(configuration);
            configuration.GetSection(Nexo.Infrastructure.TaxIntelligence.TaxIntelligencePolicyOptions.Section).Bind(o);
        });
        services.AddScoped<Nexo.Infrastructure.TaxIntelligence.TaxAuditTrail>();
        services.AddScoped<Nexo.Infrastructure.TaxIntelligence.TaxCatalogService>();
        services.AddScoped<Nexo.Infrastructure.TaxIntelligence.TaxApprovalGate>();
        services.AddScoped<Nexo.Infrastructure.TaxIntelligence.TaxComplianceService>();
        services.AddScoped<Nexo.Infrastructure.TaxIntelligence.TaxProfileService>();
        services.AddScoped<Nexo.Infrastructure.TaxIntelligence.SaleTaxFactsReader>();
        services.AddScoped<Nexo.Infrastructure.TaxIntelligence.TaxOpportunityService>();
        services.AddScoped<Nexo.Infrastructure.TaxIntelligence.TaxCalculationService>();
        services.AddScoped<Nexo.Infrastructure.TaxIntelligence.TaxSimulationService>();
        services.AddScoped<Nexo.Infrastructure.TaxIntelligence.TaxCreditService>();
        services.AddScoped<Nexo.Infrastructure.TaxIntelligence.TaxSplitPaymentService>();
        services.AddScoped<Nexo.Infrastructure.TaxIntelligence.TaxOverviewService>();
        services.AddSingleton<Nexo.Infrastructure.TaxIntelligence.ISplitPaymentProvider, Nexo.Infrastructure.TaxIntelligence.NotConfiguredSplitPaymentProvider>();
        services.AddSingleton<Nexo.Application.Features.Fiscal.IFiscalDocumentProvider, Nexo.Application.Features.Fiscal.NotConfiguredFiscalProvider>();

        // ── Dashboard ─────────────────────────────────────────────────────────
        services.AddScoped<Nexo.Infrastructure.Dashboard.DashboardService>();

        // ── Seed ─────────────────────────────────────────────────────────────
        services.AddScoped<DataSeeder>();

        // ── SignalR ───────────────────────────────────────────────────────────
        services.AddSignalR();
        services.AddScoped<IRestaurantNotificationService, RestaurantNotificationService>();

        // ── Integrations ──────────────────────────────────────────────────────
        services.AddIntegrations(configuration);

        return services;
    }

    private static bool IsDevelopmentOrTesting(IConfiguration config)
        => config["ASPNETCORE_ENVIRONMENT"] is "Development" or "Testing"
        || config["DOTNET_ENVIRONMENT"] is "Development" or "Testing";
}
