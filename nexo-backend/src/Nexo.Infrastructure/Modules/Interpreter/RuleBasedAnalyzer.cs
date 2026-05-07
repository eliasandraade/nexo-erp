using System.Globalization;
using System.Text.RegularExpressions;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Infrastructure.Modules.Interpreter;

/// <summary>
/// Deterministic regex extractor. Tries to resolve each field independently.
/// Fields resolved with high confidence (≥0.90) are AutoFilled.
/// Unresolved fields return ExtractedField.Unknown() — AnalyzerSelectorService
/// can delegate those to an LLM analyzer for a second pass.
/// </summary>
public sealed partial class RuleBasedAnalyzer : IDocumentAnalyzer
{
    public AnalyzerProvider Provider => AnalyzerProvider.RuleBased;

    public Task<AnalysisOutput> AnalyzeAsync(AnalysisInput input, CancellationToken ct = default)
    {
        var text = BuildSearchText(input);

        var known   = input.KnownFields;
        var amount  = IsResolved(known?.Amount)  ? known!.Amount!  : ExtractAmount(text);
        var date    = IsResolved(known?.Date)    ? known!.Date!    : ExtractDate(text);
        var payee   = IsResolved(known?.Payee)   ? known!.Payee!   : ExtractPayee(text);
        var account = IsResolved(known?.Account) ? known!.Account! : ExtractAccount(text);

        var output = new AnalysisOutput(
            Amount:              amount,
            Date:                date,
            Payee:               payee,
            Account:             account,
            RawProviderResponse: text,
            Prompt:              new PromptMetadata("rule-based", "1.0.0", ComputeHash(text)));

        return Task.FromResult(output);
    }

    private static bool IsResolved<T>(ExtractedField<T>? field)
        => field is { Status: FieldStatus.AutoFilled };

    // ── Field extractors ──────────────────────────────────────────────────────

    private static ExtractedField<decimal?> ExtractAmount(string text)
    {
        // Priority 1: currency-prefixed amount (R$ 1.234,56 / R$850,00 / R$1234.56)
        var m = AmountWithSymbolRegex().Match(text);
        if (m.Success)
        {
            if (TryParseAmount(m.Groups["value"].Value, out var v))
                return ExtractedField<decimal?>.From(v, 0.93f, AnalyzerProvider.RuleBased);
        }

        // Priority 2: comma-decimal without symbol (300,00 / 1.234,56) — unambiguous PT-BR
        var mc = AmountCommaRegex().Match(text);
        if (mc.Success)
        {
            if (TryParseAmount(mc.Groups["value"].Value, out var v))
                return ExtractedField<decimal?>.From(v, 0.80f, AnalyzerProvider.RuleBased);
        }

        // Priority 3: dot-decimal fallback (1234.56) — only match when no comma candidate
        var md = AmountDotDecimalRegex().Match(text);
        if (md.Success)
        {
            if (decimal.TryParse(md.Groups["value"].Value, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var v))
                return ExtractedField<decimal?>.From(v, 0.80f, AnalyzerProvider.RuleBased);
        }

        return ExtractedField<decimal?>.Unknown();
    }

    private static bool TryParseAmount(string raw, out decimal value)
    {
        // PT-BR: dot = thousand sep, comma = decimal sep → strip dots, comma→dot
        var normalized = raw.Replace(".", "").Replace(",", ".");
        return decimal.TryParse(normalized, NumberStyles.Any,
            CultureInfo.InvariantCulture, out value);
    }

    private static ExtractedField<DateOnly?> ExtractDate(string text)
    {
        // Matches: dd/mm/yyyy or dd-mm-yyyy
        var m = DateDmyRegex().Match(text);
        if (m.Success)
        {
            var d = int.Parse(m.Groups["d"].Value);
            var mo = int.Parse(m.Groups["m"].Value);
            var y = int.Parse(m.Groups["y"].Value);
            if (IsValidDate(d, mo, y))
                return ExtractedField<DateOnly?>.From(new DateOnly(y, mo, d), 0.92f, AnalyzerProvider.RuleBased);
        }

        // Matches: yyyy-mm-dd (ISO)
        var iso = DateIsoRegex().Match(text);
        if (iso.Success)
        {
            var y = int.Parse(iso.Groups["y"].Value);
            var mo = int.Parse(iso.Groups["m"].Value);
            var d = int.Parse(iso.Groups["d"].Value);
            if (IsValidDate(d, mo, y))
                return ExtractedField<DateOnly?>.From(new DateOnly(y, mo, d), 0.95f, AnalyzerProvider.RuleBased);
        }

        return ExtractedField<DateOnly?>.Unknown();
    }

    private static ExtractedField<string?> ExtractPayee(string text)
    {
        // Heuristic: look for "para:", "pagto:", "fornecedor:", "payee:" prefixes
        var m = PayeePrefixRegex().Match(text);
        if (m.Success)
        {
            var payee = m.Groups["name"].Value.Trim();
            if (payee.Length >= 3)
                return ExtractedField<string?>.From(payee, 0.78f, AnalyzerProvider.RuleBased);
        }

        return ExtractedField<string?>.Unknown();
    }

    private static ExtractedField<string?> ExtractAccount(string text)
    {
        // Heuristic: look for "conta:", "bank:", "cc:", "cc " prefixes
        var m = AccountPrefixRegex().Match(text);
        if (m.Success)
        {
            var account = m.Groups["name"].Value.Trim();
            if (account.Length >= 2)
                return ExtractedField<string?>.From(account, 0.72f, AnalyzerProvider.RuleBased);
        }

        return ExtractedField<string?>.Unknown();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildSearchText(AnalysisInput input)
        => input.RawText ?? string.Empty;

    private static bool IsValidDate(int d, int m, int y)
    {
        if (y < 2000 || y > 2100) return false;
        if (m < 1 || m > 12)     return false;
        if (d < 1 || d > 31)     return false;
        try { _ = new DateOnly(y, m, d); return true; }
        catch { return false; }
    }

    private static string ComputeHash(string text)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    // ── Compiled regexes ──────────────────────────────────────────────────────

    // Currency symbol required; value is PT-BR or dot-decimal
    [GeneratedRegex(
        @"R\$\s*(?<value>\d{1,3}(?:\.\d{3})*,\d{2}|\d+(?:[.,]\d{2})?)",
        RegexOptions.IgnoreCase)]
    private static partial Regex AmountWithSymbolRegex();

    // PT-BR comma-decimal without symbol (e.g. 300,00 / 1.234,56)
    [GeneratedRegex(
        @"(?<!\d)(?<value>\d{1,3}(?:\.\d{3})*,\d{2})(?!\d)")]
    private static partial Regex AmountCommaRegex();

    // English dot-decimal without symbol (e.g. 1234.56) — only matches \d+.\d{1,2}
    [GeneratedRegex(
        @"(?<!\d)(?<value>\d+\.\d{1,2})(?!\d)")]
    private static partial Regex AmountDotDecimalRegex();

    [GeneratedRegex(
        @"\b(?<d>\d{2})[/\-](?<m>\d{2})[/\-](?<y>\d{4})\b")]
    private static partial Regex DateDmyRegex();

    [GeneratedRegex(
        @"\b(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})\b")]
    private static partial Regex DateIsoRegex();

    [GeneratedRegex(
        @"(?:para|pagto|pagamento|fornecedor|payee|recebedor)\s*[:\-]\s*(?<name>[^\n\r,;]{3,50})",
        RegexOptions.IgnoreCase)]
    private static partial Regex PayeePrefixRegex();

    [GeneratedRegex(
        @"(?:conta|bank|banco|cc|cartao|cartão)\s*[:\-]\s*(?<name>[^\n\r,;]{2,40})",
        RegexOptions.IgnoreCase)]
    private static partial Regex AccountPrefixRegex();
}
