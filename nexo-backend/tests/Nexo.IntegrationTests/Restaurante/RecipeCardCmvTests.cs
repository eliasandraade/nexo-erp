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

/// <summary>
/// Integration tests for CMV calculation with gas and labor costs.
///   1. CMV breakdown includes ingredient cost, gas cost (per minute), and labor cost (per minute)
///   2. GET /api/products?isIngredient filter returns correct subset
///
/// Each test uses a unique suffix (6-char Guid fragment) for product codes to avoid cross-test state.
/// </summary>
[Collection("Integration")]
public class RecipeCardCmvTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RecipeCardCmvTests(TestWebApplicationFactory factory)
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

    // ═══════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════

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
                tenantId:             tenant.Id,
                moduleKey:            moduleKey,
                stripeSubscriptionId: $"sub_test_{moduleKey}",
                stripePriceId:        $"price_test_{moduleKey}",
                planType:             PlanType.Lifetime,
                periodStart:          DateTime.UtcNow,
                periodEnd:            null);

            db.ModuleSubscriptions.Add(sub);
            await db.SaveChangesAsync();
        }
    }

    private async Task<ProductDto> CreateProductAsync(
        string code, string name, string unit,
        decimal salePrice, decimal costPrice,
        bool isIngredient, bool trackStock = false)
    {
        var r = await _client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(
                Code:        code,
                Name:        name,
                Unit:        unit,
                SalePrice:   salePrice,
                CostPrice:   costPrice,
                TrackStock:  trackStock,
                IsIngredient: isIngredient));
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    // ═══════════════════════════════════════════════════════════
    // TEST 1: CMV with Gas and Labor Cost
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that the CMV breakdown correctly incorporates:
    ///   - Ingredient cost = (quantity / yield) × costPrice = 2kg × 10 / 1 = 20.00
    ///   - Gas cost        = totalPrepTimeMin × costPerMinuteGas = 10 × 0.10 = 1.00
    ///   - Labor cost      = totalPrepTimeMin × costPerMinuteLaborRate = 10 × 0.20 = 2.00
    ///   - Calculated cost = 20 + 1 + 2 = 23.00
    ///   - CMV %           = 23 / 40 × 100 = 57.50%
    /// </summary>
    [Fact]
    public async Task CMV_IncludesGasAndLaborCost_WhenSettingsConfigured()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];

        // Arrange — create ingredient product
        var ingredient = await CreateProductAsync(
            code:         $"ING-{suffix}",
            name:         $"Ingredient {suffix}",
            unit:         "Kg",
            salePrice:    0m,
            costPrice:    10m,
            isIngredient: true,
            trackStock:   false);

        // Arrange — create menu item product
        var menuItem = await CreateProductAsync(
            code:         $"PRATO-{suffix}",
            name:         $"Prato {suffix}",
            unit:         "Un",
            salePrice:    40m,
            costPrice:    0m,
            isIngredient: false,
            trackStock:   false);

        // Arrange — configure operational costs: gas=0.10/min, labor=0.20/min
        var costsResp = await _client.PutAsJsonAsync("/api/restaurante/settings/costs",
            new UpdateOperationalCostsRequest(
                CostPerMinuteGas:       0.10m,
                CostPerMinuteLaborRate: 0.20m));
        costsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Arrange — create recipe card (yield=1, yieldUnit="porção", hasPrep=true)
        var createCardResp = await _client.PostAsJsonAsync("/api/restaurante/recipe-cards",
            new CreateRecipeCardRequest(
                ProductId: menuItem.Id,
                Yield:     1m,
                YieldUnit: "porção",
                HasPrep:   true));
        createCardResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = (await createCardResp.Content.ReadFromJsonAsync<RecipeCardDto>())!;

        // Arrange — add ingredient (2 Kg)
        var addIngResp = await _client.PostAsJsonAsync(
            $"/api/restaurante/recipe-cards/{card.Id}/ingredients",
            new AddIngredientRequest(
                IngredientProductId: ingredient.Id,
                Quantity:            2m,
                Unit:                "Kg"));
        addIngResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Arrange — update recipe card with 1 prep step of 10 minutes
        var updateResp = await _client.PutAsJsonAsync(
            $"/api/restaurante/recipe-cards/{card.Id}",
            new UpdateRecipeCardRequest(
                Yield:              1m,
                YieldUnit:          "porção",
                HasPrep:            true,
                PrepSteps:          new List<PrepStepDto>
                {
                    new PrepStepDto(Order: 1, Description: "Cozinhar", DurationMinutes: 10)
                },
                AssemblyNotes:      null,
                RequiresPackaging:  false,
                PackagingProductId: null));
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await updateResp.Content.ReadFromJsonAsync<RecipeCardDto>())!;

        // Assert
        // ingredientCost = 2kg * 10 / 1 yield = 20.0
        updated.IngredientCost.Should().Be(20.0000m,
            "2 Kg × costPrice 10 / yield 1 = 20");

        // gasCost = 10min × 0.10/min = 1.0
        updated.GasCost.Should().Be(1.0000m,
            "10 minutes × 0.10/min = 1");

        // laborCost = 10min × 0.20/min = 2.0
        updated.LaborCost.Should().Be(2.0000m,
            "10 minutes × 0.20/min = 2");

        // calculatedCost = 20 + 1 + 2 = 23.0
        updated.CalculatedCost.Should().Be(23.0000m,
            "ingredientCost 20 + gasCost 1 + laborCost 2 = 23");

        // cmvPercent = 23 / 40 * 100 = 57.5%
        updated.CmvPercent.Should().Be(57.50m,
            "23 / salePrice 40 × 100 = 57.5%");

        // totalPrepTimeMin = sum of step durations = 10
        updated.TotalPrepTimeMin.Should().Be(10,
            "one prep step of 10 minutes → total = 10");
    }

    // ═══════════════════════════════════════════════════════════
    // TEST 2: GET /api/products?isIngredient filter
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that GET /api/products?isIngredient=true returns only ingredients
    /// and GET /api/products?isIngredient=false returns only non-ingredient products.
    /// </summary>
    [Fact]
    public async Task GetProducts_FilterByIsIngredient_ReturnsCorrectSubset()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];

        // Create one ingredient
        var ingredient = await CreateProductAsync(
            code:         $"ING2-{suffix}",
            name:         $"Insumo {suffix}",
            unit:         "Kg",
            salePrice:    0m,
            costPrice:    5m,
            isIngredient: true,
            trackStock:   false);

        // Create one menu item (non-ingredient)
        var menuItem = await CreateProductAsync(
            code:         $"CARD2-{suffix}",
            name:         $"Cardapio {suffix}",
            unit:         "Un",
            salePrice:    20m,
            costPrice:    0m,
            isIngredient: false,
            trackStock:   false);

        // Act — filter ingredients only
        var ingredientsResp = await _client.GetFromJsonAsync<List<ProductDto>>(
            "/api/products?isIngredient=true");

        // Assert — all returned products are ingredients
        ingredientsResp.Should().NotBeNullOrEmpty();
        ingredientsResp!.Should().AllSatisfy(p =>
            p.IsIngredient.Should().BeTrue(
                "GET /api/products?isIngredient=true must return only ingredients"));

        // The ingredient we created must appear
        ingredientsResp.Should().Contain(p => p.Id == ingredient.Id,
            "the newly created ingredient must be in the filtered list");

        // The menu item must NOT appear in the ingredient list
        ingredientsResp.Should().NotContain(p => p.Id == menuItem.Id,
            "a non-ingredient product must not appear when filtering isIngredient=true");

        // Act — filter non-ingredients only
        var menuItemsResp = await _client.GetFromJsonAsync<List<ProductDto>>(
            "/api/products?isIngredient=false");

        // Assert — all returned products are non-ingredients
        menuItemsResp.Should().NotBeNullOrEmpty();
        menuItemsResp!.Should().AllSatisfy(p =>
            p.IsIngredient.Should().BeFalse(
                "GET /api/products?isIngredient=false must return only non-ingredient products"));

        // The menu item we created must appear
        menuItemsResp.Should().Contain(p => p.Id == menuItem.Id,
            "the newly created menu item must be in the filtered list");

        // The ingredient must NOT appear in the non-ingredient list
        menuItemsResp.Should().NotContain(p => p.Id == ingredient.Id,
            "an ingredient must not appear when filtering isIngredient=false");
    }
}
