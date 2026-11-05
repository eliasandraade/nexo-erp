using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.Application.Common;
using Nexo.Application.Features.Products;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Products;

/// <summary>
/// GET /api/products/paged applies the status filter on the server. It used to run on the client,
/// over one page: with the active products first, the inactive ones could not be reached at all.
/// </summary>
[Collection("Integration")]
public class ProductPagedStatusFilterTests
{
    private readonly TestWebApplicationFactory _factory;

    public ProductPagedStatusFilterTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Each_status_lists_and_counts_only_its_own_products()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var tag = $"PFS{Guid.NewGuid():N}"[..12];
        // Names put the inactive product last, beyond the first page.
        await CreateAsync(c, tag, "A");
        await CreateAsync(c, tag, "B");
        var inactive = await CreateAsync(c, tag, "Z");
        (await c.PostAsync($"/api/products/{inactive}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var onlyInactive = await PagedAsync(c, $"search={tag}&pageSize=1&onlyInactive=true");
        onlyInactive.TotalCount.Should().Be(1);
        onlyInactive.Items.Should().ContainSingle(p => p.Id == inactive);

        var all = await PagedAsync(c, $"search={tag}&pageSize=50&includeInactive=true");
        all.TotalCount.Should().Be(3);
        all.Items.Should().Contain(p => p.Id == inactive);

        var active = await PagedAsync(c, $"search={tag}&pageSize=50");
        active.TotalCount.Should().Be(2);
        active.Items.Should().OnlyContain(p => p.IsActive);
    }

    private static async Task<Guid> CreateAsync(HttpClient c, string tag, string suffix)
    {
        var resp = await c.PostAsJsonAsync("/api/products",
            new CreateProductRequest(Code: $"{tag}-{suffix}", Name: $"{tag} {suffix}", Unit: "Un", SalePrice: 10m, CostPrice: 1m, TrackStock: false));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<ProductDto>())!.Id;
    }

    private static async Task<PagedResult<ProductDto>> PagedAsync(HttpClient c, string query)
    {
        var resp = await c.GetAsync($"/api/products/paged?{query}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<PagedResult<ProductDto>>())!;
    }
}
