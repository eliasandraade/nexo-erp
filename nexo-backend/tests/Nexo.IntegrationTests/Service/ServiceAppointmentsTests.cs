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
/// End-to-end coverage for the Service agenda (PR3): create/get/list, status state machine,
/// per-professional overlap (409) + slot release on cancel, terminal guards, the active/subject
/// reference rules, payload validation, and tenant isolation. All appointment times are UTC.
/// </summary>
[Collection("Integration")]
public class ServiceAppointmentsTests
{
    private const string ForeignTaxId = "77666555000203";
    private static readonly DateTime Base = DateTime.UtcNow.Date.AddDays(3).AddHours(10); // UTC

    private readonly TestWebApplicationFactory _factory;
    public ServiceAppointmentsTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── Gate ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Appointments_forbidden_without_service_module()
    {
        var client = await AuthClientFactory.LoginAsync(_factory, "clara.boutique", "boutique@123");
        (await client.GetAsync("/api/v1/service/appointments")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Appointments_accessible_with_service_module()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await client.GetAsync("/api/v1/service/appointments")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Create / lifecycle ───────────────────────────────────────────────────
    [Fact]
    public async Task Create_then_get_and_drive_status_to_completed()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var ctx = await SetupAsync(c);
        var id = await CreateAppointmentAsync(c, ctx, Base, Base.AddHours(1));

        var got = await c.GetAsync($"/api/v1/service/appointments/{id}");
        got.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await got.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("status").GetString().Should().Be("Scheduled");
        dto.GetProperty("priceSnapshot").GetDecimal().Should().Be(120m);
        dto.GetProperty("storeId").GetGuid().Should().NotBe(Guid.Empty);

        (await Patch(c, id, "Confirmed")).Should().Be(HttpStatusCode.OK);
        (await Patch(c, id, "InProgress")).Should().Be(HttpStatusCode.OK);
        (await Patch(c, id, "Completed")).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Invalid_status_transition_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var ctx = await SetupAsync(c);
        var id = await CreateAppointmentAsync(c, ctx, Base.AddHours(2), Base.AddHours(3));
        (await Patch(c, id, "Completed")).Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Bad_status_string_returns_400()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var ctx = await SetupAsync(c);
        var id = await CreateAppointmentAsync(c, ctx, Base.AddHours(4), Base.AddHours(5));
        var r = await c.PatchAsJsonAsync($"/api/v1/service/appointments/{id}/status", new { status = "Teleported" });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Overlap_for_same_professional_is_rejected_409_then_cancel_releases_slot()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var ctx = await SetupAsync(c);
        var s = Base.AddDays(1); var e = s.AddHours(1);
        var first = await CreateAppointmentAsync(c, ctx, s, e);

        var overlap = await PostAppointment(c, ctx, s.AddMinutes(30), e.AddMinutes(30));
        overlap.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await Patch(c, first, "Cancelled", "freeing")).Should().Be(HttpStatusCode.OK);

        var retry = await PostAppointment(c, ctx, s.AddMinutes(30), e.AddMinutes(30));
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task NoShow_is_terminal_and_blocks_edit()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var ctx = await SetupAsync(c);
        var id = await CreateAppointmentAsync(c, ctx, Base.AddDays(2), Base.AddDays(2).AddHours(1));
        (await Patch(c, id, "NoShow")).Should().Be(HttpStatusCode.OK);

        var put = await c.PutAsJsonAsync($"/api/v1/service/appointments/{id}", new
        {
            customerId = ctx.CustomerId, professionalId = ctx.ProfessionalId, catalogItemId = ctx.CatalogItemId,
            startsAt = Base.AddDays(2).AddHours(5), endsAt = Base.AddDays(2).AddHours(6),
        });
        put.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Reschedule_changes_time()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var ctx = await SetupAsync(c);
        var id = await CreateAppointmentAsync(c, ctx, Base.AddDays(4), Base.AddDays(4).AddHours(1));
        var ns = Base.AddDays(4).AddHours(3); var ne = ns.AddHours(1);
        var put = await c.PutAsJsonAsync($"/api/v1/service/appointments/{id}", new
        {
            customerId = ctx.CustomerId, professionalId = ctx.ProfessionalId, catalogItemId = ctx.CatalogItemId,
            startsAt = ns, endsAt = ne,
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await put.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("startsAt").GetDateTime().ToUniversalTime().Should().Be(ns);
    }

    // ── Reference rules ──────────────────────────────────────────────────────
    [Fact]
    public async Task Inactive_professional_is_rejected_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var ctx = await SetupAsync(c);
        await c.PostAsync($"/api/v1/service/professionals/{ctx.ProfessionalId}/deactivate", null);
        var resp = await PostAppointment(c, ctx, Base.AddDays(5), Base.AddDays(5).AddHours(1));
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Inactive_catalog_item_is_rejected_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var ctx = await SetupAsync(c);
        await c.PostAsync($"/api/v1/service/catalog/{ctx.CatalogItemId}/deactivate", null);
        var resp = await PostAppointment(c, ctx, Base.AddDays(6), Base.AddDays(6).AddHours(1));
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task RequiresSubject_without_subject_is_rejected_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var professionalId = await CreateProfessionalAsync(c);
        var catalogItemId = await CreateCatalogAsync(c, requiresSubject: true);
        var resp = await c.PostAsJsonAsync("/api/v1/service/appointments", new
        {
            customerId, professionalId, catalogItemId,
            startsAt = Base.AddDays(7), endsAt = Base.AddDays(7).AddHours(1),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Subject_belonging_to_another_customer_is_rejected_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var c1 = await CreateCustomerAsync(c);
        var c2 = await CreateCustomerAsync(c);
        var professionalId = await CreateProfessionalAsync(c);
        var catalogItemId = await CreateCatalogAsync(c, requiresSubject: true);
        var subjectOfC2 = await CreateSubjectAsync(c, c2);
        var resp = await c.PostAsJsonAsync("/api/v1/service/appointments", new
        {
            customerId = c1, professionalId, catalogItemId, subjectId = subjectOfC2,
            startsAt = Base.AddDays(8), endsAt = Base.AddDays(8).AddHours(1),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task RequiresSubject_with_matching_subject_succeeds()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var professionalId = await CreateProfessionalAsync(c);
        var catalogItemId = await CreateCatalogAsync(c, requiresSubject: true);
        var subjectId = await CreateSubjectAsync(c, customerId);
        var resp = await c.PostAsJsonAsync("/api/v1/service/appointments", new
        {
            customerId, professionalId, catalogItemId, subjectId,
            startsAt = Base.AddDays(10), endsAt = Base.AddDays(10).AddHours(1),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Invalid_payload_starts_after_ends_returns_400()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var ctx = await SetupAsync(c);
        var resp = await c.PostAsJsonAsync("/api/v1/service/appointments", new
        {
            customerId = ctx.CustomerId, professionalId = ctx.ProfessionalId, catalogItemId = ctx.CatalogItemId,
            startsAt = Base.AddDays(9).AddHours(2), endsAt = Base.AddDays(9),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_professional_returns_404()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var catalogItemId = await CreateCatalogAsync(c, false);
        var resp = await c.PostAsJsonAsync("/api/v1/service/appointments", new
        {
            customerId, professionalId = Guid.NewGuid(), catalogItemId,
            startsAt = Base.AddDays(11), endsAt = Base.AddDays(11).AddHours(1),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Appointment_from_another_tenant_is_not_accessible()
    {
        var foreignId = await SeedForeignAppointmentAsync();
        await AssertAppointmentExistsInDbAsync(foreignId);
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.GetAsync($"/api/v1/service/appointments/{foreignId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private sealed record Ctx(Guid CustomerId, Guid ProfessionalId, Guid CatalogItemId);

    private static async Task<Ctx> SetupAsync(HttpClient c)
        => new(await CreateCustomerAsync(c), await CreateProfessionalAsync(c), await CreateCatalogAsync(c, false));

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

    private static async Task<Guid> CreateCatalogAsync(HttpClient c, bool requiresSubject)
    {
        var r = await c.PostAsJsonAsync("/api/v1/service/catalog", new
        {
            name = "Svc " + Guid.NewGuid().ToString("N")[..6], durationMinutes = 60, price = 120m, requiresSubject,
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

    private static Task<HttpResponseMessage> PostAppointment(HttpClient c, Ctx ctx, DateTime s, DateTime e)
        => c.PostAsJsonAsync("/api/v1/service/appointments", new
        {
            customerId = ctx.CustomerId, professionalId = ctx.ProfessionalId, catalogItemId = ctx.CatalogItemId,
            startsAt = s, endsAt = e,
        });

    private static async Task<Guid> CreateAppointmentAsync(HttpClient c, Ctx ctx, DateTime s, DateTime e)
    {
        var r = await PostAppointment(c, ctx, s, e);
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<HttpStatusCode> Patch(HttpClient c, Guid id, string status, string? reason = null)
    {
        var r = await c.PatchAsJsonAsync($"/api/v1/service/appointments/{id}/status", new { status, reason });
        return r.StatusCode;
    }

    private async Task<Guid> SeedForeignAppointmentAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

        var t = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TaxId == ForeignTaxId);
        Guid storeId;
        if (t is null)
        {
            t = Tenant.Create("Appt Isolation Corp", ForeignTaxId, "admin@appt-iso.test");
            db.Tenants.Add(t);
            var store = Store.Create(t.Id, "AIC Store", "appt-iso-default");
            db.Stores.Add(store);
            await db.SaveChangesAsync();
            storeId = store.Id;
        }
        else storeId = await db.Stores.IgnoreQueryFilters().Where(s => s.TenantId == t.Id).Select(s => s.Id).FirstAsync();

        var cust = Customer.Create(t.Id, PersonType.Individual, "Foreign", DocumentType.Cpf, Guid.NewGuid().ToString("N")[..11]);
        db.Customers.Add(cust);
        var prof = SvcProfessional.Create(t.Id, "Foreign Pro");
        db.SvcProfessionals.Add(prof);
        db.Entry(prof).Property("StoreId").CurrentValue = storeId;
        var item = SvcCatalogItem.Create(t.Id, "Foreign Svc", 60, 100m);
        db.SvcCatalogItems.Add(item);
        db.Entry(item).Property("StoreId").CurrentValue = storeId;
        await db.SaveChangesAsync();

        var appt = SvcAppointment.Create(t.Id, cust.Id, prof.Id, item.Id, null,
            DateTime.UtcNow.AddDays(20), DateTime.UtcNow.AddDays(20).AddHours(1), 100m);
        db.SvcAppointments.Add(appt);
        db.Entry(appt).Property("StoreId").CurrentValue = storeId;
        await db.SaveChangesAsync();
        return appt.Id;
    }

    private async Task AssertAppointmentExistsInDbAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.SvcAppointments.IgnoreQueryFilters().AnyAsync(a => a.Id == id))
            .Should().BeTrue("seeding failed — test would be vacuously 404");
    }
}
