using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.Application.Features.Products;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Stock;

/// <summary>
/// A unit the catalogue does not know is the caller's mistake, not a server failure. It used to answer 500 with
/// "Requested value 'Unit' was not found", and the comment on the request listed exactly that value as valid.
/// </summary>
[Collection("Integration")]
public class ProductUnitValidationTests
{
    private readonly TestWebApplicationFactory _factory;
    public ProductUnitValidationTests(TestWebApplicationFactory factory) => _factory = factory;

    private static object NewProduct(string code, string unit) =>
        new { code, name = "Produto " + code, unit, salePrice = 10m, costPrice = 4m };

    [Theory]
    [InlineData("Unit")]
    [InlineData("Box")]
    [InlineData("99")]
    public async Task Creating_with_an_unknown_unit_is_refused_as_invalid_input(string unit)
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);

        var response = await client.PostAsJsonAsync("/api/products", NewProduct("UNIT-BAD-" + Guid.NewGuid().ToString("N")[..8], unit));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Un, Kg");
    }

    [Theory]
    [InlineData("Un")]
    [InlineData("kg")]
    public async Task Creating_with_a_known_unit_works_in_any_case(string unit)
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);

        var response = await client.PostAsJsonAsync("/api/products", NewProduct("UNIT-OK-" + Guid.NewGuid().ToString("N")[..8], unit));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Updating_to_an_unknown_unit_is_refused_and_keeps_the_product()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var created = await client.PostAsJsonAsync("/api/products", NewProduct("UNIT-UPD-" + Guid.NewGuid().ToString("N")[..8], "Un"));
        var product = (await created.Content.ReadFromJsonAsync<ProductDto>())!;

        var response = await client.PutAsJsonAsync($"/api/products/{product.Id}",
            new { name = product.Name, unit = "Box", costPrice = 4m, salePrice = 10m, trackStock = true });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var after = (await client.GetFromJsonAsync<ProductDto>($"/api/products/{product.Id}"))!;
        after.Unit.Should().Be("Un");
    }
}
