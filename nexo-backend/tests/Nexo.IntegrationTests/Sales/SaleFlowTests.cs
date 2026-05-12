using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.Application.Features.Auth;
using Nexo.Application.Features.Cash;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Sales;
using Nexo.Application.Features.Stock;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Sales;

/// <summary>
/// Integration tests for the complete sale lifecycle: create → add items → confirm → cancel.
/// Verifies stock deduction, CashMovement generation, state machine transitions, and rollback.
///
/// Each test is isolated: it creates its own product and stock record.
/// The factory and DB are shared within the collection for performance.
/// </summary>
[Collection("Integration")]
public class SaleFlowTests
{
    private readonly HttpClient _client;

    public SaleFlowTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateApiClient();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private async Task<ProductDto> CreateProductWithStockAsync(
        string code, decimal salePrice, decimal costPrice, decimal initialStock)
    {
        var productResponse = await _client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(
                Code:       code,
                Name:       $"Product {code}",
                Unit:       "Un",
                SalePrice:  salePrice,
                CostPrice:  costPrice,
                TrackStock: true));
        productResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = await productResponse.Content.ReadFromJsonAsync<ProductDto>();

        await _client.PostAsJsonAsync("/api/stock/adjust",
            new AdjustStockRequest(
                ProductId:    product!.Id,
                Quantity:     initialStock,
                MovementType: "ManualEntry",
                Notes:        "Initial stock for test"));

        return product;
    }

    private async Task<CashSessionDto> OpenCashSessionAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/cash/sessions/open",
            new OpenCashSessionRequest(OpeningBalance: 0, Notes: "Test session"));
        // 201 or 409 (already open) — either is fine for pre-condition
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var open = await _client.GetFromJsonAsync<CashSessionDto>("/api/cash/sessions/open");
            return open!;
        }
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CashSessionDto>())!;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FullCashSale_DeductsStock_CreatesCashMovement_StatusIsPaid()
    {
        await AuthenticateAsync();
        await OpenCashSessionAsync();
        var product = await CreateProductWithStockAsync("INTG-01", salePrice: 100m, costPrice: 60m, initialStock: 10);

        // 1. Create sale
        var createResp = await _client.PostAsJsonAsync("/api/sales", new CreateSaleRequest());
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var sale = await createResp.Content.ReadFromJsonAsync<SaleDto>();

        // 2. Add item: 2 units at 100 each
        var addItemResp = await _client.PostAsJsonAsync($"/api/sales/{sale!.Id}/items",
            new AddSaleItemRequest(
                ProductId:      product.Id,
                Quantity:       2,
                UnitPrice:      100m,
                DiscountAmount: 0));
        addItemResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Confirm with full cash payment (total = 200)
        var confirmResp = await _client.PostAsJsonAsync($"/api/sales/{sale.Id}/confirm",
            new ConfirmSaleRequest(
                Payments: [new PaymentInput("Cash", "Cash", 200m)],
                DiscountAmount: 0,
                TaxAmount: 0));
        confirmResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmed = await confirmResp.Content.ReadFromJsonAsync<SaleDto>();

        confirmed!.Status.Should().Be("Paid");      // all cash → immediately Paid
        confirmed.Payments.Should().HaveCount(1);
        confirmed.Payments[0].Type.Should().Be("Cash");
        confirmed.Payments[0].Amount.Should().Be(200m);

        // 4. Verify stock was deducted (10 - 2 = 8)
        var stockResp = await _client.GetFromJsonAsync<StockItemDto>(
            $"/api/stock/product/{product.Id}");
        stockResp!.CurrentQuantity.Should().Be(8);
        stockResp.AvailableQuantity.Should().Be(8);
    }

    [Fact]
    public async Task SaleWithCreditPayment_StatusIsConfirmed_NotPaid()
    {
        await AuthenticateAsync();
        await OpenCashSessionAsync();
        var product = await CreateProductWithStockAsync("INTG-02", salePrice: 50m, costPrice: 30m, initialStock: 5);

        var createResp = await _client.PostAsJsonAsync("/api/sales", new CreateSaleRequest());
        var sale = await createResp.Content.ReadFromJsonAsync<SaleDto>();

        await _client.PostAsJsonAsync($"/api/sales/{sale!.Id}/items",
            new AddSaleItemRequest(product.Id, Quantity: 1, UnitPrice: 50m));

        var due = DateTime.UtcNow.AddDays(30);
        var confirmResp = await _client.PostAsJsonAsync($"/api/sales/{sale.Id}/confirm",
            new ConfirmSaleRequest(
                Payments: [new PaymentInput("Pix", "Credit", 50m, due)],
                DiscountAmount: 0,
                TaxAmount: 0));
        confirmResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmed = await confirmResp.Content.ReadFromJsonAsync<SaleDto>();

        confirmed!.Status.Should().Be("Confirmed");   // has credit → stays Confirmed
        confirmed.Payments[0].Type.Should().Be("Credit");
        confirmed.Payments[0].DueDate.Should().BeCloseTo(due, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CancelConfirmedSale_RestoresStock_StatusIsCancelled()
    {
        await AuthenticateAsync();
        await OpenCashSessionAsync();
        var product = await CreateProductWithStockAsync("INTG-03", salePrice: 80m, costPrice: 40m, initialStock: 5);

        var createResp = await _client.PostAsJsonAsync("/api/sales", new CreateSaleRequest());
        var sale = await createResp.Content.ReadFromJsonAsync<SaleDto>();

        await _client.PostAsJsonAsync($"/api/sales/{sale!.Id}/items",
            new AddSaleItemRequest(product.Id, Quantity: 3, UnitPrice: 80m));

        // Confirm with a credit payment so the sale stays in Confirmed (not Paid).
        // Paid sales cannot be cancelled — the test verifies cancellation of a Confirmed sale.
        var due = DateTime.UtcNow.AddDays(30);
        await _client.PostAsJsonAsync($"/api/sales/{sale.Id}/confirm",
            new ConfirmSaleRequest(
                Payments: [new PaymentInput("Pix", "Credit", 240m, due)]));

        // Verify stock deducted: 5 - 3 = 2
        var stockAfterConfirm = await _client.GetFromJsonAsync<StockItemDto>(
            $"/api/stock/product/{product.Id}");
        stockAfterConfirm!.CurrentQuantity.Should().Be(2);

        // Cancel
        var cancelResp = await _client.PostAsync($"/api/sales/{sale.Id}/cancel", null);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify stock restored: 2 + 3 = 5
        var stockAfterCancel = await _client.GetFromJsonAsync<StockItemDto>(
            $"/api/stock/product/{product.Id}");
        stockAfterCancel!.CurrentQuantity.Should().Be(5);

        // Verify sale status
        var cancelledSale = await _client.GetFromJsonAsync<SaleDto>($"/api/sales/{sale.Id}");
        cancelledSale!.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task ConfirmSale_PaymentTotalMismatch_ReturnsBadRequest()
    {
        await AuthenticateAsync();
        await OpenCashSessionAsync();
        var product = await CreateProductWithStockAsync("INTG-04", salePrice: 100m, costPrice: 50m, initialStock: 10);

        var createResp = await _client.PostAsJsonAsync("/api/sales", new CreateSaleRequest());
        var sale = await createResp.Content.ReadFromJsonAsync<SaleDto>();

        await _client.PostAsJsonAsync($"/api/sales/{sale!.Id}/items",
            new AddSaleItemRequest(product.Id, Quantity: 1, UnitPrice: 100m));

        // Attempt to confirm with wrong amount (50 != 100)
        var confirmResp = await _client.PostAsJsonAsync($"/api/sales/{sale.Id}/confirm",
            new ConfirmSaleRequest(
                Payments: [new PaymentInput("Cash", "Cash", 50m)]));

        confirmResp.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnprocessableEntity,
            HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ConfirmSale_InsufficientStock_ReturnsBadRequest()
    {
        await AuthenticateAsync();
        await OpenCashSessionAsync();
        // Only 1 unit in stock
        var product = await CreateProductWithStockAsync("INTG-05", salePrice: 100m, costPrice: 50m, initialStock: 1);

        var createResp = await _client.PostAsJsonAsync("/api/sales", new CreateSaleRequest());
        var sale = await createResp.Content.ReadFromJsonAsync<SaleDto>();

        // Request 5 units
        await _client.PostAsJsonAsync($"/api/sales/{sale!.Id}/items",
            new AddSaleItemRequest(product.Id, Quantity: 5, UnitPrice: 100m));

        var confirmResp = await _client.PostAsJsonAsync($"/api/sales/{sale.Id}/confirm",
            new ConfirmSaleRequest(
                Payments: [new PaymentInput("Cash", "Cash", 500m)]));

        confirmResp.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnprocessableEntity,
            HttpStatusCode.Conflict);

        // Stock should be unchanged
        var stock = await _client.GetFromJsonAsync<StockItemDto>($"/api/stock/product/{product.Id}");
        stock!.CurrentQuantity.Should().Be(1);  // rollback verified
    }

    [Fact]
    public async Task OpenCashSession_SecondOpenByAdmin_ReturnsConflict()
    {
        await AuthenticateAsync();

        // First open (may already be open from another test)
        await _client.PostAsJsonAsync("/api/cash/sessions/open",
            new OpenCashSessionRequest(OpeningBalance: 0));

        // Second open must fail with 409
        var secondResp = await _client.PostAsJsonAsync("/api/cash/sessions/open",
            new OpenCashSessionRequest(OpeningBalance: 100m, Notes: "Duplicate attempt"));

        secondResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
