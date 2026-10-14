using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Customers;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Sales;
using Nexo.Application.Modules.Varejo;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Sales;

/// <summary>
/// Listas de preço (PR 48): the sale's unit price is decided by the server — price lists by
/// customer, priority, validity — and never taken from the client.
/// </summary>
[Collection("Integration")]
public class PriceListTests
{
    private const string Lists = "/api/varejo/price-lists";
    private readonly TestWebApplicationFactory _factory;
    public PriceListTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task The_sale_uses_the_server_price_not_the_one_the_client_sends()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var product = await ProductAsync(c, salePrice: 10m);

        var line = await AddLineAsync(c, product, customerId: null, clientPrice: 0.01m);

        line.UnitPrice.Should().Be(10m);   // product price — the client's 0,01 is ignored
    }

    [Fact]
    public async Task Customer_list_beats_the_default_list_unless_the_default_has_higher_priority()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var product = await ProductAsync(c, salePrice: 10m);
        var customer = await CustomerAsync(c);

        var standard = await ListAsync(c, "Padrão PR48", isDefault: true);
        await PriceAsync(c, standard, product, 8m);
        var vip = await ListAsync(c, "VIP PR48");
        await PriceAsync(c, vip, product, 7m);
        (await c.PutAsync($"{Lists}/{vip}/customers/{customer}", null)).EnsureSuccessStatusCode();

        (await AddLineAsync(c, product, customer)).UnitPrice.Should().Be(7m);      // the customer's own list
        (await AddLineAsync(c, product, null)).UnitPrice.Should().Be(8m);          // everyone else: default list

        // a promotion on the default list, with higher priority, wins even for the VIP customer
        (await c.PutAsJsonAsync($"{Lists}/{standard}", new UpdatePriceListRequest("Padrão PR48", Priority: 5)))
            .EnsureSuccessStatusCode();
        (await AddLineAsync(c, product, customer)).UnitPrice.Should().Be(8m);

        await ResetDefaultAsync(c, standard);
    }

    [Fact]
    public async Task Expired_or_inactive_lists_fall_back_to_the_product_price()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var product = await ProductAsync(c, salePrice: 10m);
        var customer = await CustomerAsync(c);

        var expired = await ListAsync(c, "Expirada PR48", validUntil: DateTime.UtcNow.AddDays(-1));
        await PriceAsync(c, expired, product, 5m);
        (await c.PutAsync($"{Lists}/{expired}/customers/{customer}", null)).EnsureSuccessStatusCode();
        (await AddLineAsync(c, product, customer)).UnitPrice.Should().Be(10m);

        var future = await ListAsync(c, "Futura PR48", validFrom: DateTime.UtcNow.AddDays(3));
        await PriceAsync(c, future, product, 4m);
        (await c.PutAsync($"{Lists}/{future}/customers/{customer}", null)).EnsureSuccessStatusCode();   // replaces the link
        (await AddLineAsync(c, product, customer)).UnitPrice.Should().Be(10m);

        var current = await ListAsync(c, "Vigente PR48");
        await PriceAsync(c, current, product, 6m);
        (await c.PutAsync($"{Lists}/{current}/customers/{customer}", null)).EnsureSuccessStatusCode();
        (await AddLineAsync(c, product, customer)).UnitPrice.Should().Be(6m);

        (await c.PostAsync($"{Lists}/{current}/deactivate", null)).EnsureSuccessStatusCode();
        (await AddLineAsync(c, product, customer)).UnitPrice.Should().Be(10m);
    }

    [Fact]
    public async Task List_items_are_added_updated_and_customers_linked_once()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var a = await ProductAsync(c, 10m);
        var b = await ProductAsync(c, 20m);
        var customer = await CustomerAsync(c);
        var list = await ListAsync(c, "Atacado PR48");

        await PriceAsync(c, list, a, 9m);
        await PriceAsync(c, list, b, 18m);
        var detail = await PriceAsync(c, list, a, 8.5m);   // update existing line

        detail.Items.Should().HaveCount(2);
        detail.Items.Single(i => i.ProductId == a).Price.Should().Be(8.5m);
        detail.Items.Should().OnlyContain(i => i.ProductName != "");

        (await c.PutAsJsonAsync($"{Lists}/{list}/products", new SetProductPriceRequest(a, 1.005m)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);   // cents only

        var linked = await OkAsync<PriceListDetailDto>(await c.PutAsync($"{Lists}/{list}/customers/{customer}", null));
        linked.Customers.Should().ContainSingle(x => x.CustomerId == customer);
        var unlinked = await OkAsync<PriceListDetailDto>(await c.DeleteAsync($"{Lists}/{list}/customers/{customer}"));
        unlinked.Customers.Should().BeEmpty();
    }

    [Fact]
    public async Task Cost_prices_are_hidden_from_sellers()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var product = await ProductAsync(admin, salePrice: 10m, costPrice: 4m);
        var seller = await SellerAsync();

        (await seller.GetFromJsonAsync<ProductDto>($"/api/products/{product}"))!.CostPrice.Should().BeNull();
        (await admin.GetFromJsonAsync<ProductDto>($"/api/products/{product}"))!.CostPrice.Should().Be(4m);
        (await seller.GetAsync($"/api/products/{product}/sheet.pdf")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Only_one_list_is_default_even_after_reactivating_an_old_one()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var product = await ProductAsync(c, salePrice: 10m);
        var first = await ListAsync(c, "Padrão A PR48", isDefault: true);
        await PriceAsync(c, first, product, 9m);
        (await c.PostAsync($"{Lists}/{first}/deactivate", null)).EnsureSuccessStatusCode();

        var second = await ListAsync(c, "Padrão B PR48", isDefault: true);
        await PriceAsync(c, second, product, 8m);
        (await c.PostAsync($"{Lists}/{first}/activate", null)).EnsureSuccessStatusCode();

        var all = await c.GetFromJsonAsync<List<PriceListDto>>($"{Lists}?includeInactive=true");
        all!.Count(l => l.IsDefault).Should().Be(1);
        all.Single(l => l.Id == second).IsDefault.Should().BeTrue();
        all.Single(l => l.Id == second).ItemCount.Should().Be(1);
        (await AddLineAsync(c, product, null)).UnitPrice.Should().Be(8m);

        await ResetDefaultAsync(c, second);
    }

    [Fact]
    public async Task Linking_an_unknown_customer_is_not_found()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var list = await ListAsync(c, "Sem cliente PR48");
        (await c.PutAsync($"{Lists}/{list}/customers/{Guid.NewGuid()}", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sellers_do_not_see_line_costs_on_the_sale()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var product = await ProductAsync(admin, salePrice: 10m, costPrice: 4m);
        var seller = await SellerAsync();

        (await AddLineAsync(seller, product, null)).CostPrice.Should().BeNull();
        (await AddLineAsync(admin, product, null)).CostPrice.Should().Be(4m);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<T> OkAsync<T>(HttpResponseMessage resp)
    {
        resp.IsSuccessStatusCode.Should().BeTrue(await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<Guid> ProductAsync(HttpClient c, decimal salePrice, decimal costPrice = 1m)
    {
        var code = $"PR48-{Guid.NewGuid():N}"[..20];
        return (await OkAsync<ProductDto>(await c.PostAsJsonAsync("/api/products",
            new CreateProductRequest(Code: code, Name: $"Produto {code}", Unit: "Un",
                SalePrice: salePrice, CostPrice: costPrice, TrackStock: false)))).Id;
    }

    private static async Task<Guid> CustomerAsync(HttpClient c)
    {
        var doc = Guid.NewGuid().ToString("N")[..12];
        return (await OkAsync<CustomerDto>(await c.PostAsJsonAsync("/api/customers",
            new CreateCustomerRequest("Individual", $"Cliente {doc}", "Other", doc)))).Id;
    }

    private static async Task<Guid> ListAsync(HttpClient c, string name, bool isDefault = false,
        DateTime? validFrom = null, DateTime? validUntil = null)
        => (await OkAsync<PriceListDto>(await c.PostAsJsonAsync(Lists,
            new CreatePriceListRequest(name, IsDefault: isDefault, ValidFrom: validFrom, ValidUntil: validUntil)))).Id;

    private static async Task<PriceListDetailDto> PriceAsync(HttpClient c, Guid list, Guid product, decimal price)
        => await OkAsync<PriceListDetailDto>(await c.PutAsJsonAsync($"{Lists}/{list}/products", new SetProductPriceRequest(product, price)));

    private static async Task<SaleItemDto> AddLineAsync(HttpClient c, Guid product, Guid? customerId, decimal clientPrice = 999m)
    {
        var sale = await OkAsync<SaleDto>(await c.PostAsJsonAsync("/api/sales", new CreateSaleRequest(CustomerId: customerId)));
        var withLine = await OkAsync<SaleDto>(await c.PostAsJsonAsync($"/api/sales/{sale.Id}/items",
            new AddSaleItemRequest(product, 1, clientPrice)));
        return withLine.Items.Single();
    }

    /// <summary>The default list is tenant-wide: don't leave a test's default behind for other tests.</summary>
    private static async Task ResetDefaultAsync(HttpClient c, Guid list)
        => (await c.PostAsync($"{Lists}/{list}/deactivate", null)).EnsureSuccessStatusCode();

    private async Task<HttpClient> SellerAsync()
    {
        const string login = "pricelists.seller";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            if (!await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Login == login))
            {
                var admin = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Login == TestCredentials.AdminLogin);
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                db.Users.Add(User.Create(
                    tenantId: admin.TenantId, fullName: "Vendedor PR48",
                    email: $"{login}@{TestCredentials.TestLocalDomain}", login: login,
                    passwordHash: hasher.Hash(TestCredentials.SameTenantManagerPassword), role: UserRole.Vendedor));
                await db.SaveChangesAsync();
            }
        }
        return await AuthClientFactory.LoginAsync(_factory, login, TestCredentials.SameTenantManagerPassword);
    }
}
