using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

public class SvcOrderItemTests
{
    private static readonly Guid T = Guid.NewGuid();
    private static readonly Guid Order = Guid.NewGuid();
    private static readonly Guid Item = Guid.NewGuid();

    [Fact]
    public void Create_computes_total_and_snapshots()
    {
        var i = SvcOrderItem.Create(T, Order, Item, Guid.NewGuid(), "Corte", "desc", 3m, 20m, 10m);
        i.OrderId.Should().Be(Order);
        i.CatalogItemId.Should().Be(Item);
        i.NameSnapshot.Should().Be("Corte");
        i.DescriptionSnapshot.Should().Be("desc");
        i.UnitPriceSnapshot.Should().Be(20m);
        i.CommissionPercentSnapshot.Should().Be(10m);
        i.Quantity.Should().Be(3m);
        i.TotalAmount.Should().Be(60m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_with_non_positive_quantity_throws(decimal q)
        => ((Action)(() => SvcOrderItem.Create(T, Order, Item, null, "A", null, q, 10m, null)))
            .Should().Throw<DomainException>();

    [Fact]
    public void Create_with_negative_price_throws()
        => ((Action)(() => SvcOrderItem.Create(T, Order, Item, null, "A", null, 1m, -1m, null)))
            .Should().Throw<DomainException>();

    [Fact]
    public void Create_with_empty_catalog_throws()
        => ((Action)(() => SvcOrderItem.Create(T, Order, Guid.Empty, null, "A", null, 1m, 10m, null)))
            .Should().Throw<DomainException>();

    [Fact]
    public void Update_changes_quantity_and_recomputes_total_keeping_price()
    {
        var i = SvcOrderItem.Create(T, Order, Item, null, "A", null, 2m, 15m, null); // 30
        i.Update(4m, Guid.NewGuid());
        i.Quantity.Should().Be(4m);
        i.UnitPriceSnapshot.Should().Be(15m); // unchanged
        i.TotalAmount.Should().Be(60m);
    }
}
