using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
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
    // "Z-" sorts last: the store a user lands in at login is the alphabetically first one, and other
    // tests rely on the admin's original store staying the default.
    private const string SecondStoreSlug = "z-stock-ops-destination";
    private readonly TestWebApplicationFactory _factory;
    public StockOperationsTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Applying_a_count_keeps_sales_made_after_the_count_was_typed()
    {
        var (c, _) = await StockerAsync("stockops.count");
        await CancelOpenCountsAsync(c);
        var (a, _) = await ProductWithStockAsync(c, 10);
        var (b, _) = await ProductWithStockAsync(c, 5);

        var count = await OkAsync<InventoryCountDto>(await c.PostAsJsonAsync("/api/stock/counts",
            new CreateInventoryCountRequest([a, b], "contagem mensal")));
        count.Items.Single(i => i.ProductId == a).ExpectedQuantity.Should().Be(10);

        // shelf counted 8 while the system said 10 → 2 missing
        var saved = await OkAsync<InventoryCountDto>(await c.PutAsJsonAsync($"/api/stock/counts/{count.Id}",
            new UpdateInventoryCountRequest([new InventoryCountEntry(a, 8)])));
        saved.Items.Single(i => i.ProductId == a).SystemQuantityAtCount.Should().Be(10);

        // then one unit is sold before applying: shelf 7, system 9 — the sale must survive the count
        (await c.PostAsJsonAsync("/api/stock/adjust", new AdjustStockRequest(a, 1, "ManualExit", "venda depois da contagem")))
            .EnsureSuccessStatusCode();

        var applied = await OkAsync<InventoryCountDto>(await c.PostAsync($"/api/stock/counts/{count.Id}/apply", null));

        applied.Status.Should().Be("Applied");
        var itemA = applied.Items.Single(i => i.ProductId == a);
        itemA.SystemQuantityAtApply.Should().Be(9);
        itemA.Difference.Should().Be(-2);
        applied.Items.Single(i => i.ProductId == b).Difference.Should().BeNull();   // not counted → untouched
        (await StockAsync(c, a)).Should().Be(7);
        (await StockAsync(c, b)).Should().Be(5);

        (await c.PutAsJsonAsync($"/api/stock/counts/{count.Id}", new UpdateInventoryCountRequest([new InventoryCountEntry(a, 1)])))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await c.PostAsync($"/api/stock/counts/{count.Id}/apply", null))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Only_one_count_is_open_per_store_and_an_empty_count_can_only_be_cancelled()
    {
        var (c, _) = await StockerAsync("stockops.cancel");
        var (a, _) = await ProductWithStockAsync(c, 3);
        await CancelOpenCountsAsync(c);
        var count = await OkAsync<InventoryCountDto>(await c.PostAsJsonAsync("/api/stock/counts",
            new CreateInventoryCountRequest([a])));

        (await c.PostAsJsonAsync("/api/stock/counts", new CreateInventoryCountRequest([a])))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await c.PostAsync($"/api/stock/counts/{count.Id}/apply", null))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await OkAsync<InventoryCountDto>(await c.PostAsync($"/api/stock/counts/{count.Id}/cancel", null)))
            .Status.Should().Be("Cancelled");
        (await StockAsync(c, a)).Should().Be(3);
    }

    [Fact]
    public async Task A_transfer_lands_on_the_destination_store_product_with_the_same_code()
    {
        var (c, login) = await StockerAsync("stockops.transfer");
        var destination = await OtherStoreAsync(c);
        var (a, code) = await ProductWithStockAsync(c, 10);
        var counterpart = await CounterpartInStoreAsync(login, destination, code);

        var transfer = await OkAsync<StockTransferDto>(await c.PostAsJsonAsync("/api/stock/transfers",
            new CreateStockTransferRequest(destination, [new StockTransferItemInput(a, 4)], "reposição filial")));

        transfer.Direction.Should().Be("out");
        transfer.Items.Single().ProductId.Should().Be(a);
        (await StockAsync(c, a)).Should().Be(6);
        (await StockInStoreAsync(destination, counterpart)).Should().Be(4);
        (await MovementTypesAsync(transfer.Id)).Should().BeEquivalentTo(["TransferOut", "TransferIn"]);

        // the destination sees it — on its own product — in its history and stock
        var there = await ClientInStoreAsync(login, destination);
        var incoming = (await there.GetFromJsonAsync<List<StockTransferDto>>("/api/stock/transfers"))!
            .Single(t => t.Id == transfer.Id);
        incoming.Direction.Should().Be("in");
        incoming.Items.Single().ProductId.Should().Be(counterpart);
        (await StockAsync(there, counterpart)).Should().Be(4);

        // not enough at the origin → nothing moves
        (await c.PostAsJsonAsync("/api/stock/transfers",
                new CreateStockTransferRequest(destination, [new StockTransferItemInput(a, 7)])))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await StockAsync(c, a)).Should().Be(6);
    }

    [Fact]
    public async Task A_transfer_needs_the_product_in_the_destination_catalogue_and_a_valid_store()
    {
        var (c, _) = await StockerAsync("stockops.invalid");
        var (a, _) = await ProductWithStockAsync(c, 2);
        var me = await CurrentStoreAsync(c);
        var other = await OtherStoreAsync(c);

        var noCounterpart = await c.PostAsJsonAsync("/api/stock/transfers",
            new CreateStockTransferRequest(other, [new StockTransferItemInput(a, 1)]));
        noCounterpart.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await StockAsync(c, a)).Should().Be(2);

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
        var (c, login) = await StockerAsync("stockops.race");
        var destination = await OtherStoreAsync(c);
        var (a, code) = await ProductWithStockAsync(c, 5);
        var counterpart = await CounterpartInStoreAsync(login, destination, code);
        var second = _factory.CreateApiClient();
        second.DefaultRequestHeaders.Authorization = c.DefaultRequestHeaders.Authorization;
        var body = new CreateStockTransferRequest(destination, [new StockTransferItemInput(a, 3)]);

        var responses = await Task.WhenAll(
            c.PostAsJsonAsync("/api/stock/transfers", body),
            second.PostAsJsonAsync("/api/stock/transfers", body));

        responses.Select(r => r.StatusCode).Should().BeEquivalentTo(
            [HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity]);
        (await StockAsync(c, a)).Should().Be(2);
        (await StockInStoreAsync(destination, counterpart)).Should().Be(3);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<T> OkAsync<T>(HttpResponseMessage resp)
    {
        resp.IsSuccessStatusCode.Should().BeTrue(await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<(Guid Id, string Code)> ProductWithStockAsync(HttpClient c, decimal quantity, string? code = null)
    {
        code ??= $"PR46-{Guid.NewGuid():N}"[..20];
        var resp = await c.PostAsJsonAsync("/api/products",
            new CreateProductRequest(Code: code, Name: code, Unit: "Un", SalePrice: 10m, CostPrice: 5m, TrackStock: true));
        var product = await OkAsync<ProductDto>(resp);
        if (quantity > 0)
            (await c.PostAsJsonAsync("/api/stock/adjust",
                new AdjustStockRequest(product.Id, quantity, "ManualEntry", "PR46"))).EnsureSuccessStatusCode();
        return (product.Id, code);
    }

    /// <summary>Registers the same product (same code) in another store's catalogue.</summary>
    private async Task<Guid> CounterpartInStoreAsync(string login, Guid storeId, string code)
    {
        var there = await ClientInStoreAsync(login, storeId);
        return (await ProductWithStockAsync(there, 0, code)).Id;
    }

    private async Task<HttpClient> ClientInStoreAsync(string login, Guid storeId)
    {
        var (client, body) = await AuthClientFactory.LoginWithBodyAsync(_factory, login, TestCredentials.SameTenantManagerPassword);
        var resp = await client.PostAsJsonAsync("/api/auth/switch-store",
            new { storeId = storeId.ToString(), refreshToken = body.RefreshToken });
        resp.EnsureSuccessStatusCode();
        var switched = (await resp.Content.ReadFromJsonAsync<SwitchStoreResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", switched.AccessToken);
        return client;
    }

    private static async Task CancelOpenCountsAsync(HttpClient c)
    {
        var counts = (await c.GetFromJsonAsync<List<InventoryCountDto>>("/api/stock/counts"))!;
        foreach (var open in counts.Where(x => x.Status == "Open"))
            (await c.PostAsync($"/api/stock/counts/{open.Id}/cancel", null)).EnsureSuccessStatusCode();
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
            .Where(s => s.TenantId == admin.TenantId && s.Id != current && s.Status == StoreStatus.Active)
            .OrderBy(s => s.Name)
            .Select(s => s.Id).FirstAsync();
    }

    private async Task EnsureSecondStoreAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var admin = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Login == TestCredentials.AdminLogin);
        if (await db.Stores.IgnoreQueryFilters().AnyAsync(s => s.TenantId == admin.TenantId && s.Slug == SecondStoreSlug))
            return;
        db.Stores.Add(Store.Create(admin.TenantId, "Z-Filial-PR46", SecondStoreSlug));
        await db.SaveChangesAsync();
    }

    /// <summary>Fresh Estoquista, logged in AFTER the second store exists (its token lists both).</summary>
    private async Task<(HttpClient Client, string Login)> StockerAsync(string login)
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
        return (await AuthClientFactory.LoginAsync(_factory, login, TestCredentials.SameTenantManagerPassword), login);
    }
}
