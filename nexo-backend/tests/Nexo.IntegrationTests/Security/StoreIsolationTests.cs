using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Features.Auth;
using Nexo.Application.Features.Cash;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// Regression suite for store-level isolation within a single tenant.
/// Tenant isolation alone is not enough — users scoped to Store A must not
/// see or mutate data belonging to Store B.
///
/// Mechanism: EF Core global query filter on StoreEntity subclasses applies
///   WHERE tenant_id = X AND store_id = Y
/// The store_id Y comes from the JWT claim "storeId", which is set by login
/// and updated by switch-store.
/// </summary>
[Collection("Integration")]
public class StoreIsolationTests
{
    private readonly TestWebApplicationFactory _factory;

    public StoreIsolationTests(TestWebApplicationFactory factory)
        => _factory = factory;

    // ── Test A ────────────────────────────────────────────────────────────────
    // A cash session opened under Store B must not appear in Store A's session list.
    // CashSession is a StoreEntity — the global filter enforces store_id isolation.

    [Fact]
    public async Task CashSession_OpenedInStoreB_IsNotVisibleFromStoreA()
    {
        var (storeAId, storeBId) = await EnsureTwoStoresAsync();

        // Login — default token has storeA (alphabetically first; storeA name < "Z-Store-B")
        var (loginClient, loginBody) = await LoginAsync("admin", "nexo@2026");
        var storeAToken = loginBody.AccessToken;
        loginBody.Session.StoreId.Should().Be(storeAId.ToString(),
            "default store after login must be Store A — if not, store seeding order is wrong");

        // Switch to Store B → fresh token pair scoped to storeB
        var switchResponse = await loginClient.WithBearer(storeAToken)
            .PostAsJsonAsync("/api/auth/switch-store", new { storeId = storeBId.ToString() });
        switchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var switchBody = await switchResponse.Content.ReadFromJsonAsync<SwitchStoreResponse>();
        var storeBAccessToken = switchBody!.AccessToken;
        switchBody.StoreId.Should().Be(storeBId.ToString());

        // Open a cash session in Store B
        var storeBClient = _factory.CreateApiClient().WithBearer(storeBAccessToken);
        var openResponse = await storeBClient.PostAsJsonAsync("/api/cash/sessions/open",
            new OpenCashSessionRequest(OpeningBalance: 0, Notes: "Store B isolation test"));

        // Accept 201 Created or 409 Conflict (session already open in this store from a prior run)
        openResponse.StatusCode.Should()
            .BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict,
                "expected either a new session or an already-open session in Store B");

        Guid storeBSessionId;
        if (openResponse.StatusCode == HttpStatusCode.Created)
        {
            var session = await openResponse.Content.ReadFromJsonAsync<CashSessionDto>();
            storeBSessionId = session!.Id;
        }
        else
        {
            // Conflict — retrieve the already-open session to get its ID
            var getOpen = await storeBClient.GetAsync("/api/cash/sessions/open");
            getOpen.StatusCode.Should().Be(HttpStatusCode.OK);
            var existingSession = await getOpen.Content.ReadFromJsonAsync<CashSessionDto>();
            storeBSessionId = existingSession!.Id;
        }

        // Now use Store A token and verify Store B's session is NOT in the list
        var storeAClient = _factory.CreateApiClient().WithBearer(storeAToken);
        var listResponse = await storeAClient.GetAsync("/api/cash/sessions");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var sessions = await listResponse.Content.ReadFromJsonAsync<List<CashSessionDto>>();
        sessions.Should().NotBeNull();
        sessions!.Should().NotContain(s => s.Id == storeBSessionId,
            "Store A's session list must not include Store B's cash session");
    }

    // ── Test B ────────────────────────────────────────────────────────────────
    // Switching to a valid same-tenant store must succeed and update the token's storeId.
    // GET /api/auth/me after switching confirms the JWT carries the new store context.

    [Fact]
    public async Task SwitchStore_ValidSameTenantStore_ReturnsNewTokenWithCorrectStoreId()
    {
        var (storeAId, storeBId) = await EnsureTwoStoresAsync();

        var (client, loginBody) = await LoginAsync("admin", "nexo@2026");
        var storeAToken = loginBody.AccessToken;

        // Verify starting store
        loginBody.Session.StoreId.Should().Be(storeAId.ToString());

        // Switch to Store B
        var switchResponse = await client.WithBearer(storeAToken)
            .PostAsJsonAsync("/api/auth/switch-store", new { storeId = storeBId.ToString() });

        switchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var switchBody = await switchResponse.Content.ReadFromJsonAsync<SwitchStoreResponse>();
        switchBody.Should().NotBeNull();
        switchBody!.StoreId.Should().Be(storeBId.ToString());
        switchBody.AccessToken.Should().NotBeNullOrEmpty();
        switchBody.RefreshToken.Should().NotBeNullOrEmpty();

        // Confirm the new access token's storeId claim via /api/auth/me
        var meResponse = await client.WithBearer(switchBody.AccessToken).GetAsync("/api/auth/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await meResponse.Content.ReadFromJsonAsync<SessionDto>();
        session!.StoreId.Should().Be(storeBId.ToString(),
            "/api/auth/me must reflect the new storeId after switch-store");
    }

    // ── Test C ────────────────────────────────────────────────────────────────
    // Switching to a store that belongs to a different tenant must be forbidden.
    // SwitchStoreAsync validates store.TenantId == callerTenantId before issuing tokens.

    [Fact]
    public async Task SwitchStore_CrossTenantStore_IsForbidden()
    {
        var tenantBStoreId = await SeedCrossTenantStoreAsync();

        var (client, loginBody) = await LoginAsync("admin", "nexo@2026");

        var response = await client.WithBearer(loginBody.AccessToken)
            .PostAsJsonAsync("/api/auth/switch-store",
                new { storeId = tenantBStoreId.ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "switching to a store from another tenant must be rejected");
    }

    // ── Test D ────────────────────────────────────────────────────────────────
    // Switching to a store with an invalid GUID must return 400, not 500.

    [Fact]
    public async Task SwitchStore_InvalidStoreIdFormat_ReturnsBadRequest()
    {
        var (client, loginBody) = await LoginAsync("admin", "nexo@2026");

        var response = await client.WithBearer(loginBody.AccessToken)
            .PostAsJsonAsync("/api/auth/switch-store", new { storeId = "not-a-guid" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(HttpClient Client, LoginResponse Body)> LoginAsync(
        string login, string password)
    {
        var client = _factory.CreateApiClient();
        var r = await client.PostAsJsonAsync("/api/auth/login", new { login, password });
        r.StatusCode.Should().Be(HttpStatusCode.OK, $"login as '{login}' must succeed");
        var body = await r.Content.ReadFromJsonAsync<LoginResponse>();
        return (client, body!);
    }

    /// <summary>
    /// Ensures the seeded tenant has exactly two active stores:
    ///   - The original seeded store (comes first alphabetically = default after login)
    ///   - "Z-Store-B" (comes last alphabetically = not the default)
    /// Returns (storeAId, storeBId).
    /// Idempotent.
    /// </summary>
    private async Task<(Guid StoreAId, Guid StoreBId)> EnsureTwoStoresAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

        // CROSS-TENANT: seeder context
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync();

        var stores = await db.Stores
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenant.Id && s.Status == Domain.Enums.StoreStatus.Active)
            .OrderBy(s => s.Name)
            .ToListAsync();

        // Store A = first alphabetically (already seeded)
        var storeA = stores.First();

        // Store B = create if missing. Slug "z-store-b-isolation" puts it last alphabetically.
        const string storeBSlug = "z-store-b-isolation";
        var storeB = stores.FirstOrDefault(s => s.Slug == storeBSlug);

        if (storeB is null)
        {
            storeB = Store.Create(tenant.Id, "Z-Store-B", storeBSlug);
            db.Stores.Add(storeB);
            await db.SaveChangesAsync();
        }

        return (storeA.Id, storeB.Id);
    }

    /// <summary>
    /// Seeds a store in a completely separate tenant.
    /// Used to test that switch-store rejects cross-tenant store IDs.
    /// Idempotent on TaxId.
    /// </summary>
    private async Task<Guid> SeedCrossTenantStoreAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

        const string taxId = "77666555000199";
        const string storeSlug = "cross-tenant-store-b";

        // CROSS-TENANT: seeder context
        var existing = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TaxId == taxId);

        if (existing is not null)
        {
            var existingStore = await db.Stores
                .IgnoreQueryFilters()
                .FirstAsync(s => s.TenantId == existing.Id && s.Slug == storeSlug);
            return existingStore.Id;
        }

        var tenant = Tenant.Create("Cross Tenant Store Corp", taxId, "admin@ctsc.test");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var store = Store.Create(tenant.Id, "Cross Tenant Store B", storeSlug);
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        return store.Id;
    }
}

file static class HttpClientExtensions
{
    public static HttpClient WithBearer(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
