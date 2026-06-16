using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Features.Auth;
using Nexo.Application.Features.Cash;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Sales;
using Nexo.Application.Features.Stock;
using Nexo.Application.Modules.Restaurante;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Restaurante;

/// <summary>
/// Integration tests for the Restaurante module covering:
///   1. Mesa e comanda: table state transitions driven by order lifecycle
///   2. Integração com CORE: Sale creation and confirmation via order flow
///   3. Ficha técnica / estoque: ingredient deduction, yield calculation, CostPriceSnapshot
///   4. Concorrência e integridade: duplicate open-order prevention via SELECT FOR UPDATE + partial index
///
/// Each test is isolated — unique product codes and table numbers prevent cross-test state.
/// The PostgreSQL container and DB are shared within the class (IClassFixture).
/// InitializeAsync runs before each test: authenticates, ensures module subscription, opens cash session.
/// </summary>
[Collection("Integration")]
public class RestauranteFlowTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    // Thread-safe counter for unique table numbers across tests
    private static int _tableSeq = 0;

    public RestauranteFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateApiClient();
    }

    public async Task InitializeAsync()
    {
        await AuthenticateAsync(_client);
        await EnsureModuleSubscriptionAsync("restaurante");
        await OpenCashSessionAsync(_client);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ═══════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════

    private async Task AuthenticateAsync(HttpClient client)
    {
        var r = await client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await r.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    /// <summary>
    /// Seeds a Lifetime/Active ModuleSubscription for the default test tenant.
    /// Idempotent: no-op if the subscription already exists.
    /// </summary>
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
            // CreateFromStripe: no GrantedById FK needed, Status = Active, periodEnd = null → never expires
            var sub = ModuleSubscription.CreateFromStripe(
                tenantId:             tenant.Id,
                moduleKey:            moduleKey,
                stripeSubscriptionId: $"sub_test_{moduleKey}",
                stripePriceId:        $"price_test_{moduleKey}",
                planType:             PlanType.Lifetime,
                periodStart:          DateTime.UtcNow,
                periodEnd:            null);   // null = lifetime

            db.ModuleSubscriptions.Add(sub);
            await db.SaveChangesAsync();
        }
    }

    private static async Task OpenCashSessionAsync(HttpClient client)
    {
        var r = await client.PostAsJsonAsync("/api/cash/sessions/open",
            new OpenCashSessionRequest(OpeningBalance: 0, Notes: "Restaurante test session"));
        if (r.StatusCode == HttpStatusCode.Conflict) return;   // already open
        r.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<AreaDto> CreateAreaAsync(string name)
    {
        var r = await _client.PostAsJsonAsync("/api/restaurante/areas",
            new CreateAreaRequest(name));
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<AreaDto>())!;
    }

    private async Task<TableDto> CreateTableAsync(Guid areaId)
    {
        var num = $"T{Interlocked.Increment(ref _tableSeq):D3}";
        var r = await _client.PostAsJsonAsync("/api/restaurante/tables",
            new CreateTableRequest(areaId, num, Capacity: 4));
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<TableDto>())!;
    }

    private async Task<ProductDto> CreateProductWithStockAsync(
        string code, decimal salePrice, decimal costPrice, decimal initialStock,
        bool isIngredient = false)
    {
        var pr = await _client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(
                Code:         code,
                Name:         $"Produto {code}",
                Unit:         "Un",
                SalePrice:    salePrice,
                CostPrice:    costPrice,
                TrackStock:   true,
                IsIngredient: isIngredient));
        pr.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = (await pr.Content.ReadFromJsonAsync<ProductDto>())!;

        if (initialStock > 0)
        {
            await _client.PostAsJsonAsync("/api/stock/adjust",
                new AdjustStockRequest(
                    ProductId:    product.Id,
                    Quantity:     initialStock,
                    MovementType: "ManualEntry",
                    Notes:        "Initial stock"));
        }
        return product;
    }

    /// <summary>
    /// Creates a recipe card for <paramref name="productId"/> and adds one ingredient.
    /// Returns the final RecipeCardDto (after ingredient was added).
    /// </summary>
    private async Task<RecipeCardDto> CreateRecipeCardAsync(
        Guid productId, decimal yield, Guid ingredientProductId, decimal ingredientQty)
    {
        var cr = await _client.PostAsJsonAsync("/api/restaurante/recipe-cards",
            new CreateRecipeCardRequest(productId, yield, YieldUnit: "Un"));
        cr.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = (await cr.Content.ReadFromJsonAsync<RecipeCardDto>())!;

        var addIng = await _client.PostAsJsonAsync(
            $"/api/restaurante/recipe-cards/{card.Id}/ingredients",
            new AddIngredientRequest(ingredientProductId, ingredientQty, Unit: "g"));
        addIng.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await addIng.Content.ReadFromJsonAsync<RecipeCardDto>())!;
    }

    private async Task<OrderDto> OpenOrderAsync(Guid tableId, string? notes = null)
    {
        var r = await _client.PostAsJsonAsync("/api/restaurante/orders",
            new OpenOrderRequest("DineIn", TableId: tableId, Notes: notes));
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<OrderDto>())!;
    }

    private async Task<OrderDto> AddItemAsync(Guid orderId, Guid productId, decimal qty = 1)
    {
        var r = await _client.PostAsJsonAsync($"/api/restaurante/orders/{orderId}/items",
            new AddOrderItemRequest(productId, qty));
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await r.Content.ReadFromJsonAsync<OrderDto>())!;
    }

    private async Task<CloseOrderResponse> CloseOrderAsync(Guid orderId)
    {
        var r = await _client.PostAsync($"/api/restaurante/orders/{orderId}/close", null);
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await r.Content.ReadFromJsonAsync<CloseOrderResponse>())!;
    }

    private async Task<OrderDto> PayOrderAsync(Guid orderId, decimal amount)
    {
        var r = await _client.PostAsJsonAsync($"/api/restaurante/orders/{orderId}/pay",
            new PayOrderRequest(
                new List<PaymentInputDto> { new("Cash", "Cash", amount) }));
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await r.Content.ReadFromJsonAsync<OrderDto>())!;
    }

    private async Task<TableDto> GetTableAsync(Guid tableId)
        => (await _client.GetFromJsonAsync<TableDto>($"/api/restaurante/tables/{tableId}"))!;

    private async Task<StockItemDto> GetStockAsync(Guid productId)
        => (await _client.GetFromJsonAsync<StockItemDto>($"/api/stock/product/{productId}"))!;

    private async Task<SaleDto> GetSaleAsync(Guid saleId)
        => (await _client.GetFromJsonAsync<SaleDto>($"/api/sales/{saleId}"))!;

    // ═══════════════════════════════════════════════════════════
    // 1. MESA E COMANDA
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Opening an order must transition the table from Available → Occupied.
    /// </summary>
    [Fact]
    public async Task OpenOrder_OccupiesTable()
    {
        var area  = await CreateAreaAsync("Área Salão");
        var table = await CreateTableAsync(area.Id);

        table.Status.Should().Be("Available");

        await OpenOrderAsync(table.Id);

        (await GetTableAsync(table.Id)).Status.Should().Be("Occupied");
    }

    /// <summary>
    /// A second open-order request on the same table must be rejected
    /// while the first order is still active. Validates both the application-level
    /// guard and the DB partial unique index.
    /// </summary>
    [Fact]
    public async Task SecondOpenOrder_OnOccupiedTable_ReturnsConflict()
    {
        var area  = await CreateAreaAsync("Área Dupla");
        var table = await CreateTableAsync(area.Id);

        await OpenOrderAsync(table.Id);  // first — OK

        var r = await _client.PostAsJsonAsync("/api/restaurante/orders",
            new OpenOrderRequest("DineIn", TableId: table.Id));

        r.StatusCode.Should().BeOneOf(
            HttpStatusCode.Conflict,
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// Cancelling an open order must release the table back to Available.
    /// </summary>
    [Fact]
    public async Task CancelOrder_ReleasesTable()
    {
        var area  = await CreateAreaAsync("Área Cancel");
        var table = await CreateTableAsync(area.Id);
        var order = await OpenOrderAsync(table.Id);

        (await GetTableAsync(table.Id)).Status.Should().Be("Occupied");

        var r = await _client.PostAsync($"/api/restaurante/orders/{order.Id}/cancel", null);
        r.StatusCode.Should().Be(HttpStatusCode.OK);

        (await GetTableAsync(table.Id)).Status.Should().Be("Available");
    }

    /// <summary>
    /// Closing an order (step 1 of checkout) must NOT release the table.
    /// The table stays Occupied until payment is confirmed.
    /// </summary>
    [Fact]
    public async Task CloseOrder_TableRemainsOccupied()
    {
        var area    = await CreateAreaAsync("Área Close");
        var product = await CreateProductWithStockAsync("REST-CLZ-01", 50m, 20m, 10m);
        var table   = await CreateTableAsync(area.Id);
        var order   = await OpenOrderAsync(table.Id);

        await AddItemAsync(order.Id, product.Id);
        await CloseOrderAsync(order.Id);

        (await GetTableAsync(table.Id)).Status.Should().Be("Occupied");
    }

    /// <summary>
    /// Paying a closed order must release the table to Available
    /// and transition the order to Paid status.
    /// </summary>
    [Fact]
    public async Task PayOrder_ReleasesTable()
    {
        var area    = await CreateAreaAsync("Área Pay");
        var product = await CreateProductWithStockAsync("REST-PAY-01", 50m, 20m, 10m);
        var table   = await CreateTableAsync(area.Id);
        var order   = await OpenOrderAsync(table.Id);

        await AddItemAsync(order.Id, product.Id, qty: 2);   // total = 100m
        await CloseOrderAsync(order.Id);
        var paid = await PayOrderAsync(order.Id, amount: 100m);

        paid.Status.Should().Be("Paid");
        (await GetTableAsync(table.Id)).Status.Should().Be("Available");
    }

    // ═══════════════════════════════════════════════════════════
    // 2. INTEGRAÇÃO COM CORE
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// CloseAsync must create a Sale in Draft status in the CORE module.
    /// The total in the response must match the sum of active order items.
    /// </summary>
    [Fact]
    public async Task CloseOrder_GeneratesDraftSale()
    {
        var area    = await CreateAreaAsync("Área Draft");
        var product = await CreateProductWithStockAsync("REST-SL-01", 80m, 30m, 10m);
        var table   = await CreateTableAsync(area.Id);
        var order   = await OpenOrderAsync(table.Id);

        await AddItemAsync(order.Id, product.Id, qty: 1);
        var closeResp = await CloseOrderAsync(order.Id);

        closeResp.SaleId.Should().NotBe(Guid.Empty);
        closeResp.Total.Should().Be(80m);

        var sale = await GetSaleAsync(closeResp.SaleId);
        sale.Status.Should().Be("Draft");
    }

    /// <summary>
    /// PayAsync must confirm the linked Sale. For a full cash payment the status
    /// must be Paid (no credit items).
    /// </summary>
    [Fact]
    public async Task PayOrder_ConfirmsSale_StatusIsPaid()
    {
        var area    = await CreateAreaAsync("Área Confirm");
        var product = await CreateProductWithStockAsync("REST-CF-01", 60m, 25m, 10m);
        var table   = await CreateTableAsync(area.Id);
        var order   = await OpenOrderAsync(table.Id);

        await AddItemAsync(order.Id, product.Id, qty: 1);
        var closeResp = await CloseOrderAsync(order.Id);
        await PayOrderAsync(order.Id, amount: 60m);

        var sale = await GetSaleAsync(closeResp.SaleId);
        sale.Status.Should().BeOneOf("Paid", "Confirmed");  // full cash → Paid
    }

    /// <summary>
    /// Calling /pay on an already-paid order must return 409 Conflict.
    /// After the first pay, order.Status == Paid → guard 1 fires ConflictException immediately.
    /// </summary>
    [Fact]
    public async Task PayOrderTwice_SecondCallReturnsConflict()
    {
        var area    = await CreateAreaAsync("Área Idem");
        var product = await CreateProductWithStockAsync("REST-ID-01", 40m, 15m, 10m);
        var table   = await CreateTableAsync(area.Id);
        var order   = await OpenOrderAsync(table.Id);

        await AddItemAsync(order.Id, product.Id, qty: 1);
        await CloseOrderAsync(order.Id);
        var paid = await PayOrderAsync(order.Id, amount: 40m);   // first call — OK
        paid.Status.Should().Be("Paid");

        // Second call: order.Status == Paid → guard 1 throws ConflictException → 409
        var r = await _client.PostAsJsonAsync($"/api/restaurante/orders/{order.Id}/pay",
            new PayOrderRequest(
                new List<PaymentInputDto> { new("Cash", "Cash", 40m) }));

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Cancelling a Closed order must be rejected by the domain guard.
    /// cancel() on a closed order is not allowed per the state machine.
    /// </summary>
    [Fact]
    public async Task CancelClosedOrder_ReturnsDomainError()
    {
        var area    = await CreateAreaAsync("Área CancelClosed");
        var product = await CreateProductWithStockAsync("REST-CC-01", 35m, 12m, 10m);
        var table   = await CreateTableAsync(area.Id);
        var order   = await OpenOrderAsync(table.Id);

        await AddItemAsync(order.Id, product.Id);
        await CloseOrderAsync(order.Id);   // order is now Closed

        var r = await _client.PostAsync($"/api/restaurante/orders/{order.Id}/cancel", null);

        r.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.Conflict,
            HttpStatusCode.UnprocessableEntity);
    }

    // ═══════════════════════════════════════════════════════════
    // 3. FICHA TÉCNICA / ESTOQUE
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Paying an order whose product has a recipe card must deduct ingredient stock.
    /// Scenario: yield=1, ingredient=100g per portion. Selling 2 portions → 200g consumed.
    /// </summary>
    [Fact]
    public async Task PayOrder_WithRecipe_DeductsIngredientStock()
    {
        var area       = await CreateAreaAsync("Área Deduct");
        var ingredient = await CreateProductWithStockAsync("REST-ING-01", 0m, 5m, 500m, isIngredient: true);
        var product    = await CreateProductWithStockAsync("REST-PROD-01", 30m, 10m, 0m);
        await CreateRecipeCardAsync(product.Id, yield: 1m, ingredient.Id, ingredientQty: 100m);

        var table = await CreateTableAsync(area.Id);
        var order = await OpenOrderAsync(table.Id);
        await AddItemAsync(order.Id, product.Id, qty: 2);   // 2 × 30 = 60m
        await CloseOrderAsync(order.Id);
        await PayOrderAsync(order.Id, amount: 60m);

        // 500g − (2 portions × 100g/portion ÷ yield 1) = 300g
        (await GetStockAsync(ingredient.Id)).CurrentQuantity.Should().Be(300m);
    }

    /// <summary>
    /// Validates the yield division formula: consumption = (qty_sold / yield) × ingredient_qty.
    /// Scenario: yield=2, ingredient=300g per batch → 150g per portion.
    /// Selling 4 portions → 4 × 150g = 600g consumed.
    /// </summary>
    [Fact]
    public async Task PayOrder_WithRecipe_YieldDivisionIsCorrect()
    {
        var area       = await CreateAreaAsync("Área Yield");
        var ingredient = await CreateProductWithStockAsync("REST-ING-02", 0m, 3m, 1000m, isIngredient: true);
        var product    = await CreateProductWithStockAsync("REST-PROD-02", 25m, 8m, 0m);
        await CreateRecipeCardAsync(product.Id, yield: 2m, ingredient.Id, ingredientQty: 300m);

        var table = await CreateTableAsync(area.Id);
        var order = await OpenOrderAsync(table.Id);
        await AddItemAsync(order.Id, product.Id, qty: 4);   // 4 × 25 = 100m
        await CloseOrderAsync(order.Id);
        await PayOrderAsync(order.Id, amount: 100m);

        // 1000g − (4 / 2) × 300g = 1000 − 600 = 400g
        (await GetStockAsync(ingredient.Id)).CurrentQuantity.Should().Be(400m);
    }

    /// <summary>
    /// Verifies that StockMovement.CostPriceSnapshot is populated with the ingredient's
    /// CostPrice at the time of deduction, not null.
    /// Requires direct DB access since this field is not exposed via API.
    /// </summary>
    [Fact]
    public async Task PayOrder_WithRecipe_CostPriceSnapshot_IsPopulated()
    {
        var area       = await CreateAreaAsync("Área Snapshot");
        var ingredient = await CreateProductWithStockAsync("REST-ING-03", 0m, 7.50m, 500m, isIngredient: true);
        var product    = await CreateProductWithStockAsync("REST-PROD-03", 40m, 15m, 0m);
        await CreateRecipeCardAsync(product.Id, yield: 1m, ingredient.Id, ingredientQty: 50m);

        var table = await CreateTableAsync(area.Id);
        var order = await OpenOrderAsync(table.Id);
        await AddItemAsync(order.Id, product.Id, qty: 1);
        await CloseOrderAsync(order.Id);
        await PayOrderAsync(order.Id, amount: 40m);

        // Verify via direct DB access — CostPriceSnapshot is not in the API response
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

        // IgnoreQueryFilters needed: scope has no HTTP context → CurrentTenantIdForFilter == Guid.Empty
        var movement = await db.StockMovements
            .IgnoreQueryFilters()
            .Where(m =>
                m.ProductId     == ingredient.Id &&
                m.MovementType  == StockMovementType.RecipeOutput)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync();

        movement.Should().NotBeNull("a RecipeOutput movement must have been created");
        movement!.CostPriceSnapshot.Should().Be(7.50m,
            "snapshot must capture ingredient CostPrice at deduction time");
    }

    /// <summary>
    /// If SaleService.ConfirmAsync fails (payment total mismatch), the entire
    /// PayAsync transaction must roll back:
    ///   - Table stays Occupied
    ///   - Ingredient stock is unchanged
    ///   - Sale remains in Draft status
    /// </summary>
    [Fact]
    public async Task PayOrder_PaymentMismatch_RollsBackTableAndStock()
    {
        var area       = await CreateAreaAsync("Área Rollback");
        var ingredient = await CreateProductWithStockAsync("REST-ING-04", 0m, 4m, 200m, isIngredient: true);
        var product    = await CreateProductWithStockAsync("REST-PROD-04", 20m, 8m, 0m);
        await CreateRecipeCardAsync(product.Id, yield: 1m, ingredient.Id, ingredientQty: 50m);

        var table    = await CreateTableAsync(area.Id);
        var order    = await OpenOrderAsync(table.Id);
        await AddItemAsync(order.Id, product.Id, qty: 1);   // total = 20m
        var closeResp = await CloseOrderAsync(order.Id);

        // Pay with wrong amount: 1m ≠ 20m → SaleService.ConfirmAsync throws → tx rolls back
        var r = await _client.PostAsJsonAsync($"/api/restaurante/orders/{order.Id}/pay",
            new PayOrderRequest(
                new List<PaymentInputDto> { new("Cash", "Cash", 1m) }));

        r.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.Conflict,
            HttpStatusCode.UnprocessableEntity);

        // Table must still be Occupied — tx rolled back before SetAvailable()
        (await GetTableAsync(table.Id)).Status.Should().Be("Occupied");

        // Stock must be unchanged — RecipeOutput movement never committed
        (await GetStockAsync(ingredient.Id)).CurrentQuantity.Should().Be(200m);

        // Sale must still be Draft — SaleService.ConfirmAsync never committed
        (await GetSaleAsync(closeResp.SaleId)).Status.Should().Be("Draft");
    }

    // ═══════════════════════════════════════════════════════════
    // 4. CONCORRÊNCIA E INTEGRIDADE
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Two concurrent open-order requests for the same table must result in:
    ///   - Exactly one 201 Created
    ///   - Exactly one error response (409 / 400 / 500 from DB constraint violation)
    ///   - Only one open order for the table in the database
    ///
    /// The SELECT FOR UPDATE in TableRepository.GetByIdForUpdateAsync serializes the requests.
    /// The partial unique index (tenant_id, table_id WHERE status NOT IN ('Closed','Cancelled'))
    /// acts as a second DB-level guard against any race that bypasses the application lock.
    /// </summary>
    [Fact]
    public async Task ConcurrentOrderOpen_OnSameTable_OnlyOneSucceeds()
    {
        var area  = await CreateAreaAsync("Área Concur");
        var table = await CreateTableAsync(area.Id);

        // Second client — must authenticate with the same tenant credentials
        var client2 = _factory.CreateApiClient();
        await AuthenticateAsync(client2);

        // Fire both requests simultaneously before either can complete
        var req = new OpenOrderRequest("DineIn", TableId: table.Id, Notes: "concurrent");
        var task1 = _client.PostAsJsonAsync("/api/restaurante/orders", req);
        var task2 = client2.PostAsJsonAsync("/api/restaurante/orders", req);
        var results = await Task.WhenAll(task1, task2);

        var statuses = results.Select(r => r.StatusCode).ToArray();

        // Exactly one success
        statuses.Count(s => s == HttpStatusCode.Created).Should().Be(1,
            "exactly one of the two concurrent requests must succeed");

        // The other must fail with any error status (application or DB constraint)
        statuses.Count(s => (int)s >= 400).Should().Be(1,
            "the losing request must receive an error response");

        // DB state: only one non-terminal order for this table
        var allOrders = await _client.GetFromJsonAsync<List<OrderDto>>("/api/restaurante/orders");
        var openForTable = allOrders!
            .Where(o => o.TableId == table.Id &&
                        o.Status is "Open" or "InPreparation" or "Ready" or "Closed" or "Paid")
            .ToList();

        openForTable.Should().HaveCount(1,
            "only one order must exist in a non-terminal state for the table");
    }

    /// <summary>
    /// Sequential duplicate: after the first order is opened, a second sequential
    /// request is rejected by the application guard (ConflictException) before even
    /// reaching the partial unique index. Provides a deterministic check that the
    /// guard layer works in isolation.
    /// </summary>
    [Fact]
    public async Task SequentialDuplicateOrder_IsRejectedByApplicationGuard()
    {
        var area  = await CreateAreaAsync("Área SeqIdx");
        var table = await CreateTableAsync(area.Id);

        var r1 = await _client.PostAsJsonAsync("/api/restaurante/orders",
            new OpenOrderRequest("DineIn", TableId: table.Id));
        r1.StatusCode.Should().Be(HttpStatusCode.Created);

        var r2 = await _client.PostAsJsonAsync("/api/restaurante/orders",
            new OpenOrderRequest("DineIn", TableId: table.Id));
        r2.StatusCode.Should().BeOneOf(
            HttpStatusCode.Conflict,
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnprocessableEntity);
    }

    // ═══════════════════════════════════════════════════════════
    // 5. COUVERT, TAXA DE SERVIÇO, CANCELAMENTO, ISOLAMENTO
    // ═══════════════════════════════════════════════════════════

    private async Task ResetFoodServiceSettingsAsync()
    {
        await _client.PutAsJsonAsync("/api/restaurante/settings", new UpdateFoodServiceSettingsRequest(
            StoreType:            "restaurant",
            CouvertEnabled:       false,
            CouvertPricePerPerson: null,
            CouvertAutomatic:     false,
            ServiceFeeEnabled:    false,
            ServiceFeePercent:    null,
            OrderTypesEnabled:    "DineIn,Counter,Takeaway"));
    }

    /// <summary>
    /// When both couvert and service fee are disabled, Total must equal ItemsSubtotal exactly.
    /// </summary>
    [Fact]
    public async Task PayOrder_NoCouvertNoServiceFee_TotalEqualsItemsSubtotal()
    {
        await ResetFoodServiceSettingsAsync();

        var product = await CreateProductWithStockAsync(
            $"PLAIN-{Interlocked.Increment(ref _tableSeq)}", salePrice: 50m, costPrice: 10m, initialStock: 5m);
        var area    = await CreateAreaAsync($"Área Plain {Interlocked.Increment(ref _tableSeq)}");
        var table   = await CreateTableAsync(area.Id);

        var order = await OpenOrderAsync(table.Id);

        await AddItemAsync(order.Id, product.Id, qty: 2);  // 2 × 50 = 100

        await CloseOrderAsync(order.Id);

        var paid = await PayOrderAsync(order.Id, amount: 100m);

        paid.ItemsSubtotal.Should().Be(100.00m);
        paid.CouvertAmount.Should().Be(0m);
        paid.ServiceFeeAmount.Should().Be(0m);
        paid.Total.Should().Be(100.00m);
        paid.Status.Should().Be("Paid");
    }

    /// <summary>
    /// When CouvertAutomatic=true and PartySize is given at open time,
    /// CouvertAmount = CouvertPricePerPerson × PartySize is stored on the order.
    /// </summary>
    [Fact]
    public async Task OpenOrder_WithCouvertAutomatic_AppliesCouvertAmount()
    {
        await _client.PutAsJsonAsync("/api/restaurante/settings", new UpdateFoodServiceSettingsRequest(
            StoreType:            "restaurant",
            CouvertEnabled:       true,
            CouvertPricePerPerson: 8.00m,
            CouvertAutomatic:     true,
            ServiceFeeEnabled:    false,
            ServiceFeePercent:    null,
            OrderTypesEnabled:    "DineIn,Counter,Takeaway"));

        try
        {
            var area  = await CreateAreaAsync($"Área Couvert {Interlocked.Increment(ref _tableSeq)}");
            var table = await CreateTableAsync(area.Id);

            var resp = await _client.PostAsJsonAsync("/api/restaurante/orders",
                new OpenOrderRequest("DineIn", TableId: table.Id, PartySize: 4));
            resp.StatusCode.Should().Be(HttpStatusCode.Created);
            var order = await resp.Content.ReadFromJsonAsync<OrderDto>();

            order!.CouvertAmount.Should().Be(32.00m); // 8.00 × 4
            order.PartySize.Should().Be(4);
        }
        finally
        {
            await ResetFoodServiceSettingsAsync();
        }
    }

    /// <summary>
    /// When CouvertAutomatic=false, couvert is NOT applied at open.
    /// When PartySize is provided in PayOrderRequest, couvert is calculated then.
    /// </summary>
    [Fact]
    public async Task PayOrder_WithManualCouvert_RecalculatesAtPayment()
    {
        await _client.PutAsJsonAsync("/api/restaurante/settings", new UpdateFoodServiceSettingsRequest(
            StoreType:            "restaurant",
            CouvertEnabled:       true,
            CouvertPricePerPerson: 10.00m,
            CouvertAutomatic:     false,
            ServiceFeeEnabled:    false,
            ServiceFeePercent:    null,
            OrderTypesEnabled:    "DineIn,Counter,Takeaway"));

        try
        {
            var product = await CreateProductWithStockAsync(
                $"MANUAL-{Interlocked.Increment(ref _tableSeq)}", salePrice: 40m, costPrice: 10m, initialStock: 5m);
            var area    = await CreateAreaAsync($"Área Manual {Interlocked.Increment(ref _tableSeq)}");
            var table   = await CreateTableAsync(area.Id);

            // Open without PartySize — couvert should be 0 at this point
            var order = await OpenOrderAsync(table.Id);
            order.CouvertAmount.Should().Be(0m); // not applied at open

            await AddItemAsync(order.Id, product.Id, qty: 1); // 40.00

            await CloseOrderAsync(order.Id);

            // Pay with PartySize=3 → couvert = 10.00 × 3 = 30.00
            var r = await _client.PostAsJsonAsync($"/api/restaurante/orders/{order.Id}/pay",
                new PayOrderRequest(
                    new List<PaymentInputDto> { new("Cash", "Cash", 70.00m) },
                    PartySize: 3));
            r.StatusCode.Should().Be(HttpStatusCode.OK);
            var paid = await r.Content.ReadFromJsonAsync<OrderDto>();

            paid!.ItemsSubtotal.Should().Be(40.00m);
            paid.CouvertAmount.Should().Be(30.00m); // 10.00 × 3 — set at pay time
            paid.ServiceFeeAmount.Should().Be(0m);
            paid.Total.Should().Be(70.00m);
        }
        finally
        {
            await ResetFoodServiceSettingsAsync();
        }
    }

    /// <summary>
    /// ServiceFeeAmount = ItemsSubtotal × rate, couvert is excluded from fee base.
    /// With no couvert, Total = ItemsSubtotal + ServiceFeeAmount.
    /// </summary>
    [Fact]
    public async Task PayOrder_ServiceFeeOnly_DoesNotApplyFeeOnCouvert()
    {
        await _client.PutAsJsonAsync("/api/restaurante/settings", new UpdateFoodServiceSettingsRequest(
            StoreType:            "restaurant",
            CouvertEnabled:       false,
            CouvertPricePerPerson: null,
            CouvertAutomatic:     false,
            ServiceFeeEnabled:    true,
            ServiceFeePercent:    10.00m,
            OrderTypesEnabled:    "DineIn,Counter,Takeaway"));

        try
        {
            var product = await CreateProductWithStockAsync(
                $"FEE-{Interlocked.Increment(ref _tableSeq)}", salePrice: 100m, costPrice: 10m, initialStock: 5m);
            var area    = await CreateAreaAsync($"Área Fee {Interlocked.Increment(ref _tableSeq)}");
            var table   = await CreateTableAsync(area.Id);

            var order = await OpenOrderAsync(table.Id);

            await AddItemAsync(order.Id, product.Id, qty: 1); // 100.00

            await CloseOrderAsync(order.Id);

            var r = await _client.PostAsJsonAsync($"/api/restaurante/orders/{order.Id}/pay",
                new PayOrderRequest(
                    new List<PaymentInputDto> { new("Cash", "Cash", 110.00m) }));
            r.StatusCode.Should().Be(HttpStatusCode.OK);
            var paid = await r.Content.ReadFromJsonAsync<OrderDto>();

            paid!.ItemsSubtotal.Should().Be(100.00m);
            paid.CouvertAmount.Should().Be(0m);
            paid.ServiceFeeAmount.Should().Be(10.00m); // 100 × 10% — couvert not in fee base
            paid.Total.Should().Be(110.00m);
        }
        finally
        {
            await ResetFoodServiceSettingsAsync();
        }
    }

    /// <summary>
    /// ServiceFee applies only to ItemsSubtotal (not couvert).
    /// Total = ItemsSubtotal + CouvertAmount + ServiceFeeAmount.
    /// Formula: Items=120, Couvert=16, Fee=12(=120×10%), Total=148.
    /// </summary>
    [Fact]
    public async Task PayOrder_CouvertAndServiceFee_CalculatesCorrectBreakdown()
    {
        await _client.PutAsJsonAsync("/api/restaurante/settings", new UpdateFoodServiceSettingsRequest(
            StoreType:            "restaurant",
            CouvertEnabled:       true,
            CouvertPricePerPerson: 8.00m,
            CouvertAutomatic:     true,
            ServiceFeeEnabled:    true,
            ServiceFeePercent:    10.00m,
            OrderTypesEnabled:    "DineIn,Counter,Takeaway"));

        try
        {
            var product = await CreateProductWithStockAsync(
                $"DISH-{Interlocked.Increment(ref _tableSeq)}", salePrice: 60m, costPrice: 10m, initialStock: 5m);
            var area    = await CreateAreaAsync($"Área Dish {Interlocked.Increment(ref _tableSeq)}");
            var table   = await CreateTableAsync(area.Id);

            var resp = await _client.PostAsJsonAsync("/api/restaurante/orders",
                new OpenOrderRequest("DineIn", TableId: table.Id, PartySize: 2));
            resp.StatusCode.Should().Be(HttpStatusCode.Created);
            var order = await resp.Content.ReadFromJsonAsync<OrderDto>();

            await AddItemAsync(order!.Id, product.Id, qty: 2); // 2 × 60 = 120

            await CloseOrderAsync(order.Id);

            var r = await _client.PostAsJsonAsync($"/api/restaurante/orders/{order.Id}/pay",
                new PayOrderRequest(
                    new List<PaymentInputDto> { new("Cash", "Cash", 148.00m) }));
            r.StatusCode.Should().Be(HttpStatusCode.OK);
            var paid = await r.Content.ReadFromJsonAsync<OrderDto>();

            paid!.ItemsSubtotal.Should().Be(120.00m);
            paid.CouvertAmount.Should().Be(16.00m);   // 8.00 × 2
            paid.ServiceFeeAmount.Should().Be(12.00m);  // 120 × 10% — NOT (120+16) × 10%
            paid.Total.Should().Be(148.00m);
            paid.Status.Should().Be("Paid");
        }
        finally
        {
            await ResetFoodServiceSettingsAsync();
        }
    }

    /// <summary>
    /// Adding an item to an order without satisfying a required modifier group
    /// returns 422 UnprocessableEntity (or 400 BadRequest).
    /// A modifier option is added to the group so the validation tests the real business rule
    /// ("user failed to select from available options"), not an empty-group edge case.
    /// </summary>
    [Fact]
    public async Task AddItem_WithRequiredModifierGroupMissing_Returns422()
    {
        var product = await CreateProductWithStockAsync(
            $"BURGER-{Interlocked.Increment(ref _tableSeq)}", salePrice: 30m, costPrice: 10m, initialStock: 5m);

        // Create a required modifier group
        var groupResp = await _client.PostAsJsonAsync("/api/restaurante/modifier-groups",
            new CreateModifierGroupRequest(
                ProductId:     product.Id,
                Name:          "Ponto da carne",
                IsRequired:    true,
                MinSelections: 1,
                MaxSelections: 1,
                SortOrder:     0));
        groupResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var group = await groupResp.Content.ReadFromJsonAsync<ModifierGroupDto>();

        // Add a modifier option to the group so the "required" check is meaningful
        var modifierResp = await _client.PostAsJsonAsync(
            $"/api/restaurante/modifier-groups/{group!.Id}/modifiers",
            new CreateModifierRequest(group.Id, "Mal passado", PriceAdjustment: 0m, SortOrder: 0));
        modifierResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var area  = await CreateAreaAsync($"Área Modifier {Interlocked.Increment(ref _tableSeq)}");
        var table = await CreateTableAsync(area.Id);

        var orderResp = await _client.PostAsJsonAsync("/api/restaurante/orders",
            new OpenOrderRequest("DineIn", TableId: table.Id));
        orderResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await orderResp.Content.ReadFromJsonAsync<OrderDto>();

        // Act: add item WITHOUT selecting the required modifier → validation must reject
        var addResp = await _client.PostAsJsonAsync(
            $"/api/restaurante/orders/{order!.Id}/items",
            new AddOrderItemRequest(product.Id, Quantity: 1));

        addResp.StatusCode.Should().BeOneOf(
            HttpStatusCode.UnprocessableEntity,
            HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Cancelling a DineIn order releases the table (SetAvailable),
    /// allowing a new order to be opened on the same table.
    /// </summary>
    [Fact]
    public async Task CancelOrder_DineIn_TableBecomesAvailable()
    {
        await ResetFoodServiceSettingsAsync();

        var area  = await CreateAreaAsync($"Área CancelDineIn {Interlocked.Increment(ref _tableSeq)}");
        var table = await CreateTableAsync(area.Id);

        var order = await OpenOrderAsync(table.Id);

        var cancelResp = await _client.PostAsync(
            $"/api/restaurante/orders/{order.Id}/cancel", null);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelled = await cancelResp.Content.ReadFromJsonAsync<OrderDto>();
        cancelled!.Status.Should().Be("Cancelled");

        // Prove table is free: we can open another order on the same table
        var reopenResp = await _client.PostAsJsonAsync("/api/restaurante/orders",
            new OpenOrderRequest("DineIn", TableId: table.Id));
        reopenResp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Counter/Takeaway orders (no TableId) can be cancelled without table logic.
    /// </summary>
    [Fact]
    public async Task CancelOrder_Counter_SucceedsWithoutTable()
    {
        await ResetFoodServiceSettingsAsync();

        var openResp = await _client.PostAsJsonAsync("/api/restaurante/orders",
            new OpenOrderRequest("Counter"));
        openResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await openResp.Content.ReadFromJsonAsync<OrderDto>();
        order!.TableId.Should().BeNull();

        var cancelResp = await _client.PostAsync(
            $"/api/restaurante/orders/{order.Id}/cancel", null);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelled = await cancelResp.Content.ReadFromJsonAsync<OrderDto>();
        cancelled!.Status.Should().Be("Cancelled");
        cancelled.TableId.Should().BeNull();
    }

    // Note: this test verifies positive visibility (this store sees its own orders).
    // Full negative-visibility testing (another store cannot see these orders) requires
    // a multi-store test client setup and is tracked as a Phase 2 integration test concern.
    /// <summary>
    /// Orders are scoped to the current store via the StoreEntity global query filter.
    /// GET /tables/{id}/orders returns only the orders opened in this store context.
    /// </summary>
    [Fact]
    public async Task OpenOrder_IsIsolatedToCurrentStore()
    {
        await ResetFoodServiceSettingsAsync();

        var area  = await CreateAreaAsync($"Área Isolated {Interlocked.Increment(ref _tableSeq)}");
        var table = await CreateTableAsync(area.Id);

        var openResp = await _client.PostAsJsonAsync("/api/restaurante/orders",
            new OpenOrderRequest("DineIn", TableId: table.Id));
        openResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await openResp.Content.ReadFromJsonAsync<OrderDto>();

        // Retrieve orders for this table — should contain the one we just opened
        var historyResp = await _client.GetAsync($"/api/restaurante/tables/{table.Id}/orders");
        historyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var orders = await historyResp.Content.ReadFromJsonAsync<List<OrderDto>>();

        // All returned orders belong to the same table and are accessible in this store context
        orders.Should().NotBeEmpty();
        orders!.All(o => o.TableId == table.Id).Should().BeTrue();

        // The order we opened must be present
        orders!.Any(o => o.Id == order!.Id).Should().BeTrue();
    }
}
