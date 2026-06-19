using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Service;

/// <summary>
/// E2E coverage for the public Service booking portal (PR12 — no auth):
///   slug resolution, disabled-portal guard, active-only catalog/professionals, real availability
///   (no overlap, duration-aware, "indisponível" without hours), customer create/reuse, subject
///   creation when the preset requires it, overlap/inactive/non-UTC guards, and the security
///   guarantees (no payment/OS created, no tenant/store/internal leak).
///
/// The suite shares one seeded database; the admin tenant owns the single "service" store with the
/// seeded preset salao-beleza. Each test configures the store it needs at the start, so order does
/// not matter. The pet-shop test restores the seeded preset in a finally block.
/// </summary>
[Collection("Integration")]
public class ServicePublicPortalTests
{
    private readonly TestWebApplicationFactory _factory;
    private static int _seq;

    public ServicePublicPortalTests(TestWebApplicationFactory factory) => _factory = factory;

    private const string AllWeek0622 =
        "[{\"weekday\":0,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]}," +
        "{\"weekday\":1,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]}," +
        "{\"weekday\":2,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]}," +
        "{\"weekday\":3,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]}," +
        "{\"weekday\":4,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]}," +
        "{\"weekday\":5,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]}," +
        "{\"weekday\":6,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]}]";

    // ── 1. Slug resolution ────────────────────────────────────────────────────

    [Fact]
    public async Task Portal_resolves_store_by_slug()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var slug = await ConfigureStoreAsync(admin, bookingEnabled: true);

        var pub = _factory.CreateApiClient();
        var r = await pub.GetAsync($"/api/public/service/{slug}");
        r.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("storeName").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("presetKey").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("isBookingEnabled").GetBoolean().Should().BeTrue();
        body.GetProperty("requiresProfessionalSelection").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_slug_returns_404()
    {
        var pub = _factory.CreateApiClient();
        (await pub.GetAsync("/api/public/service/nao-existe-xyz-987")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    // ── 2. Disabled portal ────────────────────────────────────────────────────

    [Fact]
    public async Task Disabled_portal_blocks_booking_endpoints_with_403()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var slug = await ConfigureStoreAsync(admin, bookingEnabled: false);

        var pub = _factory.CreateApiClient();

        // Header still resolves (200) but reports booking off.
        var header = await pub.GetAsync($"/api/public/service/{slug}");
        header.StatusCode.Should().Be(HttpStatusCode.OK);
        (await header.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("isBookingEnabled").GetBoolean().Should().BeFalse();

        // Booking surfaces are forbidden.
        (await pub.GetAsync($"/api/public/service/{slug}/catalog")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await pub.GetAsync($"/api/public/service/{slug}/professionals")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    // ── 3 & 4. Active-only catalog and professionals ──────────────────────────

    [Fact]
    public async Task Catalog_returns_only_active_items()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var slug = await ConfigureStoreAsync(admin, bookingEnabled: true);
        var active = await CreateCatalogAsync(admin);
        var inactive = await CreateCatalogAsync(admin);
        await admin.PostAsync($"/api/v1/service/catalog/{inactive}/deactivate", null);

        var pub = _factory.CreateApiClient();
        var items = await pub.GetFromJsonAsync<List<JsonElement>>($"/api/public/service/{slug}/catalog");
        var ids = items!.Select(i => i.GetProperty("id").GetGuid()).ToList();

        ids.Should().Contain(active);
        ids.Should().NotContain(inactive);
    }

    [Fact]
    public async Task Professionals_returns_only_active()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var slug = await ConfigureStoreAsync(admin, bookingEnabled: true);
        var active = await CreateProfessionalAsync(admin, AllWeek0622);
        var inactive = await CreateProfessionalAsync(admin, AllWeek0622);
        await admin.PostAsync($"/api/v1/service/professionals/{inactive}/deactivate", null);

        var pub = _factory.CreateApiClient();
        var pros = await pub.GetFromJsonAsync<List<JsonElement>>($"/api/public/service/{slug}/professionals");
        var ids = pros!.Select(p => p.GetProperty("id").GetGuid()).ToList();

        ids.Should().Contain(active);
        ids.Should().NotContain(inactive);
    }

    // ── 5 & 6. Availability ───────────────────────────────────────────────────

    [Fact]
    public async Task Availability_returns_duration_sized_slots_and_excludes_booked_times()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var slug = await ConfigureStoreAsync(admin, bookingEnabled: true);
        var professional = await CreateProfessionalAsync(admin, AllWeek0622);
        var catalog = await CreateCatalogAsync(admin, durationMinutes: 60);

        var pub = _factory.CreateApiClient();
        var before = await GetAvailabilityAsync(pub, slug, catalog, professional);
        before.Slots.Should().NotBeEmpty();
        before.Slots.Should().OnlyContain(s => (s.EndsAt - s.StartsAt).TotalMinutes == 60);

        // Pick a slot at least a day out to avoid lead/now races, book it.
        var pick = before.Slots.First(s => s.StartsAt > DateTime.UtcNow.AddDays(1));
        var book = await BookAsync(pub, slug, catalog, professional, pick.Raw);
        book.StatusCode.Should().Be(HttpStatusCode.Created);

        var after = await GetAvailabilityAsync(pub, slug, catalog, professional);
        // Check slot STARTS (a booked start equals the previous slot's end, so a raw-substring
        // check would false-positive on that adjacent endsAt).
        after.Slots.Select(s => s.Raw).Should()
            .NotContain(pick.Raw, "a booked slot must disappear from availability");
    }

    [Fact]
    public async Task Availability_is_empty_when_professional_has_no_working_hours()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var slug = await ConfigureStoreAsync(admin, bookingEnabled: true);
        var professional = await CreateProfessionalAsync(admin, workingHoursJson: null);
        var catalog = await CreateCatalogAsync(admin);

        var pub = _factory.CreateApiClient();
        var avail = await GetAvailabilityAsync(pub, slug, catalog, professional);
        avail.Slots.Should().BeEmpty("no working hours ⇒ indisponível, never a fabricated slot");
    }

    // ── 7 & 8. Customer create / reuse ────────────────────────────────────────

    [Fact]
    public async Task Booking_creates_a_customer()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var slug = await ConfigureStoreAsync(admin, bookingEnabled: true);
        var professional = await CreateProfessionalAsync(admin, AllWeek0622);
        var catalog = await CreateCatalogAsync(admin);
        var phone = NextPhone();

        var pub = _factory.CreateApiClient();
        var slot = await PickSlotAsync(pub, slug, catalog, professional);
        var r = await BookAsync(pub, slug, catalog, professional, slot, phone: phone, name: "Maria Cliente");
        r.StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.Customers.IgnoreQueryFilters().CountAsync(c => c.Phone == phone))
            .Should().Be(1);
    }

    [Fact]
    public async Task Booking_reuses_an_existing_customer_by_phone()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var slug = await ConfigureStoreAsync(admin, bookingEnabled: true);
        var professional = await CreateProfessionalAsync(admin, AllWeek0622);
        var catalog = await CreateCatalogAsync(admin);
        var phone = NextPhone();
        var pub = _factory.CreateApiClient();

        var avail = await GetAvailabilityAsync(pub, slug, catalog, professional);
        var twoSlots = avail.Slots.Where(s => s.StartsAt > DateTime.UtcNow.AddDays(1)).Take(2).ToList();
        twoSlots.Should().HaveCount(2);

        (await BookAsync(pub, slug, catalog, professional, twoSlots[0].Raw, phone: phone))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await BookAsync(pub, slug, catalog, professional, twoSlots[1].Raw, phone: phone))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.Customers.IgnoreQueryFilters().CountAsync(c => c.Phone == phone))
            .Should().Be(1, "two bookings from the same phone must reuse one customer");
    }

    // ── 9. Subject when the preset requires it ────────────────────────────────

    [Fact]
    public async Task Pet_shop_preset_requires_and_creates_a_subject()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var slug = await ConfigureStoreAsync(admin, bookingEnabled: true);
        await SetPresetAsync(admin, "pet-shop");
        try
        {
            var professional = await CreateProfessionalAsync(admin, AllWeek0622);
            var catalog = await CreateCatalogAsync(admin);
            var pub = _factory.CreateApiClient();
            var slot = await PickSlotAsync(pub, slug, catalog, professional);

            // Without a subject → 422.
            var noSubject = await BookAsync(pub, slug, catalog, professional, slot);
            noSubject.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

            // With a subject → 201 and a subject row is created.
            var phone = NextPhone();
            var withSubject = await pub.PostAsJsonAsync(
                $"/api/public/service/{slug}/appointments", new
                {
                    customerName = "Tutor Teste",
                    phone,
                    catalogItemId = catalog,
                    professionalId = professional,
                    startsAt = slot,
                    subject = new { displayName = "Rex", kind = "Pet", notes = "Banho e tosa" },
                });
            withSubject.StatusCode.Should().Be(HttpStatusCode.Created);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var customer = await db.Customers.IgnoreQueryFilters().FirstAsync(c => c.Phone == phone);
            var subject = await db.SvcSubjects.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.CustomerId == customer.Id && s.DisplayName == "Rex");
            subject.Should().NotBeNull();
            (await db.SvcAppointments.IgnoreQueryFilters()
                .AnyAsync(a => a.SubjectId == subject!.Id)).Should().BeTrue();
        }
        finally
        {
            await SetPresetAsync(admin, "salao-beleza");
        }
    }

    // ── 10–13. Guards ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Overlapping_booking_is_rejected_409()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var slug = await ConfigureStoreAsync(admin, bookingEnabled: true);
        var professional = await CreateProfessionalAsync(admin, AllWeek0622);
        var catalog = await CreateCatalogAsync(admin);
        var pub = _factory.CreateApiClient();
        var slot = await PickSlotAsync(pub, slug, catalog, professional);

        (await BookAsync(pub, slug, catalog, professional, slot)).StatusCode
            .Should().Be(HttpStatusCode.Created);
        (await BookAsync(pub, slug, catalog, professional, slot)).StatusCode
            .Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Inactive_service_is_rejected()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var slug = await ConfigureStoreAsync(admin, bookingEnabled: true);
        var professional = await CreateProfessionalAsync(admin, AllWeek0622);
        var catalog = await CreateCatalogAsync(admin);
        var pub = _factory.CreateApiClient();
        var slot = await PickSlotAsync(pub, slug, catalog, professional);
        await admin.PostAsync($"/api/v1/service/catalog/{catalog}/deactivate", null);

        (await BookAsync(pub, slug, catalog, professional, slot)).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Inactive_professional_is_rejected()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var slug = await ConfigureStoreAsync(admin, bookingEnabled: true);
        var professional = await CreateProfessionalAsync(admin, AllWeek0622);
        var catalog = await CreateCatalogAsync(admin);
        var pub = _factory.CreateApiClient();
        var slot = await PickSlotAsync(pub, slug, catalog, professional);
        await admin.PostAsync($"/api/v1/service/professionals/{professional}/deactivate", null);

        (await BookAsync(pub, slug, catalog, professional, slot)).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Non_utc_starts_at_is_rejected()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var slug = await ConfigureStoreAsync(admin, bookingEnabled: true);
        var professional = await CreateProfessionalAsync(admin, AllWeek0622);
        var catalog = await CreateCatalogAsync(admin);
        var pub = _factory.CreateApiClient();

        // A local/unspecified time (no trailing Z) must be rejected.
        var local = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(2).Date.AddHours(10), DateTimeKind.Unspecified);
        var r = await pub.PostAsJsonAsync($"/api/public/service/{slug}/appointments", new
        {
            customerName = "Sem Z",
            phone = NextPhone(),
            catalogItemId = catalog,
            professionalId = professional,
            startsAt = local,
        });
        r.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
    }

    // ── 14–16. Security ───────────────────────────────────────────────────────

    [Fact]
    public async Task Booking_creates_no_payment_and_no_order()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var slug = await ConfigureStoreAsync(admin, bookingEnabled: true);
        var professional = await CreateProfessionalAsync(admin, AllWeek0622);
        var catalog = await CreateCatalogAsync(admin);
        var phone = NextPhone();
        var pub = _factory.CreateApiClient();
        var slot = await PickSlotAsync(pub, slug, catalog, professional);

        (await BookAsync(pub, slug, catalog, professional, slot, phone: phone)).StatusCode
            .Should().Be(HttpStatusCode.Created);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var customer = await db.Customers.IgnoreQueryFilters().FirstAsync(c => c.Phone == phone);
        (await db.SvcOrders.IgnoreQueryFilters().AnyAsync(o => o.CustomerId == customer.Id))
            .Should().BeFalse("a public booking must not create an OS");
        (await db.SvcPayments.IgnoreQueryFilters().AnyAsync(p => p.CustomerId == customer.Id))
            .Should().BeFalse("a public booking must not create a payment");
    }

    [Fact]
    public async Task Public_payloads_do_not_leak_internal_fields()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var slug = await ConfigureStoreAsync(admin, bookingEnabled: true);
        var professional = await CreateProfessionalAsync(admin, AllWeek0622);
        var catalog = await CreateCatalogAsync(admin);
        var pub = _factory.CreateApiClient();

        var header = await pub.GetStringAsync($"/api/public/service/{slug}");
        var catalogJson = await pub.GetStringAsync($"/api/public/service/{slug}/catalog");
        var prosJson = await pub.GetStringAsync($"/api/public/service/{slug}/professionals");

        // Note: the header legitimately carries a boolean capability flag "commissions" (whether the
        // vertical uses commissions at all) — that is not a leak. We assert the value-bearing internal
        // field names never appear.
        foreach (var json in new[] { header, catalogJson, prosJson })
            json.Should().NotContainAny(
                "tenantId", "TenantId", "storeId", "StoreId",
                "commissionPercent", "defaultCommissionPercent", "costPrice", "createdBy");
    }

    // ── 17. Branding (PR16) ────────────────────────────────────────────────────

    [Fact]
    public async Task Branding_set_by_admin_appears_in_public_portal()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var slug = await ConfigureStoreAsync(admin, bookingEnabled: true);

        var put = await admin.PutAsJsonAsync("/api/v1/service/settings/branding", new
        {
            displayName   = "Studio Belle",
            description   = "Cuidado de verdade",
            logoUrl       = "https://x/logo.png",
            coverImageUrl = (string?)null,
            brandColor    = "#A8743F",
            whatsApp      = "(85) 99999-8888",
            address       = "Rua X, 10",
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var pub = _factory.CreateApiClient();
        var portal = await pub.GetFromJsonAsync<JsonElement>($"/api/public/service/{slug}");
        portal.GetProperty("displayName").GetString().Should().Be("Studio Belle");
        portal.GetProperty("brandColor").GetString().Should().Be("#a8743f");   // normalized
        portal.GetProperty("logoUrl").GetString().Should().Be("https://x/logo.png");
        portal.GetProperty("whatsApp").GetString().Should().Be("85999998888"); // digits only

        // Booking config and branding live on the same row but on separate endpoints — saving the
        // booking config must NOT wipe the branding.
        var booking = await admin.PutAsJsonAsync("/api/v1/service/settings/public-booking", new
        {
            publicBookingEnabled = true, bookingDaysAhead = 14, minLeadMinutes = 30,
            slotIntervalMinutes = 60, showPrices = true, autoConfirmAppointments = false, timeZoneId = "UTC",
        });
        booking.StatusCode.Should().Be(HttpStatusCode.OK);
        (await booking.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("displayName").GetString().Should().Be("Studio Belle");
    }

    [Fact]
    public async Task Branding_invalid_color_is_rejected()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        await ConfigureStoreAsync(admin, bookingEnabled: true);

        var put = await admin.PutAsJsonAsync("/api/v1/service/settings/branding", new { brandColor = "red" });
        put.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> ConfigureStoreAsync(HttpClient admin, bool bookingEnabled)
    {
        var stores = await admin.GetFromJsonAsync<List<JsonElement>>("/api/stores");
        var storeId = stores![0].GetProperty("id").GetGuid();
        var slug = $"svc-portal-{Interlocked.Increment(ref _seq)}-{Guid.NewGuid():N}".Substring(0, 28);

        (await admin.PatchAsJsonAsync($"/api/stores/{storeId}/public-slug", new { publicSlug = slug }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var put = await admin.PutAsJsonAsync("/api/v1/service/settings/public-booking", new
        {
            publicBookingEnabled = bookingEnabled,
            bookingDaysAhead = 14,
            minLeadMinutes = 30,
            slotIntervalMinutes = 60,
            showPrices = true,
            autoConfirmAppointments = false,
            timeZoneId = "UTC",
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        return slug;
    }

    private static async Task SetPresetAsync(HttpClient admin, string presetKey)
        => (await admin.PutAsJsonAsync("/api/v1/service/settings/preset", new { presetKey }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

    private static async Task<Guid> CreateProfessionalAsync(HttpClient admin, string? workingHoursJson)
    {
        var r = await admin.PostAsJsonAsync("/api/v1/service/professionals", new
        {
            name = "Pro " + Guid.NewGuid().ToString("N")[..6],
            workingHoursJson,
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateCatalogAsync(HttpClient admin, int durationMinutes = 60)
    {
        var r = await admin.PostAsJsonAsync("/api/v1/service/catalog", new
        {
            name = "Svc " + Guid.NewGuid().ToString("N")[..6],
            durationMinutes,
            price = 80m,
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private sealed record Slot(string Raw, DateTime StartsAt, DateTime EndsAt);
    private sealed record Availability(IReadOnlyList<Slot> Slots, string Raw);

    private static async Task<Availability> GetAvailabilityAsync(
        HttpClient pub, string slug, Guid catalog, Guid professional)
    {
        var url = $"/api/public/service/{slug}/availability?catalogItemId={catalog}&professionalId={professional}";
        var raw = await pub.GetStringAsync(url);
        using var doc = JsonDocument.Parse(raw);
        var slots = doc.RootElement.GetProperty("slots").EnumerateArray()
            .Select(s => new Slot(
                s.GetProperty("startsAt").GetString()!,
                s.GetProperty("startsAt").GetDateTime().ToUniversalTime(),
                s.GetProperty("endsAt").GetDateTime().ToUniversalTime()))
            .ToList();
        return new Availability(slots, raw);
    }

    private static async Task<string> PickSlotAsync(HttpClient pub, string slug, Guid catalog, Guid professional)
    {
        var avail = await GetAvailabilityAsync(pub, slug, catalog, professional);
        return avail.Slots.First(s => s.StartsAt > DateTime.UtcNow.AddDays(1)).Raw;
    }

    private static Task<HttpResponseMessage> BookAsync(
        HttpClient pub, string slug, Guid catalog, Guid professional, string startsAtRaw,
        string? phone = null, string name = "Cliente Portal")
        => pub.PostAsJsonAsync($"/api/public/service/{slug}/appointments", new
        {
            customerName = name,
            phone = phone ?? NextPhone(),
            catalogItemId = catalog,
            professionalId = professional,
            startsAt = startsAtRaw,
        });

    private static string NextPhone() => $"1199{Interlocked.Increment(ref _seq):D7}";
}
