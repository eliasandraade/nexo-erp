using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// Behavioural half of the RBAC matrix (docs/security/rbac-matrix.md): one user per role of the
/// same tenant calls representative actions of every area. "Allowed" means the request got past
/// authorization (any status but 401/403 — ids are random, so it ends in 404/400 and writes
/// nothing); "denied" means 403.
/// </summary>
[Collection("Integration")]
public class RbacMatrixTests
{
    private const string Seller = "Vendedor", Stocker = "Estoquista", Kitchen = "Cozinha", Manager = "Gerente";
    private static readonly ConcurrentDictionary<string, (HttpClient Client, Guid UserId)> Clients = new();
    private static readonly string R = Guid.NewGuid().ToString();   // random id → 404 once authorized

    private readonly TestWebApplicationFactory _factory;
    public RbacMatrixTests(TestWebApplicationFactory factory) => _factory = factory;

    public static TheoryData<string, string, string, bool> Matrix() => new()
    {
        // ── Vendas / PDV ──
        { "GET",    $"/api/sales/{R}",                         Seller,  true  },
        { "POST",   $"/api/sales/{R}/confirm",                 Seller,  true  },
        { "POST",   $"/api/sales/{R}/cancel",                  Seller,  false },
        { "POST",   $"/api/sales/{R}/cancel",                  Manager, true  },
        { "GET",    "/api/sales",                              Seller,  false },
        { "POST",   $"/api/sales/{R}/returns",                 Seller,  false },
        { "POST",   $"/api/sales/{R}/returns",                 Manager, true  },
        { "GET",    $"/api/sales/{R}/returns",                 Seller,  false },
        { "GET",    $"/api/varejo/pdv/resolve-price?productId={R}", Seller, true },
        { "GET",    "/api/varejo/price-lists",                 Seller,  false },
        // ── Caixa ──
        { "GET",    "/api/cash/sessions/open",                 Seller,  true  },
        { "GET",    "/api/cash/sessions",                      Seller,  false },
        { "POST",   $"/api/cash/sessions/{R}/close",           Seller,  false },
        { "POST",   $"/api/cash/sessions/{R}/movements",       Seller,  false },
        { "POST",   $"/api/cash/sessions/{R}/close",           Manager, true  },
        // ── Catálogo, clientes, fornecedores, estoque, compras ──
        { "GET",    "/api/products",                           Kitchen, true  },
        { "POST",   $"/api/products/{R}/deactivate",           Seller,  false },
        { "POST",   $"/api/products/{R}/deactivate",           Stocker, true  },
        { "GET",    "/api/customers",                          Seller,  true  },
        { "POST",   $"/api/customers/{R}/deactivate",          Seller,  false },
        { "POST",   $"/api/customers/{R}/deactivate",          Manager, true  },
        { "GET",    "/api/suppliers",                          Seller,  false },
        { "GET",    "/api/suppliers",                          Stocker, true  },
        { "GET",    "/api/stock",                              Seller,  true  },   // PDV flags out-of-stock items
        { "GET",    "/api/stock",                              Kitchen, false },
        { "GET",    "/api/stock",                              Stocker, true  },
        { "GET",    "/api/stock/counts",                       Stocker, true  },
        { "GET",    "/api/stock/counts",                       Seller,  false },
        { "POST",   "/api/stock/transfers",                    Seller,  false },
        { "POST",   "/api/stock/transfers",                    Kitchen, false },
        { "GET",    $"/api/stock/product/{R}/movements",       Seller,  false },
        { "GET",    "/api/customers",                          Kitchen, false },   // personal data
        { "GET",    "/api/customers",                          Stocker, false },
        { "PATCH",  $"/api/products/{R}/prices",               Seller,  false },
        { "GET",    "/api/varejo/purchases",                   Stocker, true  },
        { "POST",   $"/api/varejo/purchases/{R}/confirm",      Stocker, false },
        { "POST",   $"/api/varejo/purchases/{R}/confirm",      Manager, true  },
        { "POST",   "/api/integrations/storage/upload",        Seller,  false },
        { "POST",   "/api/integrations/storage/upload",        Stocker, true  },
        // ── Gestão ──
        { "GET",    "/api/dashboard/summary",                  Seller,  false },
        { "GET",    "/api/dashboard/summary",                  Stocker, false },
        { "GET",    "/api/dashboard/summary",                  Manager, true  },
        { "GET",    "/api/reports/sales",                      Seller,  false },
        // ── Restaurante ──
        { "GET",    "/api/restaurante/orders",                 Kitchen, true  },
        { "GET",    "/api/restaurante/orders",                 Stocker, false },
        { "PATCH",  $"/api/restaurante/orders/{R}/items/{R}/status", Kitchen, true },
        { "POST",   $"/api/restaurante/orders/{R}/pay",        Kitchen, false },
        { "POST",   "/api/restaurante/orders",                 Kitchen, false },
        { "POST",   $"/api/restaurante/orders/{R}/cancel",     Kitchen, false },
        { "POST",   $"/api/restaurante/orders/{R}/pay",        Seller,  true  },
        { "POST",   "/api/restaurante/tables",                 Seller,  false },
        { "PATCH",  $"/api/restaurante/tables/{R}/status",     Seller,  true  },
        { "PATCH",  $"/api/restaurante/tables/{R}/status",     Kitchen, false },
        { "GET",    "/api/restaurante/modifier-groups",        Kitchen, true  },
        { "POST",   "/api/restaurante/modifier-groups",        Seller,  false },
        { "PUT",    "/api/restaurante/settings",               Seller,  false },
        { "GET",    "/api/restaurante/delivery-orders",        Seller,  true  },
        { "GET",    "/api/restaurante/delivery-orders",        Kitchen, false },
        { "DELETE", $"/api/restaurante/coupons/{R}",           Seller,  false },
        { "GET",    "/api/restaurante/recipe-cards",           Kitchen, false },   // costs: stock only (PR 48)
        { "GET",    "/api/restaurante/recipe-cards",           Seller,  false },
        { "GET",    "/api/restaurante/employees",              Seller,  false },
        { "GET",    "/api/restaurante/employees",              Manager, true  },
        { "GET",    "/api/restaurante/expenses",               Seller,  false },
        { "GET",    "/api/restaurante/financeiro/summary",     Seller,  false },
        // ── Build / Interpreter ──
        { "GET",    "/api/v1/build/projects",                  Seller,  false },
        { "GET",    "/api/v1/build/projects",                  Manager, true  },
        { "POST",   $"/api/v1/build/budgets/{R}/approve",      Seller,  false },
        { "GET",    "/api/v1/movements",                       Seller,  false },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Role_matrix_is_enforced_by_the_api(string method, string url, string role, bool allowed)
    {
        var (client, _) = await ClientForAsync(role);
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (url.EndsWith("/storage/upload"))
            request.Content = new MultipartFormDataContent { { new StringContent("product-image"), "context" } };
        else if (method is "POST" or "PUT" or "PATCH")
            request.Content = JsonContent.Create(new { });

        var response = await client.SendAsync(request);

        if (allowed)
            response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, $"{role} may call {method} {url}")
                .And.NotBe(HttpStatusCode.Unauthorized);
        else
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{role} must not call {method} {url}");
    }

    [Fact]
    public async Task Users_can_read_and_change_only_their_own_account()
    {
        var (seller, sellerId) = await ClientForAsync(Seller);
        var (_, managerId) = await ClientForAsync(Manager);

        (await seller.GetAsync($"/api/users/{sellerId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await seller.GetAsync($"/api/users/{managerId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await seller.PostAsJsonAsync($"/api/users/{managerId}/change-password",
                new { currentPassword = "guess-1", newPassword = "whatever-123" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var (manager, _) = await ClientForAsync(Manager);
        (await manager.GetAsync($"/api/users/{sellerId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_platform_admin_token_is_not_a_tenant_user()
    {
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Nexo.Infrastructure.Persistence.Seed.DataSeeder>()
                .SeedPlatformAdminAsync();

        var client = _factory.CreateApiClient();
        var login = await client.PostAsJsonAsync("/api/platform/auth/login",
            new { email = TestCredentials.PlatformEmail, password = TestCredentials.PlatformPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement
            .GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        foreach (var url in new[] { "/api/dashboard/summary", "/api/financial/accounts", "/api/financial/ledger", "/api/restaurante/employees" })
            (await client.GetAsync(url)).StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<(HttpClient Client, Guid UserId)> ClientForAsync(string role)
    {
        if (Clients.TryGetValue(role, out var cached)) return cached;
        await EnsureModulesAsync("varejo", "restaurante", "build");

        var login = $"rbac.matrix.{role.ToLowerInvariant()}";
        Guid userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Login == login);
            if (user is null)
            {
                var admin = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Login == TestCredentials.AdminLogin);
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                user = User.Create(
                    tenantId: admin.TenantId, fullName: $"RBAC {role}",
                    email: $"{login}@{TestCredentials.TestLocalDomain}", login: login,
                    passwordHash: hasher.Hash(TestCredentials.SameTenantManagerPassword),
                    role: Enum.Parse<UserRole>(role));
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }
            userId = user.Id;
        }

        var client = await AuthClientFactory.LoginAsync(_factory, login, TestCredentials.SameTenantManagerPassword);
        return Clients.GetOrAdd(role, (client, userId));
    }

    /// <summary>Module gates answer 403 too — make sure the tenant has every module under test.</summary>
    private async Task EnsureModulesAsync(params string[] moduleKeys)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var admin = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Login == TestCredentials.AdminLogin);
        foreach (var key in moduleKeys)
        {
            if (await db.ModuleSubscriptions.IgnoreQueryFilters().AnyAsync(s => s.TenantId == admin.TenantId && s.ModuleKey == key))
                continue;
            db.ModuleSubscriptions.Add(ModuleSubscription.CreateFromStripe(
                tenantId: admin.TenantId, moduleKey: key,
                stripeSubscriptionId: $"sub_test_{key}", stripePriceId: $"price_test_{key}",
                planType: PlanType.Lifetime, periodStart: DateTime.UtcNow, periodEnd: null));
        }
        await db.SaveChangesAsync();
    }
}
