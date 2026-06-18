using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Common.Interfaces;
using Nexo.Infrastructure.MultiTenancy;
using Nexo.Infrastructure.Persistence;
using Nexo.Infrastructure.Persistence.Seed;
using Testcontainers.PostgreSql;

namespace Nexo.IntegrationTests.Helpers;

/// <summary>
/// Spins up a real PostgreSQL container via Testcontainers for each test class.
///
/// EF Core 8.0 registers two service types when AddDbContext is called with an options
/// factory (sp, opts) => { ... }:
///   1. DbContextOptions&lt;T&gt;  — factory that calls the options lambda  (TryAdd)
///   2. T (NexoDbContext)    — constructor-injected from (1)           (TryAdd)
///
/// ConfigureTestServices (which runs LAST, after all ConfigureServices) removes
/// both and re-adds them pointing at the Testcontainers PostgreSQL instance.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithDatabase("nexo_test")
        .WithUsername("nexo_test")
        .WithPassword("nexo_test")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Trigger the host build (Program.cs runs but skips migrations/seeding
        // in "Testing" environment), then migrate and seed the test container.
        using var scope = Services.CreateScope();
        var db     = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
        await db.Database.MigrateAsync();
        await seeder.SeedAsync();
    }

    public new async Task DisposeAsync()
    {
        await _postgres.StopAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // ConfigureTestServices runs AFTER all ConfigureServices callbacks.
        // Remove both EF Core TryAdd registrations added by AddInfrastructure,
        // then re-add them pointing at the Testcontainers PostgreSQL instance.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<NexoDbContext>>();
            services.RemoveAll<NexoDbContext>();

            services.AddDbContext<NexoDbContext>((sp, opts) =>
            {
                opts.UseNpgsql(_postgres.GetConnectionString(), npgsql =>
                {
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", "nexo");
                    npgsql.MigrationsAssembly(typeof(NexoDbContext).Assembly.GetName().Name);
                });

                var interceptor = sp.GetRequiredService<TenantSaveChangesInterceptor>();
                opts.AddInterceptors(interceptor);
            });

            // Replace the cache with a faithful in-memory implementation.
            // With no Redis connection string, AddInfrastructure registers NoOpCacheService,
            // which drops every write — so the refresh-token validity entry stored on login
            // was never found on refresh and the first refresh always returned 401. The
            // in-memory cache (backed by the singleton IMemoryCache) makes the refresh
            // rotation / replay-protection / revocation logic genuinely testable, matching
            // production's Redis-backed behaviour. See InMemoryCacheService for details.
            services.RemoveAll<ICacheService>();
            services.AddScoped<ICacheService, InMemoryCacheService>();
        });

        // JWT overrides so test tokens validate against the test server.
        builder.UseSetting("Jwt:Secret",   "test-secret-key-minimum-32-characters-long!");
        builder.UseSetting("Jwt:Issuer",   "nexo-api-test");
        builder.UseSetting("Jwt:Audience", "nexo-frontend-test");

        // Seed credential overrides — used ONLY in the Testing environment.
        // DataSeeder reads these keys with fallback to production values for non-test envs.
        // Values are intentionally verbose/fake so no scanner mistakes them for real secrets.
        builder.UseSetting("Seed:AdminPassword",    "IntegrationTestOnly!123");
        builder.UseSetting("Seed:PlatformEmail",    "platform-test@nexo.test");
        builder.UseSetting("Seed:PlatformPassword", "FakePlatformPass!999");

        // Disable the auth-login rate limiter for the GENERAL integration suite.
        // The whole suite shares ONE factory (collection fixture), and many tests
        // log in — with the limiter on, the shared window is exhausted after 5
        // logins and every later test gets 429 → empty body → JsonException
        // cascade. RateLimitingTests re-enable it explicitly via
        // WithRateLimitingEnabled() so the limiter itself stays under test.
        builder.UseSetting("RateLimiting:AuthLogin:Enabled", "false");

        // Same reasoning for the public booking limiter: the shared suite fires many public
        // POSTs from one client IP; with the limiter on, the window is exhausted and later
        // tests get 429. The policy stays enabled in production (config default true).
        builder.UseSetting("RateLimiting:PublicBooking:Enabled", "false");
    }

    /// <summary>Creates an HttpClient pre-configured for the test server.</summary>
    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });

    /// <summary>
    /// Returns a derived factory with the auth-login rate limiter ENABLED.
    /// Each call builds a fresh host (fresh in-memory limiter state), so rate-limit
    /// tests start from a clean window and don't contaminate one another.
    /// Used only by RateLimitingTests; the base factory keeps it disabled.
    /// </summary>
    public WebApplicationFactory<Program> WithRateLimitingEnabled(
        int permitLimit = 5, int windowSeconds = 900)
        => WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:AuthLogin:Enabled", "true");
            b.UseSetting("RateLimiting:AuthLogin:PermitLimit", permitLimit.ToString());
            b.UseSetting("RateLimiting:AuthLogin:WindowSeconds", windowSeconds.ToString());
        });
}
