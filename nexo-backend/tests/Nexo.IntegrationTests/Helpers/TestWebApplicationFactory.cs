using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Billing;
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
    // A imagem que o construtor sem parâmetros usava (obsoleto no Testcontainers 4.x): fixada para o banco de teste não mudar.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:15.1")
        .WithDatabase("nexo_test")
        .WithUsername("nexo_test")
        .WithPassword("nexo_test")
        .Build();

    /// <summary>The fake Anthropic API every Claude call of this host goes to (nothing leaves the test host).</summary>
    public FakeAnthropicHandler Anthropic { get; } = new();

    /// <summary>The fake Stripe every subscription read of this host goes to (nothing leaves the test host).</summary>
    public FakeStripeSubscriptionFetcher StripeSubscriptions { get; } = new();

    /// <summary>The host's latest warnings and errors, so an unexpected status in a test shows the server exception.</summary>
    public ServerLogCapture ServerLog { get; } = new();

    /// <summary>Holds one SQL command in flight when a test asks: how a race becomes deterministic. Idle otherwise.</summary>
    public DbCommandGate Gate { get; } = new();

    /// <summary>A test-only master key (base64 of 32 fixed bytes) so AI provider keys are really encrypted in the tests.</summary>
    public static readonly string TestMasterKey = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("test-only-master-key-32-bytes!!!"));

    public virtual async Task InitializeAsync()
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

    public new virtual async Task DisposeAsync()
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
                opts.AddInterceptors(interceptor, sp.GetRequiredService<Nexo.Infrastructure.Hubs.HubSessionChangeInterceptor>(), sp.GetRequiredService<SqlCommandCounter>(), Gate);
                // a page or batch without an order is nondeterministic: fail the test instead of logging a warning
                opts.ConfigureWarnings(w => w.Throw(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.RowLimitingOperationWithoutOrderByWarning));
            });
            // Counts SQL commands of requests marked with SqlCommandCounter.Header (N+1 tests).
            services.AddHttpContextAccessor();
            services.AddSingleton<SqlCommandCounter>();
            // Serilog sink (Program reads sinks from the container): unexpected statuses can show the server exception.
            services.AddSingleton<Serilog.Core.ILogEventSink>(ServerLog);

            // Replace the cache with a faithful in-memory implementation.
            // With no Redis connection string, AddInfrastructure registers NoOpCacheService,
            // which drops every write — so the refresh-token validity entry stored on login
            // was never found on refresh and the first refresh always returned 401. The
            // in-memory cache (backed by the singleton IMemoryCache) makes the refresh
            // rotation / replay-protection / revocation logic genuinely testable, matching
            // production's Redis-backed behaviour. See InMemoryCacheService for details.
            services.RemoveAll<ICacheService>();
            services.AddScoped<ICacheService, InMemoryCacheService>();

            // E-mail "delivers" (to nowhere) so features gated on a configured provider — invites,
            // the customer e-mails job — behave as in production. Nothing leaves the test host.
            services.RemoveAll<Nexo.Application.Common.Interfaces.IEmailService>();
            services.AddScoped<Nexo.Application.Common.Interfaces.IEmailService, DeliveringTestEmailService>();

            // Claude calls go to an in-process fake: the suite never reaches Anthropic.
            services.AddHttpClient(Nexo.Infrastructure.Modules.Interpreter.AnthropicClientFactory.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Anthropic);

            // Subscription reads go to an in-process fake: the suite never reaches Stripe.
            services.RemoveAll<IStripeSubscriptionFetcher>();
            services.AddSingleton<IStripeSubscriptionFetcher>(StripeSubscriptions);
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
        builder.UseSetting("RateLimiting:AuthRefresh:Enabled", "false");

        // Same reasoning for the public booking limiter: the shared suite fires many public
        // POSTs from one client IP; with the limiter on, the window is exhausted and later
        // tests get 429. The policy stays enabled in production (config default true).
        builder.UseSetting("RateLimiting:PublicBooking:Enabled", "false");
        builder.UseSetting("RateLimiting:PublicOrders:Enabled", "false");
        builder.UseSetting("RateLimiting:BuildPortal:Enabled", "false");
        builder.UseSetting("RateLimiting:BuildPortalAuth:Enabled", "false");
        builder.UseSetting("RateLimiting:BuildPortalDecision:Enabled", "false");
        builder.UseSetting("RateLimiting:CredentialCheck:Enabled", "false");
        // Same for uploads, in-flight bounds included: some suites race uploads on purpose. The Storage/Upload* tests turn them on.
        builder.UseSetting("RateLimiting:Uploads:Enabled", "false");
        builder.UseSetting("Uploads:InFlight:Enabled", "false");

        // Tax Intelligence is off unless configured (kill switch). The suite turns it on explicitly and relaxes
        // only the approval enforcement — the Mode stays unconfigured (Disabled) — so tests can use rules created
        // on the fly. TaxIntelligenceTests turns enforcement, the mode and the pilot scope on in derived hosts to
        // prove what each configuration accepts and refuses.
        builder.UseSetting("Secrets:MasterKey", TestMasterKey);
        // Stripe stays off here. A host that turns it on starts with these fake secrets (StripeStartupPolicy).
        builder.UseSetting("Integrations:Stripe:SecretKey", "sk_test_integration_tests_only");
        builder.UseSetting("Integrations:Stripe:WebhookSecret", "whsec_integration_tests_only");
        // Admin acts normally require a sign-in with two-step verification; most suites sign in with the password only.
        // PlatformSecurityTests turns the requirement back on in its own host.
        builder.UseSetting("Platform:RequireMfaForAdmin", "false");
        builder.UseSetting("TaxIntelligence:Policy:Enabled", "true");
        builder.UseSetting("TaxIntelligence:Policy:RequireApprovedRulesForDecisions", "false");
        builder.UseSetting("TaxIntelligence:Policy:RequireApprovedRulesForCalculations", "false");
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
