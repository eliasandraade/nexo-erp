using System;
using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

/// <summary>Domain rules for the SvcProfessional aggregate (PR1 engine foundation).</summary>
public class SvcProfessionalTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Create_sets_required_fields_and_defaults()
    {
        var p = SvcProfessional.Create(Tenant, "Ana Paula", role: "Cabeleireira");

        p.TenantId.Should().Be(Tenant);
        p.Name.Should().Be("Ana Paula");
        p.Role.Should().Be("Cabeleireira");
        p.IsActive.Should().BeTrue();
        p.DefaultCommissionPercent.Should().BeNull();
        p.UserId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_name(string name)
    {
        var act = () => SvcProfessional.Create(Tenant, name);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_trims_name_and_lowercases_email()
    {
        var p = SvcProfessional.Create(Tenant, "  Ana  ", email: "  ANA@X.COM ");
        p.Name.Should().Be("Ana");
        p.Email.Should().Be("ana@x.com");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.1)]
    public void Create_rejects_commission_out_of_range(decimal pct)
    {
        var act = () => SvcProfessional.Create(Tenant, "Ana", defaultCommissionPercent: pct);
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void Create_accepts_commission_in_range(decimal pct)
    {
        SvcProfessional.Create(Tenant, "Ana", defaultCommissionPercent: pct)
            .DefaultCommissionPercent.Should().Be(pct);
    }

    [Fact]
    public void Create_can_link_a_user_without_login()
    {
        var userId = Guid.NewGuid();
        SvcProfessional.Create(Tenant, "Ana", userId: userId).UserId.Should().Be(userId);
    }

    [Fact]
    public void UpdateDetails_changes_fields()
    {
        var p = SvcProfessional.Create(Tenant, "Ana");
        p.UpdateDetails("Beatriz", role: "Manicure", specialty: "Unhas",
            color: "#FF0000", phone: "1199999", email: "B@X.com");

        p.Name.Should().Be("Beatriz");
        p.Role.Should().Be("Manicure");
        p.Specialty.Should().Be("Unhas");
        p.Color.Should().Be("#FF0000");
        p.Phone.Should().Be("1199999");
        p.Email.Should().Be("b@x.com");
    }

    [Fact]
    public void UpdateDetails_rejects_blank_name()
    {
        var p = SvcProfessional.Create(Tenant, "Ana");
        var act = () => p.UpdateDetails("  ", null, null, null, null, null);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void UpdateCommission_sets_clears_and_validates()
    {
        var p = SvcProfessional.Create(Tenant, "Ana");

        p.UpdateCommission(30m);
        p.DefaultCommissionPercent.Should().Be(30m);

        p.UpdateCommission(null);
        p.DefaultCommissionPercent.Should().BeNull();

        var act = () => p.UpdateCommission(150m);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Deactivate_and_Activate_toggle_IsActive()
    {
        var p = SvcProfessional.Create(Tenant, "Ana");

        p.Deactivate();
        p.IsActive.Should().BeFalse();

        p.Activate();
        p.IsActive.Should().BeTrue();
    }
}
