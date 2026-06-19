using System;
using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

/// <summary>Domain rules for the public booking configuration carried on SvcSettings (PR12).</summary>
public class SvcSettingsPublicBookingTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Create_has_safe_booking_defaults_with_booking_off()
    {
        var s = SvcSettings.Create(Tenant, "salao-beleza");

        s.PublicBookingEnabled.Should().BeFalse();
        s.BookingDaysAhead.Should().Be(14);
        s.MinLeadMinutes.Should().Be(120);
        s.SlotIntervalMinutes.Should().Be(30);
        s.ShowPrices.Should().BeTrue();
        s.AutoConfirmAppointments.Should().BeFalse();
        s.TimeZoneId.Should().Be("America/Sao_Paulo");
    }

    [Fact]
    public void UpdatePublicBooking_sets_all_fields()
    {
        var s = SvcSettings.Create(Tenant, "pet-shop");

        s.UpdatePublicBooking(
            enabled: true, bookingDaysAhead: 30, minLeadMinutes: 60, slotIntervalMinutes: 15,
            showPrices: false, autoConfirmAppointments: true, timeZoneId: "UTC");

        s.PublicBookingEnabled.Should().BeTrue();
        s.BookingDaysAhead.Should().Be(30);
        s.MinLeadMinutes.Should().Be(60);
        s.SlotIntervalMinutes.Should().Be(15);
        s.ShowPrices.Should().BeFalse();
        s.AutoConfirmAppointments.Should().BeTrue();
        s.TimeZoneId.Should().Be("UTC");
    }

    [Theory]
    [InlineData(0, 30, "UTC")]      // days ahead too low
    [InlineData(400, 30, "UTC")]    // days ahead too high
    [InlineData(14, 0, "UTC")]      // slot interval too low
    [InlineData(14, 999, "UTC")]    // slot interval too high
    [InlineData(14, 30, "")]        // blank timezone
    public void UpdatePublicBooking_validates_ranges(int daysAhead, int slotInterval, string tz)
    {
        var s = SvcSettings.Create(Tenant, "clinica-medica");

        var act = () => s.UpdatePublicBooking(
            enabled: true, bookingDaysAhead: daysAhead, minLeadMinutes: 60,
            slotIntervalMinutes: slotInterval, showPrices: true,
            autoConfirmAppointments: false, timeZoneId: tz);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void CreateForStore_stamps_store_id_for_the_public_path()
    {
        var storeId = Guid.NewGuid();
        var appt = SvcAppointment.CreateForStore(
            Tenant, storeId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1), 100m);

        appt.StoreId.Should().Be(storeId);
        appt.Status.Should().Be(SvcAppointmentStatus.Scheduled);
    }

    // ── Branding (PR16) ──────────────────────────────────────────────────────────

    [Fact]
    public void UpdateBranding_trims_normalizes_and_lowercases_color()
    {
        var s = SvcSettings.Create(Tenant, "salao-beleza");
        s.UpdateBranding(" Studio Belle ", " Premium ", "https://x/logo.png", "https://x/cover.png",
            "#A8743F", "(85) 99999-8888", " Rua X, 10 ");

        s.DisplayName.Should().Be("Studio Belle");
        s.Description.Should().Be("Premium");
        s.LogoUrl.Should().Be("https://x/logo.png");
        s.CoverImageUrl.Should().Be("https://x/cover.png");
        s.BrandColor.Should().Be("#a8743f");
        s.WhatsApp.Should().Be("85999998888");
        s.Address.Should().Be("Rua X, 10");
    }

    [Fact]
    public void UpdateBranding_clears_with_blank_or_null()
    {
        var s = SvcSettings.Create(Tenant, "pet-shop");
        s.UpdateBranding("X", "Y", "u", "c", "#000000", "11", "addr");
        s.UpdateBranding(null, "  ", null, null, null, "", null);

        s.DisplayName.Should().BeNull();
        s.Description.Should().BeNull();
        s.LogoUrl.Should().BeNull();
        s.BrandColor.Should().BeNull();
        s.WhatsApp.Should().BeNull();
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#fff")]
    [InlineData("a8743f")]
    public void UpdateBranding_rejects_non_hex_color(string color)
    {
        var s = SvcSettings.Create(Tenant, "clinica-medica");
        var act = () => s.UpdateBranding(null, null, null, null, color, null, null);
        act.Should().Throw<DomainException>();
    }
}
