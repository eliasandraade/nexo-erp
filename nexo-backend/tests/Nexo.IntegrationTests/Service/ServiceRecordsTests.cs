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
/// End-to-end coverage for Service record entries (PR2): internal notes/history with durable
/// attachment refs, store-scoped. Exercises the family module gate, context validation
/// (customer/subject must exist in tenant), reserved-context rejection, and tenant isolation.
/// </summary>
[Collection("Integration")]
public class ServiceRecordsTests
{
    private const string ForeignTaxId = "77666555000202";

    private readonly TestWebApplicationFactory _factory;
    public ServiceRecordsTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Records_forbidden_without_service_module()
    {
        var client = await AuthClientFactory.LoginAsync(_factory, "clara.boutique", "boutique@123");
        var resp = await client.GetAsync(
            $"/api/v1/service/records?contextType=Customer&contextId={Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Record_create_for_customer_then_list_get_delete()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(client);

        var createResp = await client.PostAsJsonAsync("/api/v1/service/records", new
        {
            contextType = "Customer",
            contextId = customerId,
            text = "primeira visita",
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("contextType").GetString().Should().Be("Customer");
        created.GetProperty("authorUserId").GetGuid().Should().NotBe(Guid.Empty);

        var list = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/service/records?contextType=Customer&contextId={customerId}");
        list.EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).Should().Contain(id);

        (await client.GetAsync($"/api/v1/service/records/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.DeleteAsync($"/api/v1/service/records/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/v1/service/records/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Record_create_for_subject_with_attachment_resolves_null_url_when_storage_disabled()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(client);
        var subjResp = await client.PostAsJsonAsync("/api/v1/service/subjects",
            new { customerId, kind = "Pet", displayName = "Rex" });
        var subjectId = (await subjResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var createResp = await client.PostAsJsonAsync("/api/v1/service/records", new
        {
            contextType = "Subject",
            contextId = subjectId,
            attachments = new[]
            {
                new { storageKey = "tenants/x/service/records/abc.jpg", fileName = "abc.jpg", contentType = "image/jpeg", sizeBytes = 1234L, caption = "antes" },
            },
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var att = dto.GetProperty("attachments").EnumerateArray().Single();
        att.GetProperty("storageKey").GetString().Should().Be("tenants/x/service/records/abc.jpg");
        att.GetProperty("fileName").GetString().Should().Be("abc.jpg");
        // Storage is not configured in tests → URL resolves to null (honest "unavailable").
        att.GetProperty("url").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Record_create_without_text_or_attachments_returns_400()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(client);
        var resp = await client.PostAsJsonAsync("/api/v1/service/records",
            new { contextType = "Customer", contextId = customerId });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Record_create_with_reserved_context_type_returns_400()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var resp = await client.PostAsJsonAsync("/api/v1/service/records",
            new { contextType = "Appointment", contextId = Guid.NewGuid(), text = "x" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Record_create_with_foreign_customer_context_returns_404()
    {
        var foreignCustomerId = await SeedForeignCustomerAsync();
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var resp = await client.PostAsJsonAsync("/api/v1/service/records",
            new { contextType = "Customer", contextId = foreignCustomerId, text = "x" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Record_from_another_tenant_is_not_accessible()
    {
        var foreignId = await SeedForeignRecordAsync();
        await AssertRecordExistsInDbAsync(foreignId);

        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await client.GetAsync($"/api/v1/service/records/{foreignId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound,
                "the tenant/store global query filter must hide another tenant's record");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static async Task<Guid> CreateCustomerAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/api/customers", new
        {
            personType = "Individual",
            name = "Tutor " + Guid.NewGuid().ToString("N")[..8],
            documentType = "CPF",
            documentNumber = Guid.NewGuid().ToString("N")[..11],
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<(Guid TenantId, Guid StoreId, Guid CustomerId)> SeedForeignTenantAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

        var t = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TaxId == ForeignTaxId);
        Guid storeId;
        if (t is null)
        {
            t = Tenant.Create("Record Isolation Corp", ForeignTaxId, "admin@rec-iso.test");
            db.Tenants.Add(t);
            var store = Store.Create(t.Id, "RIC Store", "rec-iso-default");
            db.Stores.Add(store);
            await db.SaveChangesAsync();
            storeId = store.Id;
        }
        else
        {
            storeId = await db.Stores.IgnoreQueryFilters()
                .Where(s => s.TenantId == t.Id).Select(s => s.Id).FirstAsync();
        }

        var cust = await db.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == t.Id);
        if (cust is null)
        {
            cust = Customer.Create(t.Id, PersonType.Individual, "Foreign Tutor",
                DocumentType.Cpf, Guid.NewGuid().ToString("N")[..11]);
            db.Customers.Add(cust);
            await db.SaveChangesAsync();
        }
        return (t.Id, storeId, cust.Id);
    }

    private async Task<Guid> SeedForeignCustomerAsync() => (await SeedForeignTenantAsync()).CustomerId;

    private async Task<Guid> SeedForeignRecordAsync()
    {
        var (tenantId, storeId, customerId) = await SeedForeignTenantAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var rec = SvcRecordEntry.Create(
            tenantId, SvcRecordContextType.Customer, customerId, Guid.NewGuid(), "foreign note", null);
        db.SvcRecordEntries.Add(rec);
        // StoreEntity: no HTTP context here, so the interceptor doesn't auto-inject StoreId —
        // assign it explicitly (same pattern as the Product/professional isolation tests).
        db.Entry(rec).Property("StoreId").CurrentValue = storeId;
        await db.SaveChangesAsync();
        return rec.Id;
    }

    private async Task AssertRecordExistsInDbAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.SvcRecordEntries.IgnoreQueryFilters().AnyAsync(r => r.Id == id))
            .Should().BeTrue("seeding failed — test would be vacuously 404");
    }
}
