using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

public class SvcRecordEntryTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Ctx    = Guid.NewGuid();
    private static readonly Guid Author = Guid.NewGuid();

    [Fact]
    public void Create_with_text_only_is_valid()
    {
        var r = SvcRecordEntry.Create(Tenant, SvcRecordContextType.Customer, Ctx, Author, "first visit", null);
        r.ContextType.Should().Be(SvcRecordContextType.Customer);
        r.ContextId.Should().Be(Ctx);
        r.AuthorUserId.Should().Be(Author);
        r.Text.Should().Be("first visit");
        r.AttachmentsJson.Should().BeNull();
    }

    [Fact]
    public void Create_with_attachments_only_is_valid()
    {
        var r = SvcRecordEntry.Create(Tenant, SvcRecordContextType.Subject, Ctx, Author, null, "[{\"storageKey\":\"k\"}]");
        r.AttachmentsJson.Should().NotBeNull();
        r.Text.Should().BeNull();
    }

    [Fact]
    public void Create_with_neither_text_nor_attachments_throws()
    {
        var act = () => SvcRecordEntry.Create(Tenant, SvcRecordContextType.Customer, Ctx, Author, "  ", null);
        act.Should().Throw<DomainException>().WithMessage("*text*attachment*");
    }

    [Fact]
    public void Create_with_empty_context_id_throws()
    {
        var act = () => SvcRecordEntry.Create(Tenant, SvcRecordContextType.Customer, Guid.Empty, Author, "x", null);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_with_empty_author_throws()
    {
        var act = () => SvcRecordEntry.Create(Tenant, SvcRecordContextType.Customer, Ctx, Guid.Empty, "x", null);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_trims_text()
        => SvcRecordEntry.Create(Tenant, SvcRecordContextType.Customer, Ctx, Author, "  hi  ", null).Text.Should().Be("hi");
}
