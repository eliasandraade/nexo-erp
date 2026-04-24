using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Application.Features.Users;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// Regression suite for cross-tenant isolation.
/// A failing test here means a real security regression — treat it as P0.
///
/// Design rules:
///   - Every test creates its own HttpClient (no shared auth state between tests).
///   - Every seeding helper is idempotent (safe to re-run against the shared DB).
///   - Every assertion has a positive control so the test cannot trivially pass on a broken API.
/// </summary>
[Collection("Integration")]
public class TenantIsolationTests
{
    private readonly TestWebApplicationFactory _factory;

    public TenantIsolationTests(TestWebApplicationFactory factory)
        => _factory = factory;

    // ── Test 1 ────────────────────────────────────────────────────────────────
    // VerifyManager: credentials from a different tenant must always return Authorized=false.
    // Regression for: VerifyManagerAsync using IgnoreQueryFilters() without a tenant check.
    // This test FAILS before the fix (user.TenantId != callerTenantId guard) and
    // PASSES after it.

    [Fact]
    public async Task VerifyManager_CrossTenant_ManagerCredentials_AreRejected()
    {
        var (_, managerLogin, managerPassword) =
            await SeedTenantWithManagerAsync("cross-tenant-corp", "11222333000188");

        var client = await AuthenticatedClientAsync("admin", "nexo@2026");

        var response = await client.PostAsJsonAsync("/api/auth/verify-manager",
            new { login = managerLogin, password = managerPassword });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<VerifyManagerResponse>();
        body!.Authorized.Should().BeFalse(
            "a manager from a different tenant must never be authorized");
        body.ManagerUserId.Should().BeNull(
            "no manager ID should leak on a rejected cross-tenant attempt");
    }

    // ── Test 2 ────────────────────────────────────────────────────────────────
    // VerifyManager: same-tenant manager must succeed and return the exact user.

    [Fact]
    public async Task VerifyManager_SameTenant_ManagerCredentials_AreAccepted()
    {
        var client = await AuthenticatedClientAsync("admin", "nexo@2026");

        var login    = $"gerente_{Guid.NewGuid():N}"[..20];
        var password = "Manager@1234!";

        var createResponse = await client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            FullName:              "Test Manager",
            Email:                 $"{login}@nexo-test.com",
            Login:                 login,
            Password:              password,
            Role:                  "gerente",
            Phone:                 null,
            Notes:                 null,
            RequirePasswordChange: false));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<UserDto>();

        var response = await client.PostAsJsonAsync("/api/auth/verify-manager",
            new { login, password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<VerifyManagerResponse>();
        body!.Authorized.Should().BeTrue();
        body.Role.Should().Be("gerente");
        // Pin the returned identity — wrong user returning would fail here
        body.ManagerUserId.Should().Be(created!.Id,
            "must return exactly the user that was created, not another manager");
    }

    // ── Test 3 ────────────────────────────────────────────────────────────────
    // Product (TenantEntity): Tenant A cannot access a Tenant B product by ID.
    // Positive control: product is confirmed in DB before testing API visibility.

    [Fact]
    public async Task GetProduct_TenantA_CannotAccessTenantB_Product()
    {
        var tenantBProductId = await SeedTenantBProductAsync();

        // Positive control: confirm product exists in DB (test is not vacuously 404)
        await AssertProductExistsInDbAsync(tenantBProductId);

        var client = await AuthenticatedClientAsync("admin", "nexo@2026");

        var response = await client.GetAsync($"/api/products/{tenantBProductId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "EF Core global query filter on TenantId must hide Tenant B's product from Tenant A");
    }

    // ── Test 4 ────────────────────────────────────────────────────────────────
    // Unauthenticated callers must receive 401 on every protected endpoint.

    [Fact]
    public async Task VerifyManager_WithoutToken_Returns401()
    {
        var client = _factory.CreateApiClient();
        var response = await client.PostAsJsonAsync("/api/auth/verify-manager",
            new { login = "admin", password = "nexo@2026" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUsers_WithoutToken_Returns401()
    {
        var client = _factory.CreateApiClient();
        var response = await client.GetAsync("/api/users");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Test 5 ────────────────────────────────────────────────────────────────
    // GET /api/users: Tenant A's list must never include Tenant B users.
    // Positive control: Tenant A's own admin IS in the list (proves API is not returning empty).

    [Fact]
    public async Task GetUsers_TenantA_NeverReturnsTenantB_Users()
    {
        var (_, managerLogin, _) =
            await SeedTenantWithManagerAsync("isolation-corp", "99888777000100");

        var client = await AuthenticatedClientAsync("admin", "nexo@2026");

        var response = await client.GetAsync("/api/users");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var users = await response.Content.ReadFromJsonAsync<List<UserDto>>();
        users.Should().NotBeNull();

        // Negative: Tenant B user must not appear
        users!.Should().NotContain(u => u.Login == managerLogin,
            "global query filter must exclude Tenant B users");

        // Positive control: Tenant A's own admin must appear (proves list is not vacuously empty)
        users.Should().Contain(u => u.Login == "admin",
            "Tenant A's admin must be present — if absent the list endpoint is broken");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a fresh HttpClient and sets its Authorization header by logging in.
    /// Each test gets its own client — no shared auth state.
    /// </summary>
    private async Task<HttpClient> AuthenticatedClientAsync(string login, string password)
    {
        var client = _factory.CreateApiClient();
        var r = await client.PostAsJsonAsync("/api/auth/login", new { login, password });
        r.StatusCode.Should().Be(HttpStatusCode.OK, $"login as '{login}' failed");
        var body = await r.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return client;
    }

    /// <summary>
    /// Seeds Tenant B + an active Gerente user directly via DbContext.
    /// Idempotent on TaxId — safe to call multiple times in the same test run.
    /// Returns (tenantId, login, password).
    /// </summary>
    private async Task<(Guid TenantId, string Login, string Password)>
        SeedTenantWithManagerAsync(string companyName, string taxId)
    {
        using var scope  = _factory.Services.CreateScope();
        var db     = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var login    = $"mgr_{taxId[..8]}";
        var password = "TenantB@1234!";

        // CROSS-TENANT: seeder context — no HTTP request, no tenant filter
        var existing = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TaxId == taxId);

        if (existing is not null)
        {
            var existingUser = await db.Users
                .IgnoreQueryFilters()
                .FirstAsync(u => u.TenantId == existing.Id && u.Login == login);
            return (existing.Id, existingUser.Login, password);
        }

        var tenant = Tenant.Create(companyName, taxId, $"admin@{companyName}.test");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        // Store required so tenant passes middleware validation
        db.Stores.Add(Store.Create(tenant.Id, $"{companyName} Store", $"store-{taxId[..8]}"));

        db.Users.Add(User.Create(
            tenantId:     tenant.Id,
            fullName:     "Tenant B Manager",
            email:        $"{login}@tenantb.test",
            login:        login,
            passwordHash: hasher.Hash(password),
            role:         UserRole.Gerente));

        await db.SaveChangesAsync();
        return (tenant.Id, login, password);
    }

    /// <summary>
    /// Seeds a product for Tenant B. Product is a TenantEntity (not StoreEntity) — no store needed.
    /// Idempotent: always inserts a fresh product (uses Guid.NewGuid() code).
    /// </summary>
    private async Task<Guid> SeedTenantBProductAsync()
    {
        using var scope  = _factory.Services.CreateScope();
        var db     = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

        var taxId = "55444333000122";
        // CROSS-TENANT: seeder context
        var tenantB = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TaxId == taxId);

        if (tenantB is null)
        {
            tenantB = Tenant.Create("Product Isolation Corp", taxId, "admin@pic.test");
            db.Tenants.Add(tenantB);
            // Store required for middleware
            db.Stores.Add(Store.Create(tenantB.Id, "PIC Store", "pic-default"));
            await db.SaveChangesAsync();
        }

        // Always inserts a fresh product — the test needs a product guaranteed to belong to Tenant B
        var product = Product.Create(
            tenantId:   tenantB.Id,
            code:       $"TB{Guid.NewGuid():N}"[..20],
            name:       "Tenant B Secret Product",
            unit:       ProductUnit.Un,
            salePrice:  99.99m,
            costPrice:  50.00m,
            trackStock: false);

        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private async Task AssertProductExistsInDbAsync(Guid productId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var exists = await db.Products
            .IgnoreQueryFilters()   // CROSS-TENANT: direct DB check, no HTTP context
            .AnyAsync(p => p.Id == productId);
        exists.Should().BeTrue("seeding failed — product does not exist in DB, test is vacuously 404");
    }
}
