using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Infrastructure.Modules.Interpreter;

/// <summary>
/// Idempotent, culture-invariant pipeline:
/// trim → lowercase → remove accents → tokenize → remove PT-BR base stopwords
/// → remove tenant stopwords → normalize whitespace.
/// Tenant stopwords are loaded synchronously; safe in ASP.NET Core (no sync context).
/// </summary>
public sealed partial class DescriptionNormalizer : IDescriptionNormalizer
{
    private static readonly HashSet<string> PtBrStopwords = new(StringComparer.Ordinal)
    {
        "a", "ao", "aos", "as", "com", "da", "das", "de", "do", "dos",
        "e", "em", "na", "nas", "no", "nos", "o", "os", "ou", "para",
        "pela", "pelas", "pelo", "pelos", "por", "que", "se", "um", "uma",
        "umas", "uns", "ate", "aqui", "ali", "la", "ca", "ja",
        "mais", "mas", "me", "meu", "minha", "muito", "nem", "nao",
        "nossa", "nossas", "nosso", "nossos", "num", "numa",
        "nunca", "pois", "qual", "quando", "quanto", "seu", "sua", "suas",
        "seus", "so", "tambem", "te", "tem", "teu", "tua", "tuas", "teus",
        "todo", "todos", "toda", "todas", "voce", "voces", "foi", "ser",
        "esta", "isso", "esse", "essa", "esses", "essas",
        "aquele", "aquela", "aqueles", "aquelas", "este",
        "ha", "havia", "ter", "teria", "seria", "era", "eram",
        "sobre", "sob", "entre", "contra", "sem", "apos",
        "ante", "desde", "perante", "tras",
    };

    private readonly ITenantStopwordRepository _stopwords;

    public DescriptionNormalizer(ITenantStopwordRepository stopwords) => _stopwords = stopwords;

    public string Normalize(string input, Guid tenantId)
    {
        // Safe in ASP.NET Core — no SynchronizationContext means no deadlock risk.
        var tenantWords = _stopwords.GetWordsByTenantAsync(tenantId)
            .GetAwaiter().GetResult();

        return NormalizeCore(input, tenantWords);
    }

    private static string NormalizeCore(string input, IReadOnlyList<string> tenantStopwords)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var text = input.Trim();
        text     = text.ToLowerInvariant();
        text     = RemoveAccents(text);

        var tokens = TokenizerRegex().Split(text);

        var kept = tokens.Where(t => t.Length > 1 && !PtBrStopwords.Contains(t));

        if (tenantStopwords.Count > 0)
        {
            var tenantSet = new HashSet<string>(tenantStopwords, StringComparer.Ordinal);
            kept = kept.Where(t => !tenantSet.Contains(t));
        }

        return string.Join(' ', kept);
    }

    private static string RemoveAccents(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb          = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex TokenizerRegex();
}
