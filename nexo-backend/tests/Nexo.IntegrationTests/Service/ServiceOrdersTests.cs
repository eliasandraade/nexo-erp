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
/// End-to-end coverage for Service orders (PR4): manual + from-appointment creation, line items
/// with server-side totals + immutable price snapshots, the order status machine, terminal guards,
/// reference rules, and tenant isolation.
/// </summary>
[Collection("Integration")]
public class ServiceOrdersTests
{
    private const string ForeignTaxId = "77666555000204";
    private static readonly DateTime Base = DateTime.UtcNow.Date.AddDays(5).AddHours(9); // UTC

    private readonly TestWebApplicationFactory _factory;
    public ServiceOrdersTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── Gate ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Orders_forbidden_without_service_module()
    {
        var client = await AuthClientFactory.LoginAsync(_factory, "clara.boutique", "boutique@123");
        (await client.GetAsync("/api/v1/service/orders")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Orders_accessible_with_service_module()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await client.GetAsync("/api/v1/service/orders")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Manual create + items + totals ───────────────────────────────────────
    [Fact]
    public async Task Manual_create_get_add_update_delete_item_recomputes_total()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var catalogId = await CreateCatalogAsync(c, price: 100m, requiresSubject: false);

        var orderId = await CreateOrderAsync(c, customerId);
        var get0 = await (await c.GetAsync($"/api/v1/service/orders/{orderId}")).Content.ReadFromJsonAsync<JsonElement>();
        get0.GetProperty("status").GetString().Should().Be("Draft");
        get0.GetProperty("totalAmount").GetDecimal().Should().Be(0m);
        get0.GetProperty("code").GetString().Should().StartWith("OS-");

        // add 2 × 100 = 200
        var afterAdd = await AddItemAsync(c, orderId, catalogId, qty: 2m);
        afterAdd.GetProperty("totalAmount").GetDecimal().Should().Be(200m);
        var itemId = afterAdd.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        // update qty → 3 × 100 = 300
        var afterUpd = await (await c.PutAsJsonAsync(
            $"/api/v1/service/orders/{orderId}/items/{itemId}", new { quantity = 3m })).Content.ReadFromJsonAsync<JsonElement>();
        afterUpd.GetProperty("totalAmount").GetDecimal().Should().Be(300m);

        // delete → 0
        var afterDel = await (await c.DeleteAsync(
            $"/api/v1/service/orders/{orderId}/items/{itemId}")).Content.ReadFromJsonAsync<JsonElement>();
        afterDel.GetProperty("totalAmount").GetDecimal().Should().Be(0m);
        afterDel.GetProperty("items").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Item_price_snapshot_is_immutable_after_catalog_price_change()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var catalogId = await CreateCatalogAsync(c, price: 100m, requiresSubject: false);
        var orderId = await CreateOrderAsync(c, customerId);
        await AddItemAsync(c, orderId, catalogId, qty: 2m); // 200

        // catalog price changes to 999
        (await c.PutAsJsonAsync($"/api/v1/service/catalog/{catalogId}",
            new { name = "X", durationMinutes = 60, price = 999m, requiresSubject = false }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await (await c.GetAsync($"/api/v1/service/orders/{orderId}")).Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("totalAmount").GetDecimal().Should().Be(200m);
        dto.GetProperty("items").EnumerateArray().Single()
            .GetProperty("unitPriceSnapshot").GetDecimal().Should().Be(100m);
    }

    // ── Status machine ───────────────────────────────────────────────────────
    [Fact]
    public async Task Status_machine_and_terminal_guards()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var catalogId = await CreateCatalogAsync(c, price: 50m, requiresSubject: false);
        var orderId = await CreateOrderAsync(c, customerId);

        (await PatchStatus(c, orderId, "Open")).Should().Be(HttpStatusCode.OK);
        (await PatchStatus(c, orderId, "InProgress")).Should().Be(HttpStatusCode.OK);
        (await PatchStatus(c, orderId, "Completed")).Should().Be(HttpStatusCode.OK);

        // terminal blocks edit
        (await c.PutAsJsonAsync($"/api/v1/service/orders/{orderId}", new { notes = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        // terminal blocks item add
        (await c.PostAsJsonAsync($"/api/v1/service/orders/{orderId}/items", new { catalogItemId = catalogId, quantity = 1m }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Invalid_status_transition_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var orderId = await CreateOrderAsync(c, customerId);
        (await PatchStatus(c, orderId, "Completed")).Should().Be(HttpStatusCode.UnprocessableEntity); // Draft→Completed invalid
    }

    [Fact]
    public async Task Bad_status_string_returns_400()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var orderId = await CreateOrderAsync(c, customerId);
        (await c.PatchAsJsonAsync($"/api/v1/service/orders/{orderId}/status", new { status = "Warp" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Reference rules ──────────────────────────────────────────────────────
    [Fact]
    public async Task Add_item_requiring_subject_without_order_subject_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var catalogId = await CreateCatalogAsync(c, price: 80m, requiresSubject: true);
        var orderId = await CreateOrderAsync(c, customerId); // no subject
        (await c.PostAsJsonAsync($"/api/v1/service/orders/{orderId}/items",
            new { catalogItemId = catalogId, quantity = 1m }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Create_with_subject_of_other_customer_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var c1 = await CreateCustomerAsync(c);
        var c2 = await CreateCustomerAsync(c);
        var subjectOfC2 = await CreateSubjectAsync(c, c2);
        (await c.PostAsJsonAsync("/api/v1/service/orders", new { customerId = c1, subjectId = subjectOfC2 }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Add_item_inactive_catalog_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var catalogId = await CreateCatalogAsync(c, price: 80m, requiresSubject: false);
        await c.PostAsync($"/api/v1/service/catalog/{catalogId}/deactivate", null);
        var orderId = await CreateOrderAsync(c, customerId);
        (await c.PostAsJsonAsync($"/api/v1/service/orders/{orderId}/items",
            new { catalogItemId = catalogId, quantity = 1m }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Create_with_inactive_professional_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var professionalId = await CreateProfessionalAsync(c);
        await c.PostAsync($"/api/v1/service/professionals/{professionalId}/deactivate", null);
        (await c.PostAsJsonAsync("/api/v1/service/orders", new { customerId, professionalId }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Create_with_missing_customer_returns_404()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.PostAsJsonAsync("/api/v1/service/orders", new { customerId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_with_missing_customer_id_returns_400()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.PostAsJsonAsync("/api/v1/service/orders", new { notes = "no customer" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── From appointment ─────────────────────────────────────────────────────
    [Fact]
    public async Task From_appointment_creates_order_with_initial_item_using_appointment_price()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var professionalId = await CreateProfessionalAsync(c);
        var catalogId = await CreateCatalogAsync(c, price: 120m, requiresSubject: false);
        var apptId = await CreateAppointmentAsync(c, customerId, professionalId, catalogId, Base, Base.AddHours(1));

        var resp = await c.PostAsync($"/api/v1/service/orders/from-appointment/{apptId}", null);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var ord = await resp.Content.ReadFromJsonAsync<JsonElement>();
        ord.GetProperty("appointmentId").GetGuid().Should().Be(apptId);
        ord.GetProperty("customerId").GetGuid().Should().Be(customerId);
        var item = ord.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("unitPriceSnapshot").GetDecimal().Should().Be(120m); // from appointment snapshot
        ord.GetProperty("totalAmount").GetDecimal().Should().Be(120m);

        // duplicate from same appointment → 409
        (await c.PostAsync($"/api/v1/service/orders/from-appointment/{apptId}", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task From_cancelled_appointment_is_rejected_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var professionalId = await CreateProfessionalAsync(c);
        var catalogId = await CreateCatalogAsync(c, price: 120m, requiresSubject: false);
        var apptId = await CreateAppointmentAsync(c, customerId, professionalId, catalogId, Base.AddDays(1), Base.AddDays(1).AddHours(1));
        await c.PatchAsJsonAsync($"/api/v1/service/appointments/{apptId}/status", new { status = "Cancelled", reason = "x" });

        (await c.PostAsync($"/api/v1/service/orders/from-appointment/{apptId}", null))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task From_unknown_appointment_returns_404()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.PostAsync($"/api/v1/service/orders/from-appointment/{Guid.NewGuid()}", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Cross-tenant ─────────────────────────────────────────────────────────
    [Fact]
    public async Task Order_from_another_tenant_is_not_accessible()
    {
        var foreignId = await SeedForeignOrderAsync();
        await AssertOrderExistsInDbAsync(foreignId);
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.GetAsync($"/api/v1/service/orders/{foreignId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Records with ContextType=Order ───────────────────────────────────────
    [Fact]
    public async Task Record_with_order_context_create_and_list()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var orderId = await CreateOrderAsync(c, customerId);

        var create = await c.PostAsJsonAsync("/api/v1/service/records",
            new { contextType = "Order", contextId = orderId, text = "execução iniciada" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var recId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var list = await c.GetFromJsonAsync<JsonElement>(
            $"/api/v1/service/records?contextType=Order&contextId={orderId}");
        list.EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).Should().Contain(recId);
    }

    [Fact]
    public async Task Record_with_foreign_order_context_returns_404()
    {
        var foreignOrderId = await SeedForeignOrderAsync();
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var resp = await c.PostAsJsonAsync("/api/v1/service/records",
            new { contextType = "Order", contextId = foreignOrderId, text = "x" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
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

    private static async Task<Guid> CreateProfessionalAsync(HttpClient c)
    {
        var r = await c.PostAsJsonAsync("/api/v1/service/professionals", new { name = "Pro " + Guid.NewGuid().ToString("N")[..6] });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateCatalogAsync(HttpClient c, decimal price, bool requiresSubject)
    {
        var r = await c.PostAsJsonAsync("/api/v1/service/catalog", new
        {
            name = "Svc " + Guid.NewGuid().ToString("N")[..6], durationMinutes = 60, price, requiresSubject,
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateSubjectAsync(HttpClient c, Guid customerId)
    {
        var r = await c.PostAsJsonAsync("/api/v1/service/subjects", new { customerId, kind = "Pet", displayName = "Rex" });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateAppointmentAsync(
        HttpClient c, Guid customerId, Guid professionalId, Guid catalogItemId, DateTime s, DateTime e)
    {
        var r = await c.PostAsJsonAsync("/api/v1/service/appointments", new
        {
            customerId, professionalId, catalogItemId, startsAt = s, endsAt = e,
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateOrderAsync(HttpClient c, Guid customerId)
    {
        var r = await c.PostAsJsonAsync("/api/v1/service/orders", new { customerId });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> AddItemAsync(HttpClient c, Guid orderId, Guid catalogId, decimal qty)
    {
        var r = await c.PostAsJsonAsync($"/api/v1/service/orders/{orderId}/items",
            new { catalogItemId = catalogId, quantity = qty });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpStatusCode> PatchStatus(HttpClient c, Guid orderId, string status, string? reason = null)
    {
        var r = await c.PatchAsJsonAsync($"/api/v1/service/orders/{orderId}/status", new { status, reason });
        return r.StatusCode;
    }

    private async Task<Guid> SeedForeignOrderAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var t = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TaxId == ForeignTaxId);
        Guid storeId;
        if (t is null)
        {
            t = Tenant.Create("Order Isolation Corp", ForeignTaxId, "admin@order-iso.test");
            db.Tenants.Add(t);
            var store = Store.Create(t.Id, "OIC Store", "order-iso-default");
            db.Stores.Add(store);
            await db.SaveChangesAsync();
            storeId = store.Id;
        }
        else storeId = await db.Stores.IgnoreQueryFilters().Where(s => s.TenantId == t.Id).Select(s => s.Id).FirstAsync();

        var cust = Customer.Create(t.Id, PersonType.Individual, "Foreign", DocumentType.Cpf, Guid.NewGuid().ToString("N")[..11]);
        db.Customers.Add(cust);
        await db.SaveChangesAsync();

        var order = SvcOrder.Create(t.Id, "OS-FOREIGN-0001", cust.Id, null, null, null, null);
        db.SvcOrders.Add(order);
        db.Entry(order).Property("StoreId").CurrentValue = storeId;
        await db.SaveChangesAsync();
        return order.Id;
    }

    private async Task AssertOrderExistsInDbAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.SvcOrders.IgnoreQueryFilters().AnyAsync(o => o.Id == id))
            .Should().BeTrue("seeding failed — test would be vacuously 404");
    }
}
