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

public class ErrorMaskingAndLogFormatTests
{
    [Theory]
    [InlineData("The input string 'João Silva' was not in a correct format.", "The input string '***' was not in a correct format.")]
    [InlineData("Cliente “Maria Souza” não encontrado", "Cliente '***' não encontrado")]
    [InlineData("filtro name=João tenant=abc", "filtro name=*** tenant=***")]
    [InlineData("linha 1\nlinha\t2", "linha 1 linha 2")]
    public void Quoted_spans_values_and_whitespace_are_handled(string message, string expected)
        => ErrorFingerprint.Mask(message).Should().Be(expected);

    [Fact]
    public void A_long_summary_is_never_cut_in_the_middle_of_an_emoji()
    {
        var summary = ErrorFingerprint.Summarize(new InvalidOperationException(new string('a', 476) + "😀😀"));
        summary.Should().NotEndWith("\ud83d");
        System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(summary)).Should().Be(summary);
    }

    [Fact]
    public void Json_log_lines_have_the_keys_the_log_viewer_reads()
    {
        var evt = new Serilog.Events.LogEvent(DateTimeOffset.UtcNow, Serilog.Events.LogEventLevel.Warning, null,
            new Serilog.Parsing.MessageTemplateParser().Parse("Caixa {Id} sem saldo"),
            [new Serilog.Events.LogEventProperty("Id", new Serilog.Events.ScalarValue(7)),
             new Serilog.Events.LogEventProperty("RequestId", new Serilog.Events.ScalarValue("abc12345"))]);
        var writer = new StringWriter();
        Nexo.Api.Observability.RequestLogging.JsonFormatter().Format(evt, writer);
        var json = System.Text.Json.JsonDocument.Parse(writer.ToString()).RootElement;
        json.GetProperty("level").GetString().Should().Be("warn");
        json.GetProperty("message").GetString().Should().Be("Caixa 7 sem saldo");
        json.GetProperty("RequestId").GetString().Should().Be("abc12345");
    }

    [Fact]
    public void Our_request_id_replaces_the_one_aspnet_puts_on_each_event()
    {
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext { TraceIdentifier = "ours-1234567890" };
        var accessor = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = http };
        var evt = new Serilog.Events.LogEvent(DateTimeOffset.UtcNow, Serilog.Events.LogEventLevel.Information, null,
            new Serilog.Parsing.MessageTemplateParser().Parse("x"),
            [new Serilog.Events.LogEventProperty("RequestId", new Serilog.Events.ScalarValue("0HNKESTREL"))]);
        new Nexo.Api.Observability.RequestIdEnricher(accessor).Enrich(evt, new PropertyFactory());
        ((Serilog.Events.ScalarValue)evt.Properties["RequestId"]).Value.Should().Be("ours-1234567890");
    }

    private sealed class PropertyFactory : Serilog.Core.ILogEventPropertyFactory
    {
        public Serilog.Events.LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
            => new(name, new Serilog.Events.ScalarValue(value));
    }
}
