using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.Application.Features.Auth;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Stock;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Stock;

/// <summary>
/// Regression tests for GET /api/stock/paged.
///
/// Guards the bug where the repository filtered/counted/projected on the unmapped
/// computed property StockItem.AvailableQuantity (CurrentQuantity - ReservedQuantity).
/// EF Core cannot translate it to SQL, so every call — even with no filters — threw
/// InvalidOperationException and the endpoint returned 500. The fix uses the raw
/// column arithmetic; these tests assert 200 across the no-filter and status-filter
/// paths (each filter touches AvailableQuantity).
/// </summary>
[Collection("Integration")]
public class StockPagedTests
{
    private readonly HttpClient _client;

    public StockPagedTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateApiClient();
    }

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private async Task SeedProductWithStockAsync(string code, decimal initialStock)
    {
        var productResponse = await _client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(
                Code:       code,
                Name:       $"Product {code}",
                Unit:       "Un",
                SalePrice:  10m,
                CostPrice:  5m,
                TrackStock: true));
        productResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = await productResponse.Content.ReadFromJsonAsync<ProductDto>();

        if (initialStock > 0)
        {
            var adjust = await _client.PostAsJsonAsync("/api/stock/adjust",
                new AdjustStockRequest(product!.Id, initialStock, "ManualEntry"));
            adjust.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task GetPaged_WithNoFilters_Returns200_AndComputesKpiCounts()
    {
        await AuthenticateAsync();
        await SeedProductWithStockAsync($"STKPG-{Guid.NewGuid():N}".Substring(0, 12), 7m);

        var response = await _client.GetAsync("/api/stock/paged?page=1&pageSize=50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StockPagedResponse>();
        body.Should().NotBeNull();
        body!.Page.Should().Be(1);
        body.PageSize.Should().Be(50);
        body.Items.Should().NotBeNull();
        // KPI counts are derived in SQL — reaching here at all means no 500.
        body.BelowMinCount.Should().BeGreaterThanOrEqualTo(0);
        body.NoTurnoverCount.Should().BeGreaterThanOrEqualTo(0);
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("low")]
    [InlineData("normal")]
    [InlineData("all")]
    public async Task GetPaged_WithStatusFilter_Returns200(string status)
    {
        await AuthenticateAsync();

        var response = await _client.GetAsync($"/api/stock/paged?page=1&pageSize=50&status={status}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StockPagedResponse>();
        body.Should().NotBeNull();
    }
}
