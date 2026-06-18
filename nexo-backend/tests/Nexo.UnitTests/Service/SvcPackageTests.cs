using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

public class SvcPackageTests
{
    private static readonly Guid T = Guid.NewGuid();

    [Fact]
    public void Create_defaults_active()
    {
        var p = SvcPackage.Create(T, "Plano Pet", 500m, "desc", 30);
        p.Name.Should().Be("Plano Pet");
        p.Price.Should().Be(500m);
        p.ValidityDays.Should().Be(30);
        p.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_blank_name_throws()
        => ((Action)(() => SvcPackage.Create(T, " ", 1m))).Should().Throw<DomainException>();

    [Fact]
    public void Create_negative_price_throws()
        => ((Action)(() => SvcPackage.Create(T, "x", -1m))).Should().Throw<DomainException>();

    [Fact]
    public void Create_non_positive_validity_throws()
        => ((Action)(() => SvcPackage.Create(T, "x", 1m, null, 0))).Should().Throw<DomainException>();

    [Fact]
    public void Activate_deactivate_toggles()
    {
        var p = SvcPackage.Create(T, "x", 1m);
        p.Deactivate(); p.IsActive.Should().BeFalse();
        p.Activate();   p.IsActive.Should().BeTrue();
    }

    [Fact]
    public void UpdatePrice_changes_price()
    {
        var p = SvcPackage.Create(T, "x", 1m);
        p.UpdatePrice(99m);
        p.Price.Should().Be(99m);
    }

    [Fact]
    public void PackageItem_create_computes_snapshot()
    {
        var i = SvcPackageItem.Create(T, Guid.NewGuid(), Guid.NewGuid(), "Banho", 4m);
        i.NameSnapshot.Should().Be("Banho");
        i.IncludedQuantity.Should().Be(4m);
    }

    [Fact]
    public void PackageItem_non_positive_qty_throws()
        => ((Action)(() => SvcPackageItem.Create(T, Guid.NewGuid(), Guid.NewGuid(), "x", 0m)))
            .Should().Throw<DomainException>();
}
