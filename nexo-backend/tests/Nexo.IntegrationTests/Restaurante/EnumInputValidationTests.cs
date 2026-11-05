using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Financial;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Stock;
using Nexo.Application.Modules.Restaurante;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Restaurante;

/// <summary>
/// Um valor de enum que o cliente errou é erro de quem chamou: 422 com a lista do que vale. Antes, Enum.Parse estourava
/// em 500. Um teste por rota. Cada teste usa uma empresa própria com o módulo Restaurante.
/// </summary>
[Collection("Integration")]
public class EnumInputValidationTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly string _tag = Guid.NewGuid().ToString("N")[..12];
    private HttpClient _client = null!;
    private string _publicSlug = null!;

    public EnumInputValidationTests(TestWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        var login = $"enum-{_tag}";
        _publicSlug = $"enum-{_tag}";
        var password = TestCredentials.SameTenantManagerPassword;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var taxId = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
            var tenant = Tenant.Create($"Enum {_tag}", taxId, $"{login}@{TestCredentials.TestDomain}");
            db.Tenants.Add(tenant);
            var store = Store.Create(tenant.Id, $"Loja {_tag}", $"loja-{_tag}");
            store.SetPublicSlug(_publicSlug);
            db.Stores.Add(store);
            db.ModuleSubscriptions.Add(ModuleSubscription.CreateAdminGrant(tenant.Id, "restaurante"));
            db.Users.Add(User.Create(tenantId: tenant.Id, fullName: "Dona Fulana", email: $"{login}@{TestCredentials.TestDomain}", login: login,
                passwordHash: scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(password), role: UserRole.Diretoria));
            await db.SaveChangesAsync();
        }
        _client = await AuthClientFactory.LoginAsync(_factory, login, password);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Contas financeiras ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Inexistente")]
    [InlineData("Revenue")]             // estava no comentário do contrato como se fosse válido
    [InlineData("99")]                  // TryParse aceita número, e este não existe
    [InlineData("1")]                   // este existe, mas não é o nome de nada que o cliente possa saber
    [InlineData("Cash,Bank")]           // TryParse combina os valores
    public async Task A_financial_account_with_an_unknown_type_is_refused(string accountType)
        => await ExpectInvalidAsync<FinancialAccountType>(_client.PostAsJsonAsync("/api/financial/accounts",
            new { code = $"X-{_tag}", name = "Conta", accountType }));

    [Theory]
    [InlineData("Bank", "Bank")]
    [InlineData("bank", "Bank")]
    [InlineData(" Cash ", "Cash")]
    public async Task A_financial_account_with_a_known_type_is_created_whatever_the_case(string accountType, string saved)
    {
        var response = await _client.PostAsJsonAsync("/api/financial/accounts", new { code = $"X-{_tag}", name = "Conta", accountType });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await _factory.ServerLog.DescribeAsync(response));
        (await response.Content.ReadFromJsonAsync<FinancialAccountDto>())!.AccountType.Should().Be(saved);
    }

    // ── Pedidos de delivery ───────────────────────────────────────────────────

    [Fact]
    public async Task The_delivery_orders_list_refuses_an_unknown_filter()
    {
        await ExpectInvalidAsync<DeliveryOrderStatus>(_client.GetAsync("/api/restaurante/delivery-orders?status=Inexistente"));
        await ExpectInvalidAsync<DeliveryChannel>(_client.GetAsync("/api/restaurante/delivery-orders?channel=Inexistente"));

        await _client.GetAsync($"/api/restaurante/delivery-orders?status={DeliveryOrderStatus.Delivered}&channel={DeliveryChannel.PhoneCall}")
            .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
    }

    [Fact]
    public async Task Creating_a_delivery_order_refuses_an_unknown_channel_or_type()
    {
        await ExpectInvalidAsync<DeliveryChannel>(PostDeliveryOrder(channel: "Inexistente", orderType: nameof(DeliveryOrderType.Takeaway)));
        await ExpectInvalidAsync<DeliveryOrderType>(PostDeliveryOrder(channel: nameof(DeliveryChannel.PhoneCall), orderType: "Inexistente"));

        await PostDeliveryOrder(channel: "phonecall", orderType: "takeaway").ShouldHaveStatusAsync(HttpStatusCode.Created, _factory);
    }

    [Fact]
    public async Task Creating_a_manual_delivery_order_refuses_an_unknown_channel_or_type()
    {
        await ExpectInvalidAsync<DeliveryChannel>(PostManualOrder(channel: "Inexistente", orderType: nameof(DeliveryOrderType.Takeaway)));
        await ExpectInvalidAsync<DeliveryOrderType>(PostManualOrder(channel: nameof(DeliveryChannel.PhoneCall), orderType: "Inexistente"));
    }

    [Fact]
    public async Task The_public_portal_refuses_an_unknown_order_type()
        => await ExpectInvalidAsync<DeliveryOrderType>(_factory.CreateApiClient().PostAsJsonAsync("/api/public/orders", new
        {
            publicSlug = _publicSlug,
            orderType = "Inexistente",
            customerName = "Cliente",
            customerPhone = "11999999999",
            items = Array.Empty<object>(),
        }));

    [Fact]
    public async Task Changing_a_delivery_order_status_refuses_an_unknown_status()
    {
        var created = await PostDeliveryOrder(channel: nameof(DeliveryChannel.PhoneCall), orderType: nameof(DeliveryOrderType.Takeaway));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await _factory.ServerLog.DescribeAsync(created));
        var order = (await created.Content.ReadFromJsonAsync<DeliveryOrderDto>())!;

        await ExpectInvalidAsync<DeliveryOrderStatus>(
            _client.PatchAsJsonAsync($"/api/restaurante/delivery-orders/{order.Id}/status", new { status = "Inexistente" }));
    }

    // ── Comandas ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Opening_an_order_refuses_an_unknown_type()
    {
        await ExpectInvalidAsync<RestOrderType>(_client.PostAsJsonAsync("/api/restaurante/orders", new { orderType = "Inexistente" }));

        await _client.PostAsJsonAsync("/api/restaurante/orders", new { orderType = "counter" }).ShouldHaveStatusAsync(HttpStatusCode.Created, _factory);
    }

    [Fact]
    public async Task Changing_an_order_item_status_refuses_an_unknown_status_and_leaves_the_item_as_it_was()
    {
        var product = await PostAsync<ProductDto>("/api/products", new CreateProductRequest($"P-{_tag}", "Produto", "Un", SalePrice: 10m, CostPrice: 4m));
        (await _client.PostAsJsonAsync("/api/stock/adjust", new AdjustStockRequest(product.Id, 5, "ManualEntry", "Teste"))).EnsureSuccessStatusCode();
        var open = await PostAsync<OrderDto>("/api/restaurante/orders", new { orderType = "Counter" });
        var order = await PostAsync<OrderDto>($"/api/restaurante/orders/{open.Id}/items", new AddOrderItemRequest(product.Id, 1), HttpStatusCode.OK);
        var item = order.Items.Single();

        await ExpectInvalidAsync<RestOrderItemStatus>(
            _client.PatchAsJsonAsync($"/api/restaurante/orders/{order.Id}/items/{item.Id}/status", new { status = "Inexistente" }));

        var after = await _client.GetFromJsonAsync<OrderDto>($"/api/restaurante/orders/{order.Id}");
        after!.Items.Single().Status.Should().Be(item.Status);
    }

    // ── Cupons ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Creating_a_coupon_refuses_an_unknown_discount_type()
        => await ExpectInvalidAsync<CouponDiscountType>(_client.PostAsJsonAsync("/api/restaurante/coupons",
            new { code = $"C{_tag}", discountType = "Inexistente", discountValue = 10m }));

    [Fact]
    public async Task Updating_a_coupon_refuses_an_unknown_discount_type()
    {
        var created = await _client.PostAsJsonAsync("/api/restaurante/coupons",
            new { code = $"C{_tag}", discountType = "percentage", discountValue = 10m });
        created.IsSuccessStatusCode.Should().BeTrue(await _factory.ServerLog.DescribeAsync(created));
        var coupon = (await created.Content.ReadFromJsonAsync<CouponDto>())!;

        await ExpectInvalidAsync<CouponDiscountType>(_client.PutAsJsonAsync($"/api/restaurante/coupons/{coupon.Id}",
            new { discountType = "Inexistente", discountValue = 10m }));
    }

    // ── Mesas ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Changing_a_table_status_refuses_an_unknown_status()
    {
        var area = await PostAsync<AreaDto>("/api/restaurante/areas", new CreateAreaRequest($"Salao {_tag}"));
        var table = await PostAsync<TableDto>("/api/restaurante/tables", new CreateTableRequest(area.Id, "T1", Capacity: 4));

        await ExpectInvalidAsync<RestTableStatus>(_client.PatchAsJsonAsync($"/api/restaurante/tables/{table.Id}/status", new { status = "Inexistente" }));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task ExpectInvalidAsync<TEnum>(Task<HttpResponseMessage> request) where TEnum : struct, Enum
    {
        using var response = await request;
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await _factory.ServerLog.DescribeAsync(response));
        (await response.Content.ReadAsStringAsync()).Should().Contain(string.Join(", ", Enum.GetNames<TEnum>()), "a resposta lista o que vale");
    }

    private Task<HttpResponseMessage> PostDeliveryOrder(string channel, string orderType)
        => _client.PostAsJsonAsync("/api/restaurante/delivery-orders", new
        {
            channel,
            orderType,
            customerName = "Cliente",
            customerPhone = "11999999999",
        });

    private Task<HttpResponseMessage> PostManualOrder(string channel, string orderType)
        => _client.PostAsJsonAsync("/api/restaurante/delivery-orders/manual", new
        {
            channel,
            orderType,
            customerName = "Cliente",
            customerPhone = "11999999999",
        });

    private async Task<T> PostAsync<T>(string path, object body, HttpStatusCode expected = HttpStatusCode.Created)
    {
        var response = await _client.PostAsJsonAsync(path, body);
        response.StatusCode.Should().Be(expected, await _factory.ServerLog.DescribeAsync(response));
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
