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
/// End-to-end coverage for Service subjects (PR2): auth → family module gate → controller →
/// service → repo → DB, including tenant isolation and the foreign-customer rejection. The
/// default dev tenant holds the 'salao-beleza' service SKU; 'clara.boutique' (varejo only) is
/// the no-service negative case.
/// </summary>
[Collection("Integration")]
public class ServiceSubjectsTests
{
    private const string ForeignTaxId = "77666555000201";

    private readonly TestWebApplicationFactory _factory;
    public ServiceSubjectsTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── Module gate ──────────────────────────────────────────────────────────
    [Fact]
    public async Task Subjects_forbidden_without_service_module()
    {
        var client = await AuthClientFactory.LoginAsync(_factory, "clara.boutique", "boutique@123");
        (await client.GetAsync("/api/v1/service/subjects")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Subjects_accessible_with_service_module()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await client.GetAsync("/api/v1/service/subjects")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Subject_create_get_update_deactivate_activate()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(client);

        var createResp = await client.PostAsJsonAsync("/api/v1/service/subjects", new
        {
            customerId,
            kind = "Pet",
            displayName = "Rex",
            metadataJson = "{\"species\":\"dog\",\"breed\":\"shih-tzu\"}",
            notes = "friendly",
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("kind").GetString().Should().Be("Pet");
        created.GetProperty("customerId").GetGuid().Should().Be(customerId);
        created.GetProperty("isActive").GetBoolean().Should().BeTrue();

        (await client.GetAsync($"/api/v1/service/subjects/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var updateResp = await client.PutAsJsonAsync($"/api/v1/service/subjects/{id}", new
        {
            kind = "Other",
            displayName = "Rex II",
            metadataJson = "{\"note\":\"older\"}",
            notes = "senior",
        });
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("displayName").GetString().Should().Be("Rex II");
        updated.GetProperty("kind").GetString().Should().Be("Other");

        var deact = await client.PostAsync($"/api/v1/service/subjects/{id}/deactivate", null);
        (await deact.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean().Should().BeFalse();
        var act = await client.PostAsync($"/api/v1/service/subjects/{id}/activate", null);
        (await act.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Subject_list_filters_by_customer_and_kind()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var c1 = await CreateCustomerAsync(client);
        var c2 = await CreateCustomerAsync(client);
        await client.PostAsJsonAsync("/api/v1/service/subjects", new { customerId = c1, kind = "Pet", displayName = "A" });
        await client.PostAsJsonAsync("/api/v1/service/subjects", new { customerId = c1, kind = "Vehicle", displayName = "B" });
        await client.PostAsJsonAsync("/api/v1/service/subjects", new { customerId = c2, kind = "Pet", displayName = "C" });

        var byCustomer = await client.GetFromJsonAsync<JsonElement>($"/api/v1/service/subjects?customerId={c1}");
        byCustomer.EnumerateArray().Should().HaveCount(2);

        var byKind = await client.GetFromJsonAsync<JsonElement>($"/api/v1/service/subjects?customerId={c1}&kind=Vehicle");
        byKind.EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("displayName").GetString().Should().Be("B");
    }

    // ── Validation ───────────────────────────────────────────────────────────
    [Fact]
    public async Task Subject_create_with_blank_display_name_returns_400()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(client);
        var resp = await client.PostAsJsonAsync("/api/v1/service/subjects",
            new { customerId, kind = "Pet", displayName = "" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Subject_create_with_invalid_kind_returns_400()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(client);
        var resp = await client.PostAsJsonAsync("/api/v1/service/subjects",
            new { customerId, kind = "Spaceship", displayName = "X" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Subject_create_with_invalid_metadata_json_returns_400()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(client);
        var resp = await client.PostAsJsonAsync("/api/v1/service/subjects",
            new { customerId, kind = "Pet", displayName = "Rex", metadataJson = "{not valid json" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Cross-tenant ─────────────────────────────────────────────────────────
    [Fact]
    public async Task Subject_create_with_foreign_customer_returns_404()
    {
        var (_, foreignCustomerId) = await SeedForeignTenantWithCustomerAsync();
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var resp = await client.PostAsJsonAsync("/api/v1/service/subjects",
            new { customerId = foreignCustomerId, kind = "Pet", displayName = "Ghost" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a customer from another tenant is invisible — subject must not link cross-tenant");
    }

    [Fact]
    public async Task Subject_from_another_tenant_is_not_accessible()
    {
        var foreignId = await SeedForeignSubjectAsync();
        await AssertSubjectExistsInDbAsync(foreignId);

        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await client.GetAsync($"/api/v1/service/subjects/{foreignId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound,
                "the tenant global query filter must hide another tenant's subject");
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

    private async Task<(Guid TenantId, Guid CustomerId)> SeedForeignTenantWithCustomerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

        var t = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TaxId == ForeignTaxId);
        if (t is null)
        {
            t = Tenant.Create("Subject Isolation Corp", ForeignTaxId, "admin@subj-iso.test");
            db.Tenants.Add(t);
            await db.SaveChangesAsync();
        }

        var cust = await db.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == t.Id);
        if (cust is null)
        {
            cust = Customer.Create(t.Id, PersonType.Individual, "Foreign Tutor",
                DocumentType.Cpf, Guid.NewGuid().ToString("N")[..11]);
            db.Customers.Add(cust);
            await db.SaveChangesAsync();
        }
        return (t.Id, cust.Id);
    }

    private async Task<Guid> SeedForeignSubjectAsync()
    {
        var (tenantId, customerId) = await SeedForeignTenantWithCustomerAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var subj = SvcSubject.Create(tenantId, customerId, SvcSubjectKind.Pet, "Foreign Rex");
        db.SvcSubjects.Add(subj);
        await db.SaveChangesAsync();
        return subj.Id;
    }

    private async Task AssertSubjectExistsInDbAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.SvcSubjects.IgnoreQueryFilters().AnyAsync(s => s.Id == id))
            .Should().BeTrue("seeding failed — test would be vacuously 404");
    }
}
