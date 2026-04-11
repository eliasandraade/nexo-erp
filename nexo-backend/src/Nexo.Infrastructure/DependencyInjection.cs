using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Varejo.Interfaces;
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
using Nexo.Infrastructure.Repositories.Modules.Restaurante;
using Nexo.Infrastructure.Repositories.Modules.Varejo;
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
                    npgsql.EnableRetryOnFailure(3);
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
                    return ConnectionMultiplexer.Connect(redisConnectionString);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Redis connection failed. Cache will be degraded (fail-open).");
                    // Return a fake/noop multiplexer fallback — requests continue without cache.
                    // In production, this should alert (Redis is required for JWT blacklist).
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
        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<ICashRepository, CashRepository>();
        services.AddScoped<IFinancialRepository, FinancialRepository>();

        // ── Módulo Restaurante ────────────────────────────────────────────────
        services.AddScoped<IAreaRepository, AreaRepository>();
        services.AddScoped<ITableRepository, TableRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IRecipeCardRepository, RecipeCardRepository>();

        // ── Módulo Varejo ─────────────────────────────────────────────────────
        services.AddScoped<IPurchaseRepository, PurchaseRepository>();
        services.AddScoped<IPriceListRepository, PriceListRepository>();

        // ── Module Access (cache em memória + DB) ─────────────────────────────
        services.AddMemoryCache();
        services.AddScoped<IModuleAccessService, ModuleAccessService>();

        // ── Auth ─────────────────────────────────────────────────────────────
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUserService>();
        services.AddScoped<IEmailService, ConsoleEmailService>();
        services.AddScoped<RegistrationService>();

        // ── Audit ─────────────────────────────────────────────────────────────
        services.AddScoped<IAuditWriter, AuditWriterService>();

        // ── Seed ─────────────────────────────────────────────────────────────
        services.AddScoped<DataSeeder>();

        return services;
    }

    private static bool IsProduction(IConfiguration config)
        => config["ASPNETCORE_ENVIRONMENT"] == "Production";
}
