using FluentAssertions;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Modules.Interpreter;

namespace Nexo.UnitTests.Interpreter;

public class RuleBasedAnalyzerTests
{
    private readonly RuleBasedAnalyzer _sut = new();

    private static AnalysisInput TextInput(string text) => new(
        Source:     InputSourceType.Text,
        RawText:    text,
        StorageKey: null,
        TenantId:   Guid.NewGuid(),
        UserId:     Guid.NewGuid());

    // ── Provider ─────────────────────────────────────────────────────────────

    [Fact]
    public void Provider_IsRuleBased()
        => _sut.Provider.Should().Be(AnalyzerProvider.RuleBased);

    // ── Amount extraction ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("Pagamento de R$ 150,00 para João",          150.00)]
    [InlineData("Pix R$1.200,50 enviado",                    1200.50)]
    [InlineData("Transferência de R$ 3.500,00 para conta",   3500.00)]
    [InlineData("Despesa: 75,99",                             75.99)]
    [InlineData("total 1234.56",                             1234.56)]
    public async Task Analyze_ExtractsAmount_FromBrazilianFormats(string text, decimal expected)
    {
        var result = await _sut.AnalyzeAsync(TextInput(text));

        result.Amount.Value.Should().BeApproximately(expected, 0.01m);
        result.Amount.Status.Should().NotBe(FieldStatus.RequiresInput);
    }

    [Fact]
    public async Task Analyze_AmountWithCurrencySymbol_HasHigherConfidence()
    {
        var withSymbol    = await _sut.AnalyzeAsync(TextInput("R$ 100,00 pagamento"));
        var withoutSymbol = await _sut.AnalyzeAsync(TextInput("pagamento 100,00"));

        withSymbol.Amount.Confidence.Should().BeGreaterThan(withoutSymbol.Amount.Confidence);
        withSymbol.Amount.Status.Should().Be(FieldStatus.AutoFilled);
    }

    [Fact]
    public async Task Analyze_NoAmount_ReturnsUnknown()
    {
        var result = await _sut.AnalyzeAsync(TextInput("sem valor aqui, apenas texto"));

        result.Amount.Value.Should().BeNull();
        result.Amount.Status.Should().Be(FieldStatus.RequiresInput);
        result.Amount.Confidence.Should().Be(0f);
    }

    // ── Date extraction ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("Pagamento em 05/05/2026",        2026, 5, 5)]
    [InlineData("Data: 01-12-2025 valor R$100",   2025, 12, 1)]
    [InlineData("comprovante 31/03/2026",         2026, 3, 31)]
    public async Task Analyze_ExtractsDate_BrazilianFormat(string text, int year, int month, int day)
    {
        var result = await _sut.AnalyzeAsync(TextInput(text));

        result.Date.Value.Should().Be(new DateOnly(year, month, day));
        result.Date.Status.Should().NotBe(FieldStatus.RequiresInput);
    }

    [Theory]
    [InlineData("ISO date 2026-05-07 pagamento",  2026, 5, 7)]
    [InlineData("2025-01-31 transferência",        2025, 1, 31)]
    public async Task Analyze_ExtractsDate_IsoFormat_HasHighestConfidence(string text, int year, int month, int day)
    {
        var result = await _sut.AnalyzeAsync(TextInput(text));

        result.Date.Value.Should().Be(new DateOnly(year, month, day));
        result.Date.Confidence.Should().BeGreaterThanOrEqualTo(0.90f);
        result.Date.Status.Should().Be(FieldStatus.AutoFilled);
    }

    [Fact]
    public async Task Analyze_InvalidDate_ReturnsUnknown()
    {
        var result = await _sut.AnalyzeAsync(TextInput("data 32/13/2026 invalida"));

        result.Date.Value.Should().BeNull();
        result.Date.Status.Should().Be(FieldStatus.RequiresInput);
    }

    [Fact]
    public async Task Analyze_NoDate_ReturnsUnknown()
    {
        var result = await _sut.AnalyzeAsync(TextInput("pagamento sem data R$ 50,00"));

        result.Date.Value.Should().BeNull();
        result.Date.Status.Should().Be(FieldStatus.RequiresInput);
    }

    // ── Payee extraction ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("pagamento para: Fornecedor ABC Ltda",       "Fornecedor ABC Ltda")]
    [InlineData("pagto: João da Silva",                      "João da Silva")]
    [InlineData("fornecedor: Distribuidora Central",         "Distribuidora Central")]
    [InlineData("Payee: Maria Luiza",                       "Maria Luiza")]
    public async Task Analyze_ExtractsPayee_FromPrefixedFormats(string text, string expectedPayee)
    {
        var result = await _sut.AnalyzeAsync(TextInput(text));

        result.Payee.Value.Should().Be(expectedPayee);
        result.Payee.Status.Should().NotBe(FieldStatus.RequiresInput);
    }

    [Fact]
    public async Task Analyze_NoPayeePrefix_ReturnsUnknown()
    {
        var result = await _sut.AnalyzeAsync(TextInput("R$ 100,00 em 05/05/2026"));

        result.Payee.Value.Should().BeNull();
        result.Payee.Status.Should().Be(FieldStatus.RequiresInput);
    }

    // ── Account extraction ────────────────────────────────────────────────────

    [Theory]
    [InlineData("conta: Banco do Brasil",     "Banco do Brasil")]
    [InlineData("banco: Itaú",               "Itaú")]
    [InlineData("cartao: Nubank",            "Nubank")]
    public async Task Analyze_ExtractsAccount_FromPrefixedFormats(string text, string expectedAccount)
    {
        var result = await _sut.AnalyzeAsync(TextInput(text));

        result.Account.Value.Should().Be(expectedAccount);
        result.Account.Status.Should().NotBe(FieldStatus.RequiresInput);
    }

    // ── Full realistic scenarios ───────────────────────────────────────────────

    [Fact]
    public async Task Analyze_FullPixText_ExtractsAllFields()
    {
        const string text = "Pix enviado em 07/05/2026\n" +
                            "para: João Carlos da Silva\n" +
                            "R$ 850,00\n" +
                            "conta: Banco Nubank";

        var result = await _sut.AnalyzeAsync(TextInput(text));

        result.Amount.Value.Should().Be(850m);
        result.Date.Value.Should().Be(new DateOnly(2026, 5, 7));
        result.Payee.Value.Should().Be("João Carlos da Silva");
        result.Account.Value.Should().Be("Banco Nubank");
    }

    [Fact]
    public async Task Analyze_AmbiguousText_ReturnsRequiresInputForUnresolvedFields()
    {
        const string text = "algum texto aqui sem estrutura nenhuma";

        var result = await _sut.AnalyzeAsync(TextInput(text));

        result.Amount.Status.Should().Be(FieldStatus.RequiresInput);
        result.Date.Status.Should().Be(FieldStatus.RequiresInput);
        result.Payee.Status.Should().Be(FieldStatus.RequiresInput);
    }

    [Fact]
    public async Task Analyze_EmptyText_ReturnsAllUnknown()
    {
        var result = await _sut.AnalyzeAsync(TextInput(""));

        result.Amount.Status.Should().Be(FieldStatus.RequiresInput);
        result.Date.Status.Should().Be(FieldStatus.RequiresInput);
        result.Payee.Status.Should().Be(FieldStatus.RequiresInput);
        result.Account.Status.Should().Be(FieldStatus.RequiresInput);
    }

    // ── KnownFields: partial delegation ──────────────────────────────────────

    [Fact]
    public async Task Analyze_RespectsKnownFields_SkipsAutoFilledAmount()
    {
        var knownAmount = ExtractedField<decimal?>.From(999m, 0.95f, AnalyzerProvider.Claude);
        var known = new PartialExtraction(Amount: knownAmount);

        var input = TextInput("R$ 150,00 pagamento") with { KnownFields = known };

        var result = await _sut.AnalyzeAsync(input);

        // Should NOT override the known AutoFilled amount
        result.Amount.Value.Should().Be(999m);
        result.Amount.Confidence.Should().Be(0.95f);
    }

    [Fact]
    public async Task Analyze_ExtractsUnresolvedFieldsEvenWhenOtherKnown()
    {
        var knownDate = ExtractedField<DateOnly?>.From(new DateOnly(2026, 1, 1), 0.95f, AnalyzerProvider.Claude);
        var known = new PartialExtraction(Date: knownDate);

        var input = TextInput("R$ 200,00 pagamento") with { KnownFields = known };

        var result = await _sut.AnalyzeAsync(input);

        result.Date.Value.Should().Be(new DateOnly(2026, 1, 1));   // preserved
        result.Amount.Value.Should().Be(200m);                       // extracted
    }

    // ── FieldStatus thresholds ────────────────────────────────────────────────

    [Fact]
    public async Task Analyze_AmountWithCurrencySymbol_IsAutoFilled()
    {
        var result = await _sut.AnalyzeAsync(TextInput("R$ 500,00"));
        result.Amount.Status.Should().Be(FieldStatus.AutoFilled);
    }

    [Fact]
    public async Task Analyze_PromptMetadata_IsRuleBasedProvider()
    {
        var result = await _sut.AnalyzeAsync(TextInput("R$ 100,00"));
        result.Prompt.PromptType.Should().Be("rule-based");
        result.Prompt.PromptVersion.Should().Be("1.0.0");
        result.Prompt.PromptHash.Should().NotBeNullOrWhiteSpace();
    }
}
