using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Middleware;
using Nexo.Application.Common.Observability;
using Xunit;

namespace Nexo.UnitTests.Observability;

public class ErrorFingerprintTests
{
    private static Exception Thrown(Func<Exception> make)
    {
        try { throw make(); }
        catch (Exception ex) { return ex; }
    }

    [Fact]
    public void Personal_data_is_masked_before_it_is_stored()
    {
        ErrorFingerprint.Mask("Cliente maria@exemplo.com, CPF 529.982.247-25, fone 11987654321, item 12")
            .Should().Be("Cliente *** CPF #### fone #### item 12");
        ErrorFingerprint.Mask("pedido 4521 de 3 itens").Should().Be("pedido #### de 3 itens");
        ErrorFingerprint.Mask("Chave 'segredo' inválida").Should().Be("Chave '***' inválida");
        ErrorFingerprint.Mask("documento 52998224725").Should().Be("documento ####");
    }

    [Fact]
    public void Database_errors_keep_only_their_kind()
        => ErrorFingerprint.Summarize(new DbUpdateException("duplicate key (email)=(a@b.com)"))
            .Should().Be("DbUpdateException: database error");

    [Fact]
    public void The_same_kind_of_error_has_the_same_fingerprint()
    {
        var a = Thrown(() => new InvalidOperationException("x 1"));
        var b = Thrown(() => new InvalidOperationException("y 2"));
        ErrorFingerprint.Compute(a, "api/sales").Should().Be(ErrorFingerprint.Compute(b, "api/sales"));
        ErrorFingerprint.Compute(a, "api/sales").Should().NotBe(ErrorFingerprint.Compute(a, "api/products"));
        ErrorFingerprint.Compute(a, "api/sales").Should().NotBe(ErrorFingerprint.Compute(Thrown(() => new ArgumentException()), "api/sales"));
    }

    [Theory]
    [InlineData("frontend-req-12345", "frontend-req-12345")]
    [InlineData("short", null)]
    [InlineData("has space in it", null)]
    [InlineData("evil\"value{with}junk", null)]
    public void Request_ids_from_callers_are_taken_only_when_sane(string value, string? expected)
        => CorrelationIdMiddleware.Sanitize(value).Should().Be(expected);
}
