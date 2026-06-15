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
using Nexo.Infrastructure.Repositories;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Application.Modules.Build.Interfaces;
using Nexo.Infrastructure.Hubs;
using Nexo.Infrastructure.Modules.Build;
using Nexo.Infrastructure.Modules.Interpreter;
using Nexo.Infrastructure.Repositories.Modules.Build;
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
            if (!IsProduction(configuration))
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
                    // Fail-open fast: if Redis is unreachable, don't block each request for 5s.
                    // 300ms means at most ~1.8s overhead (6 ops × 300ms) instead of 30s+.
                    options.AbortOnConnectFail = false;
                    options.ConnectTimeout     = 2000;
                    options.AsyncTimeout       = 300;
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

        // Analyzers (registered individually for IEnumerable<IDocumentAnalyzer> injection)
        services.AddScoped<IDocumentAnalyzer, RuleBasedAnalyzer>();
        services.AddScoped<IDocumentAnalyzer, ClaudeAnalyzerStub>();

        // Feature flags — controls which analyzers/features are active
        services.AddSingleton<IInterpreterFeatureFlags>(
            new InterpreterFeatureFlags(configuration));

        // Application services
        services.AddScoped<IAnalyzerSelector, AnalyzerSelectorService>();
        services.AddScoped<IInterpretationService, RuleBasedInterpretationService>();

        // Telemetry writer — singleton so it can hold IServiceProvider safely
        services.AddSingleton<ITelemetryWriter, TelemetryWriterService>();

        // Attachment storage — local filesystem for MVP
        var attachmentsDir = configuration["Interpreter:AttachmentsDir"]
                             ?? Path.Combine(AppContext.BaseDirectory, "wwwroot", "attachments");
        services.AddSingleton<IAttachmentStorage>(new LocalAttachmentStorage(attachmentsDir));

        // Health checks
        services.AddHealthChecks()
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
            services.AddHttpClient<ResendEmailService>();
            services.AddScoped<IEmailService, ResendEmailService>();
        }
        else
        {
            services.AddScoped<IEmailService, ConsoleEmailService>();
        }
        services.AddScoped<RegistrationService>();

        // ── Audit ─────────────────────────────────────────────────────────────
        services.AddScoped<IAuditWriter, AuditWriterService>();
        services.AddScoped<AuditQueryService>();

        // ── Reports ───────────────────────────────────────────────────────────
        services.AddScoped<Nexo.Infrastructure.Reports.ReportsService>();

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

    private static bool IsProduction(IConfiguration config)
        => config["ASPNETCORE_ENVIRONMENT"] == "Production";
}
