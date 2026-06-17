using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Service;

/// <summary>
/// End-to-end coverage for the Service engine foundation (PR1): professionals + catalog.
/// Exercises auth → family module gate → controller → service → repo → DB, including
/// tenant isolation. The default dev tenant holds the 'salao-beleza' service SKU (seeded);
/// 'clara.boutique' (varejo only) is the no-service negative case.
/// </summary>
[Collection("Integration")]
public class ServiceFoundationTests
{
    private readonly TestWebApplicationFactory _factory;

    public ServiceFoundationTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── Module gate ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Professionals_are_forbidden_for_a_tenant_without_a_service_module()
    {
        var client = await AuthClientFactory.LoginAsync(_factory, "clara.boutique", "boutique@123");
        var resp = await client.GetAsync("/api/v1/service/professionals");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Catalog_is_forbidden_for_a_tenant_without_a_service_module()
    {
        var client = await AuthClientFactory.LoginAsync(_factory, "clara.boutique", "boutique@123");
        var resp = await client.GetAsync("/api/v1/service/catalog");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Professionals_are_accessible_for_a_service_tenant()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var resp = await client.GetAsync("/api/v1/service/professionals");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Professional lifecycle ──────────────────────────────────────────────────

    [Fact]
    public async Task Professional_create_get_update_deactivate_activate()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);

        // Create
        var createResp = await client.PostAsJsonAsync("/api/v1/service/professionals", new
        {
            name = "Ana Paula",
            role = "Cabeleireira",
            defaultCommissionPercent = 20m,
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("name").GetString().Should().Be("Ana Paula");
        created.GetProperty("isActive").GetBoolean().Should().BeTrue();
        created.GetProperty("storeId").GetGuid().Should().NotBe(Guid.Empty);

        // Get by id
        var getResp = await client.GetAsync($"/api/v1/service/professionals/{id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Update (details + commission in one PUT)
        var updateResp = await client.PutAsJsonAsync($"/api/v1/service/professionals/{id}", new
        {
            name = "Ana P. Souza",
            role = "Manicure",
            specialty = "Unhas",
            defaultCommissionPercent = 35m,
        });
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("name").GetString().Should().Be("Ana P. Souza");
        updated.GetProperty("role").GetString().Should().Be("Manicure");
        updated.GetProperty("defaultCommissionPercent").GetDecimal().Should().Be(35m);

        // Deactivate
        var deact = await client.PostAsync($"/api/v1/service/professionals/{id}/deactivate", null);
        deact.StatusCode.Should().Be(HttpStatusCode.OK);
        (await deact.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("isActive").GetBoolean().Should().BeFalse();

        // Activate
        var act = await client.PostAsync($"/api/v1/service/professionals/{id}/activate", null);
        act.StatusCode.Should().Be(HttpStatusCode.OK);
        (await act.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("isActive").GetBoolean().Should().BeTrue();

        // List contains it
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/service/professionals");
        list.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).Should().Contain(id);
    }

    [Fact]
    public async Task Professional_create_with_blank_name_returns_400()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var resp = await client.PostAsJsonAsync("/api/v1/service/professionals", new { name = "" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Catalog lifecycle ───────────────────────────────────────────────────────

    [Fact]
    public async Task Catalog_create_update_deactivate_activate()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);

        var createResp = await client.PostAsJsonAsync("/api/v1/service/catalog", new
        {
            name = "Corte de cabelo",
            durationMinutes = 30,
            price = 50m,
            category = "Cabelo",
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("durationMinutes").GetInt32().Should().Be(30);
        created.GetProperty("price").GetDecimal().Should().Be(50m);
        created.GetProperty("requiresSubject").GetBoolean().Should().BeFalse();
        created.GetProperty("isActive").GetBoolean().Should().BeTrue();

        var updateResp = await client.PutAsJsonAsync($"/api/v1/service/catalog/{id}", new
        {
            name = "Corte + escova",
            durationMinutes = 45,
            price = 75m,
            requiresSubject = false,
        });
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("durationMinutes").GetInt32().Should().Be(45);
        updated.GetProperty("price").GetDecimal().Should().Be(75m);

        var deact = await client.PostAsync($"/api/v1/service/catalog/{id}/deactivate", null);
        deact.StatusCode.Should().Be(HttpStatusCode.OK);
        (await deact.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("isActive").GetBoolean().Should().BeFalse();

        var act = await client.PostAsync($"/api/v1/service/catalog/{id}/activate", null);
        act.StatusCode.Should().Be(HttpStatusCode.OK);
        (await act.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 50)]    // duration must be > 0
    [InlineData(30, -1)]   // price must be >= 0
    public async Task Catalog_create_with_invalid_payload_returns_400(int durationMinutes, decimal price)
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var resp = await client.PostAsJsonAsync("/api/v1/service/catalog", new
        {
            name = "Inválido",
            durationMinutes,
            price,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Tenant isolation ────────────────────────────────────────────────────────

    [Fact]
    public async Task Professional_from_another_tenant_is_not_accessible()
    {
        var foreignId = await SeedForeignTenantProfessionalAsync();

        // Positive control: it really exists in the DB (so a 404 means "filtered", not "absent").
        await AssertProfessionalExistsInDbAsync(foreignId);

        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var resp = await client.GetAsync($"/api/v1/service/professionals/{foreignId}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the tenant/store global query filter must hide another tenant's professional");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<Guid> SeedForeignTenantProfessionalAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

        const string taxId = "77666555000133";
        var tenantB = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TaxId == taxId);
        Guid storeBId;

        if (tenantB is null)
        {
            tenantB = Tenant.Create("Service Isolation Corp", taxId, "admin@svc-iso.test");
            db.Tenants.Add(tenantB);
            var store = Store.Create(tenantB.Id, "SIC Store", "svc-iso-default");
            db.Stores.Add(store);
            await db.SaveChangesAsync();
            storeBId = store.Id;
        }
        else
        {
            storeBId = await db.Stores.IgnoreQueryFilters()
                .Where(s => s.TenantId == tenantB.Id).Select(s => s.Id).FirstAsync();
        }

        // StoreEntity: no HTTP context here, so the interceptor doesn't auto-inject StoreId —
        // assign it explicitly (same pattern as the Product isolation test).
        var prof = SvcProfessional.Create(tenantB.Id, $"Foreign Pro {Guid.NewGuid():N}"[..20]);
        db.SvcProfessionals.Add(prof);
        db.Entry(prof).Property("StoreId").CurrentValue = storeBId;
        await db.SaveChangesAsync();
        return prof.Id;
    }

    private async Task AssertProfessionalExistsInDbAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var exists = await db.SvcProfessionals.IgnoreQueryFilters().AnyAsync(p => p.Id == id);
        exists.Should().BeTrue("seeding failed — professional does not exist, test would be vacuously 404");
    }
}
