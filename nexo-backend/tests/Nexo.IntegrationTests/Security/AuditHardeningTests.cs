using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Sales;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// Regressions for the P0 findings of the 2026-10 product audit: cross-tenant slug write, the
/// active store lost on token refresh, inverted stock adjustments, negative sale lines, ingredients
/// and invalid quantities on the public menu, and role checks on money / audit endpoints.
/// </summary>
[Collection("Integration")]
public class AuditHardeningTests
{
    private const string ForeignTaxId = "33222111000144";
    private const string SellerLogin  = "audit.hardening.seller";

    private readonly TestWebApplicationFactory _factory;
    public AuditHardeningTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── Cross-tenant write on the public slug ────────────────────────────────
    [Fact]
    public async Task Public_slug_of_another_tenants_store_cannot_be_changed()
    {
        var (foreignStoreId, foreignSlug) = await SeedForeignStoreWithSlugAsync();
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);

        var r = await c.PatchAsJsonAsync($"/api/stores/{foreignStoreId}/public-slug", new { publicSlug = (string?)null });
        r.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.Stores.IgnoreQueryFilters().SingleAsync(s => s.Id == foreignStoreId))
            .PublicSlug.Should().Be(foreignSlug, "the foreign portal must stay untouched");
    }

    // ── Refresh keeps the active store ───────────────────────────────────────
    [Fact]
    public async Task Refresh_keeps_the_store_the_user_switched_to()
    {
        var storeB = await GetOrCreateSecondStoreIdAsync();
        var client = _factory.CreateApiClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login", TestCredentials.AdminLoginPayload()))
            .Content.ReadFromJsonAsync<JsonElement>();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        var switched = await client.PostAsJsonAsync("/api/auth/switch-store",
            new { storeId = storeB.ToString(), refreshToken = login.GetProperty("refreshToken").GetString() });
        switched.StatusCode.Should().Be(HttpStatusCode.OK);
        var switchBody = await switched.Content.ReadFromJsonAsync<JsonElement>();

        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(switchBody.GetProperty("refreshToken").GetString()!));
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var newAccess = (await refreshed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        StoreIdClaim(newAccess).Should().Be(storeB.ToString(), "a refresh must not move the session back to the first store");
    }

    // ── Stock adjustments follow the movement type ───────────────────────────
    [Fact]
    public async Task Manual_exit_and_loss_decrease_stock_and_adjustment_is_signed()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var product = await CreateProductAsync(c, isIngredient: false);

        await AdjustAsync(c, product, 10m, "ManualEntry");
        await AdjustAsync(c, product, 3m, "ManualExit");
        await AdjustAsync(c, product, 2m, "Loss");
        await AdjustAsync(c, product, -1m, "Adjustment");

        var stock = await c.GetFromJsonAsync<JsonElement>($"/api/stock/product/{product}");
        stock.GetProperty("currentQuantity").GetDecimal().Should().Be(4m, "10 − 3 − 2 − 1");

        (await c.PostAsJsonAsync("/api/stock/adjust", new { productId = product, quantity = 1m, movementType = "SaleOutput" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "sale movements are not manual adjustments");
        (await c.PostAsJsonAsync("/api/stock/adjust", new { productId = product, quantity = 0m, movementType = "Adjustment" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── Sale lines cannot be negative ────────────────────────────────────────
    [Theory]
    [InlineData(-1, 10, 0)]
    [InlineData(1, -10, 0)]
    [InlineData(1, 10, 11)]
    public async Task Sale_items_with_negative_quantity_price_or_excess_discount_are_rejected(
        decimal quantity, decimal unitPrice, decimal discount)
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var product = await CreateProductAsync(c, isIngredient: false);
        var sale = await (await c.PostAsJsonAsync("/api/sales", new CreateSaleRequest()))
            .Content.ReadFromJsonAsync<SaleDto>();

        var r = await c.PostAsJsonAsync($"/api/sales/{sale!.Id}/items",
            new AddSaleItemRequest(product, quantity, unitPrice, DiscountAmount: discount));
        r.IsSuccessStatusCode.Should().BeFalse();
        ((int)r.StatusCode).Should().BeOneOf(400, 422);
    }

    // ── Public menu ──────────────────────────────────────────────────────────
    [Fact]
    public async Task Public_menu_never_lists_ingredients_and_rejects_invalid_quantities()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var slug = await EnsureAdminStoreHasPublicSlugAsync(c);
        var dish = await CreateProductAsync(c, isIngredient: false, name: "Prato Audit " + Guid.NewGuid().ToString("N")[..6]);
        var ingredientName = "Insumo Audit " + Guid.NewGuid().ToString("N")[..6];
        await CreateProductAsync(c, isIngredient: true, name: ingredientName);

        var anon = AuthClientFactory.CreateAnonymousClient(_factory);
        var menu = await anon.GetStringAsync($"/api/public/menu/{slug}");
        menu.Should().NotContain(ingredientName);

        var phone = "1199" + Random.Shared.Next(1000000, 9999999);
        var bad = await anon.PostAsJsonAsync("/api/public/orders", new
        {
            publicSlug = slug, orderType = "Takeaway", customerName = "Cliente", customerPhone = phone,
            items = new[] { new { productId = dish, quantity = -2m } },
        });
        bad.IsSuccessStatusCode.Should().BeFalse("a negative quantity must never create an order");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.RestDeliveryOrders.IgnoreQueryFilters().CountAsync(o => o.CustomerPhone == phone))
            .Should().Be(0, "nothing is saved for a rejected request");
    }

    // ── Role checks ──────────────────────────────────────────────────────────
    [Fact]
    public async Task Financial_audit_billing_and_stock_adjust_require_the_right_role()
    {
        var seller = await LoginAsSellerOfAdminTenantAsync();
        (await seller.GetAsync("/api/financial/accounts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await seller.GetAsync("/api/audit")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await seller.PostAsJsonAsync("/api/billing/portal", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await seller.PostAsJsonAsync("/api/stock/adjust", new { productId = Guid.NewGuid(), quantity = 1m, movementType = "ManualEntry" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await admin.GetAsync("/api/financial/accounts")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/audit")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static string StoreIdClaim(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        return doc.RootElement.GetProperty("storeId").GetString()!;
    }

    private static async Task<Guid> CreateProductAsync(HttpClient c, bool isIngredient, string? name = null)
    {
        var code = "AUD" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var r = await c.PostAsJsonAsync("/api/products", new CreateProductRequest(
            Code: code, Name: name ?? $"Produto {code}", Unit: "Un", SalePrice: 10m, CostPrice: 5m,
            TrackStock: true, IsIngredient: isIngredient));
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<ProductDto>())!.Id;
    }

    private static async Task AdjustAsync(HttpClient c, Guid product, decimal quantity, string type)
        => (await c.PostAsJsonAsync("/api/stock/adjust", new { productId = product, quantity, movementType = type }))
            .StatusCode.Should().Be(HttpStatusCode.OK, $"{type} {quantity}");

    private static async Task<string> EnsureAdminStoreHasPublicSlugAsync(HttpClient c)
    {
        var stores = await c.GetFromJsonAsync<JsonElement[]>("/api/stores");
        var mine = stores!.First();
        if (mine.GetProperty("publicSlug").ValueKind == JsonValueKind.String)
            return mine.GetProperty("publicSlug").GetString()!;
        var slug = "audit-menu-" + Guid.NewGuid().ToString("N")[..6];
        (await c.PatchAsJsonAsync($"/api/stores/{mine.GetProperty("id").GetString()}/public-slug", new { publicSlug = slug }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        return slug;
    }

    private async Task<(Guid StoreId, string Slug)> SeedForeignStoreWithSlugAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var t = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TaxId == ForeignTaxId);
        if (t is null)
        {
            t = Tenant.Create("Slug Isolation Corp", ForeignTaxId, "admin@slug-iso.test");
            db.Tenants.Add(t);
            await db.SaveChangesAsync();
        }
        var store = await db.Stores.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == t.Id);
        if (store is null)
        {
            store = Store.Create(t.Id, "SIC Store", "slug-iso-default");
            store.SetPublicSlug("slug-iso-portal");
            db.Stores.Add(store);
            await db.SaveChangesAsync();
        }
        return (store.Id, store.PublicSlug!);
    }

    private async Task<Guid> GetOrCreateSecondStoreIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var admin = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Login == TestCredentials.AdminLogin);
        const string slug = "z-store-b-auth-test";
        var existing = await db.Stores.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == admin.TenantId && s.Slug == slug);
        if (existing is not null) return existing.Id;
        var store = Store.Create(admin.TenantId, "Z-Store-B-AuthTest", slug);
        db.Stores.Add(store);
        await db.SaveChangesAsync();
        return store.Id;
    }

    private async Task<HttpClient> LoginAsSellerOfAdminTenantAsync()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            if (!await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Login == SellerLogin))
            {
                var admin = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Login == TestCredentials.AdminLogin);
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                db.Users.Add(User.Create(
                    tenantId: admin.TenantId, fullName: "Audit Seller",
                    email: $"{SellerLogin}@{TestCredentials.TestLocalDomain}", login: SellerLogin,
                    passwordHash: hasher.Hash(TestCredentials.SameTenantManagerPassword), role: UserRole.Vendedor));
                await db.SaveChangesAsync();
            }
        }
        return await AuthClientFactory.LoginAsync(_factory, SellerLogin, TestCredentials.SameTenantManagerPassword);
    }
}
