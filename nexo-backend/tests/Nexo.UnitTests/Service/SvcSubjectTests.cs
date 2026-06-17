using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

public class SvcSubjectTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Cust   = Guid.NewGuid();

    [Fact]
    public void Create_sets_fields_and_defaults_active()
    {
        var s = SvcSubject.Create(Tenant, Cust, SvcSubjectKind.Pet, "Rex", "{\"species\":\"dog\"}", "friendly");
        s.CustomerId.Should().Be(Cust);
        s.Kind.Should().Be(SvcSubjectKind.Pet);
        s.DisplayName.Should().Be("Rex");
        s.MetadataJson.Should().Be("{\"species\":\"dog\"}");
        s.Notes.Should().Be("friendly");
        s.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_trims_display_name()
        => SvcSubject.Create(Tenant, Cust, SvcSubjectKind.Vehicle, "  Civic  ").DisplayName.Should().Be("Civic");

    [Fact]
    public void Create_with_empty_customer_throws()
    {
        var act = () => SvcSubject.Create(Tenant, Guid.Empty, SvcSubjectKind.Pet, "Rex");
        act.Should().Throw<DomainException>().WithMessage("*customer*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_with_blank_display_name_throws(string name)
    {
        var act = () => SvcSubject.Create(Tenant, Cust, SvcSubjectKind.Pet, name);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void UpdateDetails_changes_kind_name_notes()
    {
        var s = SvcSubject.Create(Tenant, Cust, SvcSubjectKind.Pet, "Rex");
        s.UpdateDetails(SvcSubjectKind.Other, "Rex II", "older");
        s.Kind.Should().Be(SvcSubjectKind.Other);
        s.DisplayName.Should().Be("Rex II");
        s.Notes.Should().Be("older");
    }

    [Fact]
    public void UpdateMetadata_replaces_json()
    {
        var s = SvcSubject.Create(Tenant, Cust, SvcSubjectKind.Pet, "Rex");
        s.UpdateMetadata("{\"breed\":\"shih-tzu\"}");
        s.MetadataJson.Should().Be("{\"breed\":\"shih-tzu\"}");
    }

    [Fact]
    public void Activate_deactivate_toggles()
    {
        var s = SvcSubject.Create(Tenant, Cust, SvcSubjectKind.Pet, "Rex");
        s.Deactivate(); s.IsActive.Should().BeFalse();
        s.Activate();   s.IsActive.Should().BeTrue();
    }
}
