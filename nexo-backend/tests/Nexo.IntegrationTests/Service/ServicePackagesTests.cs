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

/// <summary>End-to-end coverage for Service package templates (PR5): CRUD + items + isolation.</summary>
[Collection("Integration")]
public class ServicePackagesTests
{
    private const string ForeignTaxId = "77666555000205";

    private readonly TestWebApplicationFactory _factory;
    public ServicePackagesTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Packages_forbidden_without_service_module()
    {
        var client = await AuthClientFactory.LoginAsync(_factory, "clara.boutique", "boutique@123");
        (await client.GetAsync("/api/v1/service/packages")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Packages_accessible_with_service_module()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await client.GetAsync("/api/v1/service/packages")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Package_create_get_update_activate_deactivate()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var createResp = await c.PostAsJsonAsync("/api/v1/service/packages",
            new { name = "Plano Pet", description = "banho+tosa", price = 500m, validityDays = 30 });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("isActive").GetBoolean().Should().BeTrue();
        created.GetProperty("validityDays").GetInt32().Should().Be(30);

        (await c.GetAsync($"/api/v1/service/packages/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var upd = await c.PutAsJsonAsync($"/api/v1/service/packages/{id}",
            new { name = "Plano Pet+", description = "premium", validityDays = 60 });
        upd.StatusCode.Should().Be(HttpStatusCode.OK);
        (await upd.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString().Should().Be("Plano Pet+");

        (await c.PutAsJsonAsync($"/api/v1/service/packages/{id}/price", new { price = 600m })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await c.PostAsync($"/api/v1/service/packages/{id}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await c.GetAsync($"/api/v1/service/packages/{id}")).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("isActive").GetBoolean().Should().BeFalse();
        (await c.PostAsync($"/api/v1/service/packages/{id}/activate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Package_item_add_update_delete()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var packageId = await CreatePackageAsync(c);
        var catalogId = await CreateCatalogAsync(c, price: 50m, active: true);

        var add = await c.PostAsJsonAsync($"/api/v1/service/packages/{packageId}/items",
            new { catalogItemId = catalogId, includedQuantity = 4m });
        add.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await add.Content.ReadFromJsonAsync<JsonElement>();
        var item = dto.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("includedQuantity").GetDecimal().Should().Be(4m);
        item.GetProperty("nameSnapshot").GetString().Should().NotBeNullOrEmpty();
        var itemId = item.GetProperty("id").GetGuid();

        (await c.PutAsJsonAsync($"/api/v1/service/packages/{packageId}/items/{itemId}", new { includedQuantity = 8m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.DeleteAsync($"/api/v1/service/packages/{packageId}/items/{itemId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Package_item_with_inactive_catalog_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var packageId = await CreatePackageAsync(c);
        var catalogId = await CreateCatalogAsync(c, price: 50m, active: false);
        (await c.PostAsJsonAsync($"/api/v1/service/packages/{packageId}/items",
            new { catalogItemId = catalogId, includedQuantity = 1m }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Duplicate_catalog_item_in_package_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var packageId = await CreatePackageAsync(c);
        var catalogId = await CreateCatalogAsync(c, price: 50m, active: true);
        (await c.PostAsJsonAsync($"/api/v1/service/packages/{packageId}/items", new { catalogItemId = catalogId, includedQuantity = 1m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.PostAsJsonAsync($"/api/v1/service/packages/{packageId}/items", new { catalogItemId = catalogId, includedQuantity = 2m }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Package_create_blank_name_returns_400()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.PostAsJsonAsync("/api/v1/service/packages", new { name = "", price = 1m }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Package_from_another_tenant_is_not_accessible()
    {
        var foreignId = await SeedForeignPackageAsync();
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.GetAsync($"/api/v1/service/packages/{foreignId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static async Task<Guid> CreatePackageAsync(HttpClient c)
    {
        var r = await c.PostAsJsonAsync("/api/v1/service/packages",
            new { name = "Pkg " + Guid.NewGuid().ToString("N")[..6], price = 100m, validityDays = 30 });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateCatalogAsync(HttpClient c, decimal price, bool active)
    {
        var r = await c.PostAsJsonAsync("/api/v1/service/catalog",
            new { name = "Svc " + Guid.NewGuid().ToString("N")[..6], durationMinutes = 60, price, requiresSubject = false });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        if (!active) await c.PostAsync($"/api/v1/service/catalog/{id}/deactivate", null);
        return id;
    }

    private async Task<Guid> SeedForeignPackageAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var t = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TaxId == ForeignTaxId);
        Guid storeId;
        if (t is null)
        {
            t = Tenant.Create("Pkg Isolation Corp", ForeignTaxId, "admin@pkg-iso.test");
            db.Tenants.Add(t);
            var store = Store.Create(t.Id, "PIC Store", "pkg-iso-default");
            db.Stores.Add(store);
            await db.SaveChangesAsync();
            storeId = store.Id;
        }
        else storeId = await db.Stores.IgnoreQueryFilters().Where(s => s.TenantId == t.Id).Select(s => s.Id).FirstAsync();

        var pkg = SvcPackage.Create(t.Id, "Foreign Pkg", 100m, null, 30);
        db.SvcPackages.Add(pkg);
        db.Entry(pkg).Property("StoreId").CurrentValue = storeId;
        await db.SaveChangesAsync();
        return pkg.Id;
    }
}
