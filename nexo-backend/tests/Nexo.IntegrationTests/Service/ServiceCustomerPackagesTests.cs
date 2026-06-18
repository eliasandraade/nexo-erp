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
/// End-to-end coverage for assigning packages to customers + consuming balances (PR5): expiry,
/// price snapshot, auto-Consumed, order links (without touching the order total), and isolation.
/// </summary>
[Collection("Integration")]
public class ServiceCustomerPackagesTests
{
    private const string ForeignTaxId = "77666555000206";

    private readonly TestWebApplicationFactory _factory;
    public ServiceCustomerPackagesTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── Gate ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CustomerPackages_forbidden_without_service_module()
    {
        var client = await AuthClientFactory.LoginAsync(_factory, "clara.boutique", "boutique@123");
        (await client.GetAsync("/api/v1/service/customer-packages")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CustomerPackages_accessible_with_service_module()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await client.GetAsync("/api/v1/service/customer-packages")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Assign ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task Assign_creates_balances_and_computes_expiry_and_price()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var catalogId = await CreateCatalogAsync(c, 50m);
        var packageId = await CreatePackageWithItemAsync(c, catalogId, 4m, validityDays: 30, price: 500m);
        var startsAt = DateTime.UtcNow;

        var resp = await c.PostAsJsonAsync("/api/v1/service/customer-packages",
            new { packageId, customerId, startsAt });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("status").GetString().Should().Be("Active");
        dto.GetProperty("priceSnapshot").GetDecimal().Should().Be(500m);
        dto.GetProperty("code").GetString().Should().StartWith("PKG-");
        var balance = dto.GetProperty("items").EnumerateArray().Single();
        balance.GetProperty("catalogItemId").GetGuid().Should().Be(catalogId);
        balance.GetProperty("totalQuantity").GetDecimal().Should().Be(4m);
        balance.GetProperty("remainingQuantity").GetDecimal().Should().Be(4m);
        // ExpiresAt ≈ startsAt + 30d
        dto.GetProperty("expiresAt").GetDateTime().ToUniversalTime().Should().BeCloseTo(startsAt.AddDays(30), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Assign_inactive_package_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var catalogId = await CreateCatalogAsync(c, 50m);
        var packageId = await CreatePackageWithItemAsync(c, catalogId, 1m);
        await c.PostAsync($"/api/v1/service/packages/{packageId}/deactivate", null);
        (await c.PostAsJsonAsync("/api/v1/service/customer-packages", new { packageId, customerId, startsAt = DateTime.UtcNow }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Assign_package_without_items_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var pkgResp = await c.PostAsJsonAsync("/api/v1/service/packages", new { name = "Empty", price = 10m });
        var packageId = (await pkgResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await c.PostAsJsonAsync("/api/v1/service/customer-packages", new { packageId, customerId, startsAt = DateTime.UtcNow }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Assign_subject_of_other_customer_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var c1 = await CreateCustomerAsync(c);
        var c2 = await CreateCustomerAsync(c);
        var subjectOfC2 = await CreateSubjectAsync(c, c2);
        var catalogId = await CreateCatalogAsync(c, 50m);
        var packageId = await CreatePackageWithItemAsync(c, catalogId, 1m);
        (await c.PostAsJsonAsync("/api/v1/service/customer-packages",
            new { packageId, customerId = c1, subjectId = subjectOfC2, startsAt = DateTime.UtcNow }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Assign_unknown_customer_returns_404()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var catalogId = await CreateCatalogAsync(c, 50m);
        var packageId = await CreatePackageWithItemAsync(c, catalogId, 1m);
        (await c.PostAsJsonAsync("/api/v1/service/customer-packages",
            new { packageId, customerId = Guid.NewGuid(), startsAt = DateTime.UtcNow }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Assign_non_utc_starts_at_returns_400()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var catalogId = await CreateCatalogAsync(c, 50m);
        var packageId = await CreatePackageWithItemAsync(c, catalogId, 1m);
        // Unspecified-kind datetime serializes without a 'Z' → rejected.
        (await c.PostAsJsonAsync("/api/v1/service/customer-packages",
            new { packageId, customerId, startsAt = new DateTime(2026, 6, 18, 0, 0, 0, DateTimeKind.Unspecified) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Consume ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task Consume_reduces_balance_and_lists_usage()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var (cpId, catalogId, _) = await AssignedAsync(c, included: 4m);

        var resp = await Consume(c, cpId, catalogId, 1m);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().Single()
            .GetProperty("remainingQuantity").GetDecimal().Should().Be(3m);

        var usages = await c.GetFromJsonAsync<JsonElement>($"/api/v1/service/customer-packages/{cpId}/usages");
        usages.EnumerateArray().Should().HaveCount(1);
        usages.EnumerateArray().Single().GetProperty("quantity").GetDecimal().Should().Be(1m);
    }

    [Fact]
    public async Task Consume_insufficient_balance_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var (cpId, catalogId, _) = await AssignedAsync(c, included: 1m);
        (await Consume(c, cpId, catalogId, 5m)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Consume_catalog_not_in_balance_returns_404()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var (cpId, _, _) = await AssignedAsync(c, included: 1m);
        (await Consume(c, cpId, Guid.NewGuid(), 1m)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Consume_cancelled_package_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var (cpId, catalogId, _) = await AssignedAsync(c, included: 2m);
        (await c.PostAsync($"/api/v1/service/customer-packages/{cpId}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Consume(c, cpId, catalogId, 1m)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Consume_expired_package_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var catalogId = await CreateCatalogAsync(c, 50m);
        var packageId = await CreatePackageWithItemAsync(c, catalogId, 2m, validityDays: 1);
        // StartsAt 10 days ago, validity 1 day → expired.
        var assign = await c.PostAsJsonAsync("/api/v1/service/customer-packages",
            new { packageId, customerId, startsAt = DateTime.UtcNow.AddDays(-10) });
        var cpId = (await assign.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await Consume(c, cpId, catalogId, 1m)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Consume_zeroing_all_balances_marks_consumed()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var (cpId, catalogId, _) = await AssignedAsync(c, included: 2m);
        (await Consume(c, cpId, catalogId, 2m)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await (await c.GetAsync($"/api/v1/service/customer-packages/{cpId}")).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("status").GetString().Should().Be("Consumed");
        // a further consume is rejected (terminal)
        (await Consume(c, cpId, catalogId, 1m)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Consume_linked_to_order_does_not_change_order_total()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var catalogId = await CreateCatalogAsync(c, 100m);
        var packageId = await CreatePackageWithItemAsync(c, catalogId, 4m);
        var cpId = await AssignOkAsync(c, packageId, customerId);
        var (orderId, orderItemId) = await CreateOrderWithItemAsync(c, customerId, catalogId, qty: 2m); // order total 200

        var resp = await Consume(c, cpId, catalogId, 1m, orderId, orderItemId);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // The order total/status must be untouched by the package consumption.
        var order = await (await c.GetAsync($"/api/v1/service/orders/{orderId}")).Content.ReadFromJsonAsync<JsonElement>();
        order.GetProperty("totalAmount").GetDecimal().Should().Be(200m);
    }

    [Fact]
    public async Task Consume_with_order_of_different_customer_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var c1 = await CreateCustomerAsync(c);
        var c2 = await CreateCustomerAsync(c);
        var catalogId = await CreateCatalogAsync(c, 100m);
        var packageId = await CreatePackageWithItemAsync(c, catalogId, 4m);
        var cpId = await AssignOkAsync(c, packageId, c1);
        var (orderId, _) = await CreateOrderWithItemAsync(c, c2, catalogId, qty: 1m); // order belongs to c2
        (await Consume(c, cpId, catalogId, 1m, orderId)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Consume_with_subject_mismatch_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var subjectId = await CreateSubjectAsync(c, customerId);
        var catalogId = await CreateCatalogAsync(c, 100m);
        var packageId = await CreatePackageWithItemAsync(c, catalogId, 4m);
        var cpId = await AssignOkAsync(c, packageId, customerId, subjectId);   // package bound to subjectId
        var (orderId, _) = await CreateOrderWithItemAsync(c, customerId, catalogId, qty: 1m); // order has no subject
        (await Consume(c, cpId, catalogId, 1m, orderId)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Consume_order_item_without_order_returns_400()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var (cpId, catalogId, _) = await AssignedAsync(c, included: 2m);
        var resp = await c.PostAsJsonAsync($"/api/v1/service/customer-packages/{cpId}/consume",
            new { catalogItemId = catalogId, quantity = 1m, orderItemId = Guid.NewGuid() });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CustomerPackage_from_another_tenant_is_not_accessible()
    {
        var foreignId = await SeedForeignCustomerPackageAsync();
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.GetAsync($"/api/v1/service/customer-packages/{foreignId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
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

    private static async Task<Guid> CreateSubjectAsync(HttpClient c, Guid customerId)
    {
        var r = await c.PostAsJsonAsync("/api/v1/service/subjects", new { customerId, kind = "Pet", displayName = "Rex" });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreatePackageWithItemAsync(
        HttpClient c, Guid catalogId, decimal included, int? validityDays = 30, decimal price = 100m)
    {
        var pkg = await c.PostAsJsonAsync("/api/v1/service/packages",
            new { name = "Pkg " + Guid.NewGuid().ToString("N")[..6], price, validityDays });
        var packageId = (await pkg.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await c.PostAsJsonAsync($"/api/v1/service/packages/{packageId}/items",
            new { catalogItemId = catalogId, includedQuantity = included })).StatusCode.Should().Be(HttpStatusCode.OK);
        return packageId;
    }

    private static async Task<Guid> AssignOkAsync(HttpClient c, Guid packageId, Guid customerId, Guid? subjectId = null)
    {
        var r = await c.PostAsJsonAsync("/api/v1/service/customer-packages",
            new { packageId, customerId, subjectId, startsAt = DateTime.UtcNow });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Creates customer + catalog + package(item) + assigns; returns (customerPackageId, catalogId, customerId).</summary>
    private static async Task<(Guid CpId, Guid CatalogId, Guid CustomerId)> AssignedAsync(HttpClient c, decimal included)
    {
        var customerId = await CreateCustomerAsync(c);
        var catalogId = await CreateCatalogAsync(c, 50m);
        var packageId = await CreatePackageWithItemAsync(c, catalogId, included);
        var cpId = await AssignOkAsync(c, packageId, customerId);
        return (cpId, catalogId, customerId);
    }

    private static Task<HttpResponseMessage> Consume(
        HttpClient c, Guid cpId, Guid catalogItemId, decimal quantity, Guid? orderId = null, Guid? orderItemId = null)
        => c.PostAsJsonAsync($"/api/v1/service/customer-packages/{cpId}/consume",
            new { catalogItemId, quantity, orderId, orderItemId });

    private static async Task<(Guid OrderId, Guid OrderItemId)> CreateOrderWithItemAsync(
        HttpClient c, Guid customerId, Guid catalogId, decimal qty)
    {
        var ord = await c.PostAsJsonAsync("/api/v1/service/orders", new { customerId });
        var orderId = (await ord.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var added = await c.PostAsJsonAsync($"/api/v1/service/orders/{orderId}/items",
            new { catalogItemId = catalogId, quantity = qty });
        added.StatusCode.Should().Be(HttpStatusCode.OK);
        var itemId = (await added.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();
        return (orderId, itemId);
    }

    private async Task<Guid> SeedForeignCustomerPackageAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var t = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TaxId == ForeignTaxId);
        Guid storeId;
        if (t is null)
        {
            t = Tenant.Create("CP Isolation Corp", ForeignTaxId, "admin@cp-iso.test");
            db.Tenants.Add(t);
            var store = Store.Create(t.Id, "CIC Store", "cp-iso-default");
            db.Stores.Add(store);
            await db.SaveChangesAsync();
            storeId = store.Id;
        }
        else storeId = await db.Stores.IgnoreQueryFilters().Where(s => s.TenantId == t.Id).Select(s => s.Id).FirstAsync();

        var cust = Customer.Create(t.Id, PersonType.Individual, "Foreign", DocumentType.Cpf, Guid.NewGuid().ToString("N")[..11]);
        db.Customers.Add(cust);
        var pkg = SvcPackage.Create(t.Id, "Foreign Pkg", 100m, null, 30);
        db.SvcPackages.Add(pkg);
        db.Entry(pkg).Property("StoreId").CurrentValue = storeId;
        await db.SaveChangesAsync();

        var cp = SvcCustomerPackage.Create(t.Id, "PKG-FOREIGN", pkg.Id, cust.Id, null,
            DateTime.UtcNow, DateTime.UtcNow.AddDays(30), 100m, null);
        db.SvcCustomerPackages.Add(cp);
        db.Entry(cp).Property("StoreId").CurrentValue = storeId;
        await db.SaveChangesAsync();
        return cp.Id;
    }
}
