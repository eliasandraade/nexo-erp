using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Features.Auth;
using Nexo.Application.Features.Products;
using Nexo.Application.Modules.Restaurante;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Restaurante;

[Collection("Integration")]
public class FinanceiroReportTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public FinanceiroReportTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateApiClient();
    }

    public async Task InitializeAsync()
    {
        await AuthenticateAsync(_client);
        await EnsureModuleSubscriptionAsync("restaurante");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task AuthenticateAsync(HttpClient client)
    {
        var r = await client.PostAsJsonAsync("/api/auth/login",
            new { login = "admin", password = "nexo@2026" });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await r.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private async Task EnsureModuleSubscriptionAsync(string moduleKey)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var tenant = await db.Tenants.FirstOrDefaultAsync();
        if (tenant is null) return;
        var exists = await db.ModuleSubscriptions
            .AnyAsync(s => s.TenantId == tenant.Id && s.ModuleKey == moduleKey);
        if (!exists)
        {
            var sub = ModuleSubscription.CreateFromStripe(
                tenantId: tenant.Id, moduleKey: moduleKey,
                stripeSubscriptionId: $"sub_test_{moduleKey}",
                stripePriceId: $"price_test_{moduleKey}",
                planType: PlanType.Lifetime,
                periodStart: DateTime.UtcNow, periodEnd: null);
            db.ModuleSubscriptions.Add(sub);
            await db.SaveChangesAsync();
        }
    }

    private async Task<ProductDto> CreateProductAsync(
        string code, string name, decimal salePrice, decimal costPrice, bool isIngredient)
    {
        var r = await _client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(
                Code: code, Name: name, Unit: "un",
                SalePrice: salePrice, CostPrice: costPrice,
                TrackStock: false, IsIngredient: isIngredient));
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    [Fact]
    public async Task CmvReport_ReturnsItemWithCorrectCmvMetrics()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var ing   = await CreateProductAsync($"ING-F2-{suffix}", $"Ing F2 {suffix}", 0m, 8m, true);
        var prato = await CreateProductAsync($"PRT-F2-{suffix}", $"Prato F2 {suffix}", 30m, 0m, false);

        var cardResp = await _client.PostAsJsonAsync("/api/restaurante/recipe-cards",
            new CreateRecipeCardRequest(ProductId: prato.Id, Yield: 1m, YieldUnit: "un"));
        cardResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = (await cardResp.Content.ReadFromJsonAsync<RecipeCardDto>())!;

        var addIng = await _client.PostAsJsonAsync(
            $"/api/restaurante/recipe-cards/{card.Id}/ingredients",
            new AddIngredientRequest(
                IngredientProductId: ing.Id, Quantity: 2m, Unit: "un"));
        addIng.StatusCode.Should().Be(HttpStatusCode.OK);

        var resp = await _client.GetFromJsonAsync<CmvReportDto>(
            "/api/restaurante/financeiro/cmv-report");

        resp.Should().NotBeNull();
        var item = resp!.Items.FirstOrDefault(i => i.ProductId == prato.Id);
        item.Should().NotBeNull("the dish must appear in the CMV report");
        item!.SalePrice.Should().Be(30m);
        item.UnitIngredientCost.Should().Be(16m, "2 × 8 / yield 1 = 16");
        item.UnitCost.Should().Be(16m, "no prep → total = 16");
        item.CmvPercent.Should().BeApproximately(53.33m, 0.01m, "16/30×100≈53.33");
        item.Margin.Should().BeApproximately(14m, 0.01m, "30-16=14");
    }

    [Fact]
    public async Task FinanceiroSummary_ReturnsZeroRevenue_WhenNoPaidOrdersInPeriod()
    {
        var resp = await _client.GetFromJsonAsync<FinanceiroSummaryDto>(
            "/api/restaurante/financeiro/summary?from=2099-01-01&to=2099-01-31");

        resp.Should().NotBeNull();
        resp!.Revenue.Should().Be(0m);
        resp.TotalCostOfGoodsSold.Should().Be(0m);
        resp.OrdersCount.Should().Be(0);
        resp.WeightedCmvPercent.Should().Be(0m);
        resp.GrossMargin.Should().Be(0m);
    }
}

public record CmvReportItemDto(
    Guid    ProductId,
    string  ProductName,
    string  ProductCode,
    decimal SalePrice,
    decimal UnitIngredientCost,
    decimal GasCost,
    decimal LaborCost,
    decimal UnitCost,
    decimal CmvPercent,
    decimal Margin,
    decimal MarginPercent);

public record CmvReportDto(
    IReadOnlyList<CmvReportItemDto> Items,
    string From,
    string To);

public record FinanceiroSummaryDto(
    int     OrdersCount,
    decimal Revenue,
    decimal TotalCostOfGoodsSold,
    decimal WeightedCmvPercent,
    decimal GrossMargin,
    string  From,
    string  To);
