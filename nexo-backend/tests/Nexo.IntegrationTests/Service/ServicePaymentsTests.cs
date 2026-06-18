using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Service;

/// <summary>
/// End-to-end coverage for Service payments (PR6): manual records against an order or a customer
/// package, partial totals, void, summaries — and the hard guarantee that a payment never changes
/// the order total/status nor the package balance/status. No Stripe/cash/financial side effects.
/// </summary>
[Collection("Integration")]
public class ServicePaymentsTests
{
    private const string ForeignTaxId = "77666555000207";

    private readonly TestWebApplicationFactory _factory;
    public ServicePaymentsTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── Gate ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Payments_forbidden_without_service_module()
    {
        var client = await AuthClientFactory.LoginAsync(_factory, "clara.boutique", "boutique@123");
        (await client.GetAsync("/api/v1/service/payments")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Payments_accessible_with_service_module()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await client.GetAsync("/api/v1/service/payments")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Create ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task Payment_for_order_creates_paid_and_derives_customer()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var (orderId, _) = await OrderWithTotalAsync(c, customerId, 300m);

        var resp = await c.PostAsJsonAsync("/api/v1/service/payments",
            new { orderId, amount = 100m, method = "Pix", paidAt = DateTime.UtcNow });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("status").GetString().Should().Be("Paid");
        dto.GetProperty("customerId").GetGuid().Should().Be(customerId);       // derived from the order
        dto.GetProperty("orderId").GetGuid().Should().Be(orderId);
        dto.GetProperty("customerPackageId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Payment_for_customer_package_creates_paid_and_derives_customer()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var cpId = await CustomerPackageAsync(c, customerId, price: 500m);

        var resp = await c.PostAsJsonAsync("/api/v1/service/payments",
            new { customerPackageId = cpId, amount = 200m, method = "Cash", paidAt = DateTime.UtcNow });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("customerId").GetGuid().Should().Be(customerId);       // derived from the package
        dto.GetProperty("customerPackageId").GetGuid().Should().Be(cpId);
    }

    // ── Payload validation ───────────────────────────────────────────────────
    [Fact]
    public async Task Payment_with_no_target_returns_400()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.PostAsJsonAsync("/api/v1/service/payments", new { amount = 10m, method = "Cash", paidAt = DateTime.UtcNow }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Payment_with_both_targets_returns_400()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.PostAsJsonAsync("/api/v1/service/payments",
            new { orderId = Guid.NewGuid(), customerPackageId = Guid.NewGuid(), amount = 10m, method = "Cash", paidAt = DateTime.UtcNow }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Payment_with_non_utc_paid_at_returns_400()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.PostAsJsonAsync("/api/v1/service/payments",
            new { orderId = Guid.NewGuid(), amount = 10m, method = "Cash", paidAt = new DateTime(2026, 6, 18, 0, 0, 0, DateTimeKind.Unspecified) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Payment_with_non_positive_amount_returns_400()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.PostAsJsonAsync("/api/v1/service/payments",
            new { orderId = Guid.NewGuid(), amount = 0m, method = "Cash", paidAt = DateTime.UtcNow }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Payment_with_invalid_method_returns_400()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.PostAsJsonAsync("/api/v1/service/payments",
            new { orderId = Guid.NewGuid(), amount = 10m, method = "Bitcoin", paidAt = DateTime.UtcNow }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Target rules ─────────────────────────────────────────────────────────
    [Fact]
    public async Task Payment_for_unknown_order_returns_404()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.PostAsJsonAsync("/api/v1/service/payments",
            new { orderId = Guid.NewGuid(), amount = 10m, method = "Cash", paidAt = DateTime.UtcNow }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Payment_for_unknown_customer_package_returns_404()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.PostAsJsonAsync("/api/v1/service/payments",
            new { customerPackageId = Guid.NewGuid(), amount = 10m, method = "Cash", paidAt = DateTime.UtcNow }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Payment_for_cancelled_order_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var (orderId, _) = await OrderWithTotalAsync(c, customerId, 100m);
        await c.PatchAsJsonAsync($"/api/v1/service/orders/{orderId}/status", new { status = "Cancelled", reason = "x" });
        (await c.PostAsJsonAsync("/api/v1/service/payments", new { orderId, amount = 10m, method = "Cash", paidAt = DateTime.UtcNow }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Payment_for_cancelled_customer_package_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var cpId = await CustomerPackageAsync(c, customerId, 500m);
        await c.PostAsync($"/api/v1/service/customer-packages/{cpId}/cancel", null);
        (await c.PostAsJsonAsync("/api/v1/service/payments", new { customerPackageId = cpId, amount = 10m, method = "Cash", paidAt = DateTime.UtcNow }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Payment_exceeding_order_remaining_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var (orderId, _) = await OrderWithTotalAsync(c, customerId, 100m);
        (await c.PostAsJsonAsync("/api/v1/service/payments", new { orderId, amount = 101m, method = "Cash", paidAt = DateTime.UtcNow }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── Partial payments + summary + void ────────────────────────────────────
    [Fact]
    public async Task Partial_payments_accumulate_and_summary_reflects_remaining()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var (orderId, _) = await OrderWithTotalAsync(c, customerId, 300m);

        await PayOrder(c, orderId, 100m);
        await PayOrder(c, orderId, 50m);

        var s = await Summary(c, $"order/{orderId}");
        s.GetProperty("totalAmount").GetDecimal().Should().Be(300m);
        s.GetProperty("paidAmount").GetDecimal().Should().Be(150m);
        s.GetProperty("remainingAmount").GetDecimal().Should().Be(150m);
        s.GetProperty("isFullyPaid").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Customer_package_summary_calculates_paid_and_remaining()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var cpId = await CustomerPackageAsync(c, customerId, 500m);
        await PayCustomerPackage(c, cpId, 500m);

        var s = await Summary(c, $"customer-package/{cpId}");
        s.GetProperty("totalAmount").GetDecimal().Should().Be(500m);
        s.GetProperty("paidAmount").GetDecimal().Should().Be(500m);
        s.GetProperty("remainingAmount").GetDecimal().Should().Be(0m);
        s.GetProperty("isFullyPaid").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Void_sets_voided_drops_from_paid_and_blocks_double_void()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var (orderId, _) = await OrderWithTotalAsync(c, customerId, 300m);
        var paymentId = await PayOrder(c, orderId, 100m);

        var v = await c.PostAsJsonAsync($"/api/v1/service/payments/{paymentId}/void", new { reason = "wrong" });
        v.StatusCode.Should().Be(HttpStatusCode.OK);
        (await v.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Voided");

        var s = await Summary(c, $"order/{orderId}");
        s.GetProperty("paidAmount").GetDecimal().Should().Be(0m);          // voided no longer counts
        s.GetProperty("voidedAmount").GetDecimal().Should().Be(100m);
        s.GetProperty("remainingAmount").GetDecimal().Should().Be(300m);

        (await c.PostAsJsonAsync($"/api/v1/service/payments/{paymentId}/void", new { reason = "again" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);   // double void
    }

    // ── Immutability of the targets (the core PR6 guarantee) ─────────────────
    [Fact]
    public async Task Paying_an_order_does_not_change_its_total_or_status()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var (orderId, _) = await OrderWithTotalAsync(c, customerId, 300m);
        var paymentId = await PayOrder(c, orderId, 100m);
        await c.PostAsJsonAsync($"/api/v1/service/payments/{paymentId}/void", new { reason = "x" });

        var order = await (await c.GetAsync($"/api/v1/service/orders/{orderId}")).Content.ReadFromJsonAsync<JsonElement>();
        order.GetProperty("totalAmount").GetDecimal().Should().Be(300m);
        order.GetProperty("status").GetString().Should().Be("Draft");
    }

    [Fact]
    public async Task Paying_a_customer_package_does_not_change_its_balance_or_status()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var cpId = await CustomerPackageAsync(c, customerId, 500m);
        await PayCustomerPackage(c, cpId, 200m);

        var cp = await (await c.GetAsync($"/api/v1/service/customer-packages/{cpId}")).Content.ReadFromJsonAsync<JsonElement>();
        cp.GetProperty("status").GetString().Should().Be("Active");
        var balance = cp.GetProperty("items").EnumerateArray().Single();
        balance.GetProperty("remainingQuantity").GetDecimal().Should().Be(balance.GetProperty("totalQuantity").GetDecimal());
    }

    // ── Cross-tenant ─────────────────────────────────────────────────────────
    [Fact]
    public async Task Payment_for_cross_tenant_order_returns_404()
    {
        var foreignOrderId = (await SeedForeignTenantWithOrderAsync()).OrderId;
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.PostAsJsonAsync("/api/v1/service/payments",
            new { orderId = foreignOrderId, amount = 10m, method = "Cash", paidAt = DateTime.UtcNow }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Payment_from_another_tenant_is_not_accessible()
    {
        var foreignId = await SeedForeignPaymentAsync();
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.GetAsync($"/api/v1/service/payments/{foreignId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static async Task<Guid> CreateCustomerAsync(HttpClient c)
    {
        var r = await c.PostAsJsonAsync("/api/customers", new
        {
            personType = "Individual", name = "Cli " + Guid.NewGuid().ToString("N")[..8],
            documentType = "CPF", documentNumber = Guid.NewGuid().ToString("N")[..11],
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateCatalogAsync(HttpClient c, decimal price)
    {
        var r = await c.PostAsJsonAsync("/api/v1/service/catalog",
            new { name = "Svc " + Guid.NewGuid().ToString("N")[..6], durationMinutes = 60, price, requiresSubject = false });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Creates an order for the customer with a single item worth <paramref name="total"/> (qty 1).</summary>
    private static async Task<(Guid OrderId, decimal Total)> OrderWithTotalAsync(HttpClient c, Guid customerId, decimal total)
    {
        var catalogId = await CreateCatalogAsync(c, total);
        var ord = await c.PostAsJsonAsync("/api/v1/service/orders", new { customerId });
        var orderId = (await ord.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await c.PostAsJsonAsync($"/api/v1/service/orders/{orderId}/items",
            new { catalogItemId = catalogId, quantity = 1m })).StatusCode.Should().Be(HttpStatusCode.OK);
        return (orderId, total);
    }

    /// <summary>Creates a package (price = priceSnapshot) with one item and assigns it to the customer.</summary>
    private static async Task<Guid> CustomerPackageAsync(HttpClient c, Guid customerId, decimal price)
    {
        var catalogId = await CreateCatalogAsync(c, 50m);
        var pkg = await c.PostAsJsonAsync("/api/v1/service/packages",
            new { name = "Pkg " + Guid.NewGuid().ToString("N")[..6], price, validityDays = 30 });
        var packageId = (await pkg.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await c.PostAsJsonAsync($"/api/v1/service/packages/{packageId}/items",
            new { catalogItemId = catalogId, includedQuantity = 4m })).StatusCode.Should().Be(HttpStatusCode.OK);
        var assign = await c.PostAsJsonAsync("/api/v1/service/customer-packages",
            new { packageId, customerId, startsAt = DateTime.UtcNow });
        assign.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await assign.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> PayOrder(HttpClient c, Guid orderId, decimal amount)
    {
        var r = await c.PostAsJsonAsync("/api/v1/service/payments", new { orderId, amount, method = "Pix", paidAt = DateTime.UtcNow });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> PayCustomerPackage(HttpClient c, Guid cpId, decimal amount)
    {
        var r = await c.PostAsJsonAsync("/api/v1/service/payments", new { customerPackageId = cpId, amount, method = "Pix", paidAt = DateTime.UtcNow });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> Summary(HttpClient c, string path)
        => await c.GetFromJsonAsync<JsonElement>($"/api/v1/service/payments/{path}/summary");

    private async Task<(Guid TenantId, Guid StoreId, Guid CustomerId, Guid OrderId)> SeedForeignTenantWithOrderAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var t = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TaxId == ForeignTaxId);
        Guid storeId;
        if (t is null)
        {
            t = Tenant.Create("Pay Isolation Corp", ForeignTaxId, "admin@pay-iso.test");
            db.Tenants.Add(t);
            var store = Store.Create(t.Id, "PIC Store", "pay-iso-default");
            db.Stores.Add(store);
            await db.SaveChangesAsync();
            storeId = store.Id;
        }
        else storeId = await db.Stores.IgnoreQueryFilters().Where(s => s.TenantId == t.Id).Select(s => s.Id).FirstAsync();

        var cust = await db.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == t.Id);
        if (cust is null)
        {
            cust = Customer.Create(t.Id, PersonType.Individual, "Foreign", DocumentType.Cpf, Guid.NewGuid().ToString("N")[..11]);
            db.Customers.Add(cust);
            await db.SaveChangesAsync();
        }
        var order = await db.SvcOrders.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.TenantId == t.Id);
        if (order is null)
        {
            order = SvcOrder.Create(t.Id, "OS-PAY-FOREIGN", cust.Id, null, null, null, null);
            db.SvcOrders.Add(order);
            db.Entry(order).Property("StoreId").CurrentValue = storeId;
            await db.SaveChangesAsync();
        }
        return (t.Id, storeId, cust.Id, order.Id);
    }

    private async Task<Guid> SeedForeignPaymentAsync()
    {
        var (tenantId, storeId, customerId, orderId) = await SeedForeignTenantWithOrderAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var payment = SvcPayment.CreateForOrder(tenantId, customerId, orderId, 100m, SvcPaymentMethod.Cash, DateTime.UtcNow, null, null);
        db.SvcPayments.Add(payment);
        db.Entry(payment).Property("StoreId").CurrentValue = storeId;
        await db.SaveChangesAsync();
        return payment.Id;
    }
}
