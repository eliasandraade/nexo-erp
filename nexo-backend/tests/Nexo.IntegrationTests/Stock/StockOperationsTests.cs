using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Stock;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Stock;

/// <summary>Inventário (contagem física) and transferência between stores (PR 46).</summary>
[Collection("Integration")]
public class StockOperationsTests
{
    private const string SecondStoreSlug = "z-stock-ops-destination";
    private readonly TestWebApplicationFactory _factory;
    public StockOperationsTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Applying_a_count_sets_the_counted_quantity_against_the_stock_at_apply_time()
    {
        var c = await StockerAsync("stockops.count");
        var a = await ProductWithStockAsync(c, 10);
        var b = await ProductWithStockAsync(c, 5);

        var count = await OkAsync<InventoryCountDto>(await c.PostAsJsonAsync("/api/stock/counts",
            new CreateInventoryCountRequest([a, b], "contagem mensal")));
        count.Items.Single(i => i.ProductId == a).ExpectedQuantity.Should().Be(10);

        await OkAsync<InventoryCountDto>(await c.PutAsJsonAsync($"/api/stock/counts/{count.Id}",
            new UpdateInventoryCountRequest([new InventoryCountEntry(a, 8)])));
        // a unit leaves while counting (e.g. a sale) — it must not be undone by the count
        (await c.PostAsJsonAsync("/api/stock/adjust", new AdjustStockRequest(a, 1, "ManualExit", "venda no meio da contagem")))
            .EnsureSuccessStatusCode();

        var applied = await OkAsync<InventoryCountDto>(await c.PostAsync($"/api/stock/counts/{count.Id}/apply", null));

        applied.Status.Should().Be("Applied");
        var itemA = applied.Items.Single(i => i.ProductId == a);
        itemA.SystemQuantityAtApply.Should().Be(9);
        itemA.Difference.Should().Be(-1);
        applied.Items.Single(i => i.ProductId == b).Difference.Should().BeNull();   // not counted → untouched
        (await StockAsync(c, a)).Should().Be(8);
        (await StockAsync(c, b)).Should().Be(5);

        (await c.PutAsJsonAsync($"/api/stock/counts/{count.Id}", new UpdateInventoryCountRequest([new InventoryCountEntry(a, 1)])))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await c.PostAsync($"/api/stock/counts/{count.Id}/apply", null))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_count_with_nothing_counted_cannot_be_applied_but_can_be_cancelled()
    {
        var c = await StockerAsync("stockops.cancel");
        var a = await ProductWithStockAsync(c, 3);
        var count = await OkAsync<InventoryCountDto>(await c.PostAsJsonAsync("/api/stock/counts",
            new CreateInventoryCountRequest([a])));

        (await c.PostAsync($"/api/stock/counts/{count.Id}/apply", null))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await OkAsync<InventoryCountDto>(await c.PostAsync($"/api/stock/counts/{count.Id}/cancel", null)))
            .Status.Should().Be("Cancelled");
        (await StockAsync(c, a)).Should().Be(3);
    }

    [Fact]
    public async Task A_transfer_moves_stock_between_stores_atomically()
    {
        var c = await StockerAsync("stockops.transfer");
        var destination = await OtherStoreAsync(c);
        var a = await ProductWithStockAsync(c, 10);

        var transfer = await OkAsync<StockTransferDto>(await c.PostAsJsonAsync("/api/stock/transfers",
            new CreateStockTransferRequest(destination, [new StockTransferItemInput(a, 4)], "reposição filial")));

        transfer.Direction.Should().Be("out");
        (await StockAsync(c, a)).Should().Be(6);
        (await StockInStoreAsync(destination, a)).Should().Be(4);
        (await MovementTypesAsync(transfer.Id)).Should().BeEquivalentTo(["TransferOut", "TransferIn"]);

        // not enough at the origin → nothing moves
        (await c.PostAsJsonAsync("/api/stock/transfers",
                new CreateStockTransferRequest(destination, [new StockTransferItemInput(a, 7)])))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await StockAsync(c, a)).Should().Be(6);
        (await StockInStoreAsync(destination, a)).Should().Be(4);
    }

    [Fact]
    public async Task Transfers_need_a_different_store_of_the_tenant()
    {
        var c = await StockerAsync("stockops.invalid");
        var a = await ProductWithStockAsync(c, 2);
        var me = await CurrentStoreAsync(c);

        (await c.PostAsJsonAsync("/api/stock/transfers",
                new CreateStockTransferRequest(me, [new StockTransferItemInput(a, 1)])))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await c.PostAsJsonAsync("/api/stock/transfers",
                new CreateStockTransferRequest(Guid.NewGuid(), [new StockTransferItemInput(a, 1)])))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Concurrent_transfers_never_take_more_than_the_origin_holds()
    {
        var c = await StockerAsync("stockops.race");
        var destination = await OtherStoreAsync(c);
        var a = await ProductWithStockAsync(c, 5);
        var second = _factory.CreateApiClient();
        second.DefaultRequestHeaders.Authorization = c.DefaultRequestHeaders.Authorization;
        var body = new CreateStockTransferRequest(destination, [new StockTransferItemInput(a, 3)]);

        var responses = await Task.WhenAll(
            c.PostAsJsonAsync("/api/stock/transfers", body),
            second.PostAsJsonAsync("/api/stock/transfers", body));

        responses.Select(r => r.StatusCode).Should().BeEquivalentTo(
            [HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity]);
        (await StockAsync(c, a)).Should().Be(2);
        (await StockInStoreAsync(destination, a)).Should().Be(3);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<T> OkAsync<T>(HttpResponseMessage resp)
    {
        resp.IsSuccessStatusCode.Should().BeTrue(await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<Guid> ProductWithStockAsync(HttpClient c, decimal quantity)
    {
        var code = $"PR46-{Guid.NewGuid():N}"[..20];
        var product = (await (await c.PostAsJsonAsync("/api/products",
                new CreateProductRequest(Code: code, Name: code, Unit: "Un", SalePrice: 10m, CostPrice: 5m, TrackStock: true)))
            .Content.ReadFromJsonAsync<ProductDto>())!;
        (await c.PostAsJsonAsync("/api/stock/adjust",
            new AdjustStockRequest(product.Id, quantity, "ManualEntry", "PR46"))).EnsureSuccessStatusCode();
        return product.Id;
    }

    private static async Task<decimal> StockAsync(HttpClient c, Guid productId)
        => (await c.GetFromJsonAsync<StockItemDto>($"/api/stock/product/{productId}"))!.CurrentQuantity;

    private async Task<decimal> StockInStoreAsync(Guid storeId, Guid productId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return (await db.StockItems.IgnoreQueryFilters()
            .SingleAsync(s => s.StoreId == storeId && s.ProductId == productId)).CurrentQuantity;
    }

    private async Task<List<string>> MovementTypesAsync(Guid transferId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.StockMovements.IgnoreQueryFilters()
            .Where(m => m.ReferenceType == "StockTransfer" && m.ReferenceId == transferId)
            .Select(m => m.MovementType.ToString()).ToListAsync();
    }

    private static async Task<Guid> CurrentStoreAsync(HttpClient c)
    {
        var token = c.DefaultRequestHeaders.Authorization!.Parameter!;
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var doc = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(payload));
        return Guid.Parse(doc.RootElement.GetProperty("storeId").GetString()!);
    }

    /// <summary>A store of the tenant other than the token's current one.</summary>
    private async Task<Guid> OtherStoreAsync(HttpClient c)
    {
        var current = await CurrentStoreAsync(c);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var admin = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Login == TestCredentials.AdminLogin);
        return await db.Stores.IgnoreQueryFilters()
            .Where(s => s.TenantId == admin.TenantId && s.Id != current)
            .Select(s => s.Id).FirstAsync();
    }

    private async Task<Guid> EnsureSecondStoreAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var admin = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Login == TestCredentials.AdminLogin);
        var existing = await db.Stores.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == admin.TenantId && s.Slug == SecondStoreSlug);
        if (existing is not null) return existing.Id;
        var store = Store.Create(admin.TenantId, "Z-Filial-PR46", SecondStoreSlug);
        db.Stores.Add(store);
        await db.SaveChangesAsync();
        return store.Id;
    }

    /// <summary>Fresh Estoquista, logged in AFTER the second store exists (its token lists both).</summary>
    private async Task<HttpClient> StockerAsync(string login)
    {
        await EnsureSecondStoreAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            if (!await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Login == login))
            {
                var admin = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Login == TestCredentials.AdminLogin);
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                db.Users.Add(User.Create(
                    tenantId: admin.TenantId, fullName: $"Estoque {login}",
                    email: $"{login}@{TestCredentials.TestLocalDomain}", login: login,
                    passwordHash: hasher.Hash(TestCredentials.SameTenantManagerPassword), role: UserRole.Estoquista));
                await db.SaveChangesAsync();
            }
        }
        return await AuthClientFactory.LoginAsync(_factory, login, TestCredentials.SameTenantManagerPassword);
    }
}
