using FluentAssertions;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Infrastructure.Modules.Interpreter;
using NSubstitute;

namespace Nexo.UnitTests.Interpreter;

public class DescriptionNormalizerTests
{
    private readonly ITenantStopwordRepository _stopwords = Substitute.For<ITenantStopwordRepository>();
    private readonly Guid _tenantId = Guid.NewGuid();

    private DescriptionNormalizer BuildSut(IReadOnlyList<string>? tenantWords = null)
    {
        _stopwords
            .GetWordsByTenantAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(tenantWords ?? Array.Empty<string>());

        return new DescriptionNormalizer(_stopwords);
    }

    // ── Basic pipeline ────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_EmptyString_ReturnsEmpty()
    {
        var sut = BuildSut();
        sut.Normalize("", _tenantId).Should().BeEmpty();
        sut.Normalize("   ", _tenantId).Should().BeEmpty();
    }

    [Fact]
    public void Normalize_IsLowercase()
    {
        var sut = BuildSut();
        sut.Normalize("PAGAMENTO PIX", _tenantId).Should().Be("pagamento pix");
    }

    [Fact]
    public void Normalize_RemovesAccents()
    {
        var sut = BuildSut();
        sut.Normalize("Pagaçâo descrição fornecédo", _tenantId)
           .Should().Be("pagacao descricao fornecedo");
    }

    [Fact]
    public void Normalize_RemovesPtBrStopwords()
    {
        var sut = BuildSut();
        // "pagamento para o fornecedor" → removes "para", "o"
        var result = sut.Normalize("pagamento para o fornecedor", _tenantId);
        result.Should().Contain("pagamento");
        result.Should().Contain("fornecedor");
        result.Should().NotContain(" para ");
        result.Should().NotContain(" o ");
    }

    [Fact]
    public void Normalize_RemovesSingleCharTokens()
    {
        var sut = BuildSut();
        var result = sut.Normalize("compra a b c materiais", _tenantId);
        result.Should().NotContain(" a ");
        result.Should().NotContain(" b ");
        result.Should().NotContain(" c ");
        result.Should().Contain("compra");
        result.Should().Contain("materiais");
    }

    [Fact]
    public void Normalize_NormalizesWhitespace()
    {
        var sut = BuildSut();
        sut.Normalize("pagamento    fornecedor   obra", _tenantId)
           .Should().Be("pagamento fornecedor obra");
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_IsIdempotent()
    {
        var sut    = BuildSut();
        var first  = sut.Normalize("Pagamento ao Fornecedor Central", _tenantId);
        var second = sut.Normalize(first, _tenantId);
        first.Should().Be(second);
    }

    // ── Tenant stopwords ──────────────────────────────────────────────────────

    [Fact]
    public void Normalize_RemovesTenantStopwords()
    {
        var sut = BuildSut(tenantWords: ["ifood", "rappi", "uber"]);
        var result = sut.Normalize("pedido ifood entregue rappi", _tenantId);
        result.Should().NotContain("ifood");
        result.Should().NotContain("rappi");
        result.Should().Contain("pedido");
        result.Should().Contain("entregue");
    }

    [Fact]
    public void Normalize_TenantStopwordsAreAlreadyNormalized_MatchesCorrectly()
    {
        // Tenant stopwords stored as normalized (lowercase, no accent).
        // Input may have accents/caps — normalizer strips them before comparing.
        var sut = BuildSut(tenantWords: ["obra"]);
        var result = sut.Normalize("Despesa de Obra Civil", _tenantId);
        result.Should().NotContain("obra");
        result.Should().Contain("despesa");
        result.Should().Contain("civil");
    }

    [Fact]
    public void Normalize_EmptyTenantStopwords_DoesNotBreak()
    {
        var sut = BuildSut(tenantWords: []);
        sut.Normalize("pagamento fornecedor materiais", _tenantId)
           .Should().Be("pagamento fornecedor materiais");
    }

    // ── Real-world scenarios ──────────────────────────────────────────────────

    [Fact]
    public void Normalize_PixComprovante_ProducesCleanTokens()
    {
        var sut = BuildSut();
        var result = sut.Normalize("Pix enviado para o fornecedor de materiais de construção", _tenantId);
        // Should keep: "pix", "enviado", "fornecedor", "materiais", "construcao"
        // Should remove: "para", "o", "de", "de" (stopwords)
        result.Should().Contain("pix");
        result.Should().Contain("enviado");
        result.Should().Contain("fornecedor");
        result.Should().Contain("materiais");
        result.Should().Contain("construcao");
        result.Should().NotContain("para");
    }

    [Fact]
    public void Normalize_PunctuationAndSpecialChars_AreRemoved()
    {
        var sut = BuildSut();
        var result = sut.Normalize("pagamento! R$100,00 - fornecedor #01", _tenantId);
        result.Should().NotContain("!");
        result.Should().NotContain("-");
        result.Should().NotContain("#");
        result.Should().NotContain("$");
    }

    [Fact]
    public void Normalize_DifferentTenantsDoNotShareStopwords()
    {
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        _stopwords
            .GetWordsByTenantAsync(tenant1, Arg.Any<CancellationToken>())
            .Returns(new List<string> { "central" });
        _stopwords
            .GetWordsByTenantAsync(tenant2, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());

        var sut = new DescriptionNormalizer(_stopwords);

        var forTenant1 = sut.Normalize("distribuidora central materiais", tenant1);
        var forTenant2 = sut.Normalize("distribuidora central materiais", tenant2);

        forTenant1.Should().NotContain("central");
        forTenant2.Should().Contain("central");
    }
}
