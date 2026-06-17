using System;
using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

/// <summary>Domain rules for the SvcCatalogItem aggregate (PR1 engine foundation).</summary>
public class SvcCatalogItemTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Create_sets_required_fields_and_defaults()
    {
        var c = SvcCatalogItem.Create(Tenant, "Corte de cabelo", durationMinutes: 30, price: 50m);

        c.TenantId.Should().Be(Tenant);
        c.Name.Should().Be("Corte de cabelo");
        c.DurationMinutes.Should().Be(30);
        c.Price.Should().Be(50m);
        c.IsActive.Should().BeTrue();
        c.RequiresSubject.Should().BeFalse();
        c.CommissionPercent.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_name(string name)
    {
        var act = () => SvcCatalogItem.Create(Tenant, name, 30, 50m);
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-15)]
    public void Create_rejects_non_positive_duration(int minutes)
    {
        var act = () => SvcCatalogItem.Create(Tenant, "X", minutes, 50m);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_rejects_negative_price()
    {
        var act = () => SvcCatalogItem.Create(Tenant, "X", 30, -1m);
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(101)]
    public void Create_rejects_commission_out_of_range(decimal pct)
    {
        var act = () => SvcCatalogItem.Create(Tenant, "X", 30, 50m, commissionPercent: pct);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_accepts_zero_price_and_subject_flag_and_trims_name()
    {
        var c = SvcCatalogItem.Create(Tenant, "  Avaliação  ", 20, 0m, requiresSubject: true);
        c.Name.Should().Be("Avaliação");
        c.Price.Should().Be(0m);
        c.RequiresSubject.Should().BeTrue();
    }

    [Fact]
    public void UpdatePrice_sets_and_validates_non_negative()
    {
        var c = SvcCatalogItem.Create(Tenant, "X", 30, 50m);

        c.UpdatePrice(75m);
        c.Price.Should().Be(75m);

        var act = () => c.UpdatePrice(-1m);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void UpdateDetails_changes_fields_and_validates_duration()
    {
        var c = SvcCatalogItem.Create(Tenant, "X", 30, 50m);

        c.UpdateDetails("Corte", description: "desc", category: "Cabelo",
            durationMinutes: 45, requiresSubject: true);
        c.Name.Should().Be("Corte");
        c.Description.Should().Be("desc");
        c.Category.Should().Be("Cabelo");
        c.DurationMinutes.Should().Be(45);
        c.RequiresSubject.Should().BeTrue();

        var act = () => c.UpdateDetails("Y", null, null, 0, false);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void UpdateCommission_sets_clears_and_validates()
    {
        var c = SvcCatalogItem.Create(Tenant, "X", 30, 50m);

        c.UpdateCommission(10m);
        c.CommissionPercent.Should().Be(10m);

        c.UpdateCommission(null);
        c.CommissionPercent.Should().BeNull();

        var act = () => c.UpdateCommission(-5m);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Deactivate_and_Activate_toggle_IsActive()
    {
        var c = SvcCatalogItem.Create(Tenant, "X", 30, 50m);

        c.Deactivate();
        c.IsActive.Should().BeFalse();

        c.Activate();
        c.IsActive.Should().BeTrue();
    }
}
