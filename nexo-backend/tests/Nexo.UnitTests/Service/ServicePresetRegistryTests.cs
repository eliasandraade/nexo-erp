using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

/// <summary>
/// Unit coverage for the Service preset registry — the single source of truth for the
/// "service" module family (decision D1) and preset resolution (Q5 deterministic fallback).
/// </summary>
public class ServicePresetRegistryTests
{
    [Fact]
    public void All_contains_the_ten_v1_presets()
    {
        ServicePresetRegistry.All.Should().HaveCount(10);
    }

    [Fact]
    public void Barbearia_preset_uses_barber_labels_and_enables_comanda()
    {
        var preset = ServicePresetRegistry.GetByKey("barbearia");

        preset.Should().NotBeNull();
        preset!.Labels.Professional.Should().Be("Barbeiro");
        preset.Labels.Order.Should().Be("Comanda");
        preset.Capabilities.Appointments.Should().BeTrue();
        preset.Capabilities.Orders.Should().BeTrue();
        preset.Capabilities.Packages.Should().BeTrue();
        preset.Capabilities.Commissions.Should().BeTrue();
    }

    [Fact]
    public void Capabilities_expose_only_surfaces_the_product_actually_has()
    {
        // A capability flag no screen reads is a lie told to the caller of GET /service/preset.
        var names = typeof(ServiceCapabilities)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name);

        names.Should().BeEquivalentTo(
            "Appointments", "Orders", "Packages", "Commissions", "SubjectKind");
    }

    [Fact]
    public void Presets_that_label_an_order_as_comanda_must_enable_orders()
    {
        // A label promising a "Comanda" with the Orders surface off is a dead promise:
        // the nav entry never renders and walk-in customers cannot be billed.
        foreach (var p in ServicePresetRegistry.All.Where(p => p.Labels.Order == "Comanda"))
            p.Capabilities.Orders.Should().BeTrue($"{p.Key} promete comanda no label");
    }

    [Fact]
    public void All_keys_are_unique_lowercase_and_have_no_spaces()
    {
        var keys = ServicePresetRegistry.All.Select(p => p.Key).ToList();
        keys.Should().OnlyHaveUniqueItems();
        keys.Should().OnlyContain(k => k == k.ToLowerInvariant() && !k.Contains(' '));
    }

    [Theory]
    [InlineData("clinica-medica")]
    [InlineData("salao-beleza")]
    [InlineData("pet-shop")]
    [InlineData("oficina-mecanica")]
    [InlineData("nutricionista")]
    [InlineData("personal-trainer")]
    [InlineData("autoescola")]
    [InlineData("escola-idiomas")]
    [InlineData("programador-autonomo")]
    public void IsLegacyVerticalKey_recognizes_every_retired_sku(string key)
    {
        ServicePresetRegistry.IsLegacyVerticalKey(key).Should().BeTrue();
    }

    [Theory]
    [InlineData("service")]   // the commercial module, not a retired SKU
    [InlineData("barbearia")] // a preset that was never sold as a SKU
    [InlineData("build")]
    [InlineData("varejo")]
    [InlineData("")]
    public void IsLegacyVerticalKey_is_false_for_everything_else(string key)
    {
        ServicePresetRegistry.IsLegacyVerticalKey(key).Should().BeFalse();
    }

    [Fact]
    public void IsLegacyVerticalKey_is_case_insensitive()
    {
        ServicePresetRegistry.IsLegacyVerticalKey("Salao-Beleza").Should().BeTrue();
    }

    [Fact]
    public void Every_preset_has_non_blank_display_name_and_labels()
    {
        foreach (var p in ServicePresetRegistry.All)
        {
            p.Key.Should().NotBeNullOrWhiteSpace();
            p.DisplayName.Should().NotBeNullOrWhiteSpace();
            p.Labels.Customer.Should().NotBeNullOrWhiteSpace();
            p.Labels.Professional.Should().NotBeNullOrWhiteSpace();
            p.Labels.CatalogItem.Should().NotBeNullOrWhiteSpace();
            p.Labels.Appointment.Should().NotBeNullOrWhiteSpace();
            p.Labels.Order.Should().NotBeNullOrWhiteSpace();
            p.Capabilities.Should().NotBeNull();
        }
    }

    [Fact]
    public void Priorities_are_unique_so_resolution_is_always_deterministic()
    {
        var priorities = ServicePresetRegistry.All.Select(p => p.Priority).ToList();
        priorities.Should().OnlyHaveUniqueItems();
    }

    // ── v1.1: single commercial module ("service") vs internal preset keys ────────

    [Theory]
    [InlineData("salao-beleza")]
    [InlineData("barbearia")]
    [InlineData("Pet-Shop")] // case-insensitive
    public void GetByKey_and_IsValidPresetKey_accept_a_vertical_preset(string key)
    {
        ServicePresetRegistry.GetByKey(key).Should().NotBeNull();
        ServicePresetRegistry.IsValidPresetKey(key).Should().BeTrue();
    }

    [Theory]
    [InlineData("service")] // the commercial module key is NOT a preset key
    [InlineData("varejo")]
    [InlineData("")]
    [InlineData(null)]
    public void GetByKey_and_IsValidPresetKey_reject_non_preset_keys(string? key)
    {
        ServicePresetRegistry.GetByKey(key).Should().BeNull();
        ServicePresetRegistry.IsValidPresetKey(key).Should().BeFalse();
    }

    [Theory]
    [InlineData("service")] // the only entitlement
    [InlineData("Service")] // case-insensitive
    public void IsServiceEntitlement_is_true_only_for_the_commercial_module(string key)
    {
        ServicePresetRegistry.IsServiceEntitlement(key).Should().BeTrue();
    }

    [Theory]
    [InlineData("varejo")]
    [InlineData("build")]
    [InlineData("")]
    public void IsServiceEntitlement_is_false_for_non_service_modules(string key)
    {
        ServicePresetRegistry.IsServiceEntitlement(key).Should().BeFalse();
    }

    [Theory]
    [InlineData("salao-beleza")]
    [InlineData("clinica-medica")]
    public void Retired_vertical_skus_no_longer_grant_access(string legacyKey)
    {
        // The ConvertLegacyServiceSubscriptions migration rewrites these to "service" before
        // the gate ever sees them. Accepting them here would keep the dead SKUs alive forever.
        ServicePresetRegistry.IsServiceEntitlement(legacyKey).Should().BeFalse();
    }
}
