using System.Globalization;
using System.Text;

namespace Nexo.Application.Modules.Build;

/// <summary>Facts (already published or approved) a client update is written from. Nothing internal goes in here.</summary>
public sealed record BuildClientFacts(
    string ProjectName,
    DateOnly From,
    DateOnly To,
    decimal? ActualPercent,
    decimal? ActualPercentAtStart,
    IReadOnlyList<string> CompletedStages,
    IReadOnlyList<(DateOnly Date, string Summary)> VisibleLogSummaries,
    IReadOnlyList<(int Number, string Title, int ScheduleImpactDays, decimal? CostImpact)> ApprovedChangeOrders,
    IReadOnlyList<(int Number, DateOnly PeriodEnd, decimal? Amount)> ApprovedMeasurements,
    IReadOnlyList<(string Stage, DateOnly PlannedEnd)> UpcomingMilestones,
    DateOnly? ForecastDelivery);

/// <summary>
/// The deterministic fallbacks of the Build AI — used when no language model is available. They only extract and template:
/// no generated prose. Text matching is accent- and case-insensitive by tokens (no regular expressions).
/// </summary>
public static class BuildAiRules
{
    private static readonly System.Globalization.CultureInfo PtBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "de", "da", "do", "das", "dos", "e", "a", "o", "as", "os", "em", "no", "na", "nos", "nas", "para", "com", "por", "etapa", "fase",
    };

    /// <summary>Lowercase, without accents.</summary>
    public static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Folded words (letters and digits), in order.</summary>
    public static IReadOnlyList<string> Tokens(string? text)
    {
        var folded = Fold(text);
        var tokens = new List<string>();
        var sb = new StringBuilder();
        foreach (var c in folded)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    /// <summary>The meaningful words of a stage name (no stop words, at least 3 letters).</summary>
    public static IReadOnlyList<string> SignificantTokens(string? name)
        => Tokens(name).Where(t => t.Length >= 3 && !StopWords.Contains(t)).Distinct().ToList();

    /// <summary>Same word allowing simple inflections (plural, gender): equal, or a long common prefix.</summary>
    public static bool SameWord(string a, string b)
    {
        if (a == b) return true;
        var min = Math.Min(a.Length, b.Length);
        if (min < 5) return false;
        var common = 0;
        while (common < min && a[common] == b[common]) common++;
        return common >= Math.Max(5, min - 2);
    }

    /// <summary>A stage is mentioned when every meaningful word of its name appears in the text.</summary>
    public static bool Mentions(IReadOnlyList<string> textTokens, string stageName)
    {
        var words = SignificantTokens(stageName);
        return words.Count > 0 && words.All(w => textTokens.Any(t => SameWord(t, w)));
    }

    /// <summary>Sentences of a free text (split on . ! ? ; and line breaks), trimmed, non-empty.</summary>
    public static IReadOnlyList<string> Sentences(string? text)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return list;
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if (c is '.' or '!' or '?' or ';' or '\n' or '\r')
            {
                if (sb.ToString().Trim() is { Length: > 0 } s) list.Add(s);
                sb.Clear();
            }
            else if (!char.IsControl(c)) sb.Append(c);
        }
        if (sb.ToString().Trim() is { Length: > 0 } last) list.Add(last);
        return list;
    }

    /// <summary>
    /// Daily log copilot without a language model: the stages whose names are mentioned, each with the (first) sentence of the
    /// report that mentions it — copied, not written. No workforce/equipment/issues inference and no client summary.
    /// </summary>
    public static BuildDailyLogDraft DailyLogFallback(string text, DateOnly date, IReadOnlyList<BuildAiStageRef> stages)
    {
        var sentences = Sentences(text);
        var activities = new List<BuildCopilotActivity>();
        foreach (var stage in stages.OrderBy(s => s.Order).ThenBy(s => s.Name))
        {
            var sentence = sentences.FirstOrDefault(s => Mentions(Tokens(s), stage.Name));
            if (sentence is null) continue;
            activities.Add(new BuildCopilotActivity(stage.Id, stage.Name,
                sentence.Length > BuildAiContracts.MaxText ? sentence[..BuildAiContracts.MaxText] : sentence, null, 0.5m));
        }
        return new BuildDailyLogDraft(date, activities, [], [], [], null, null, activities.Count == 0 ? 0m : 0.5m, 0);
    }

    /// <summary>The IFC classes (and keywords of stage names) of the deterministic model mapper.</summary>
    public static readonly IReadOnlyList<(string[] Classes, string[] Keywords)> MapperTable =
    [
        (["IfcFooting", "IfcPile"], ["funda"]),
        (["IfcWall", "IfcWallStandardCase", "IfcWallElementedCase"], ["alvenaria", "parede"]),
        (["IfcSlab", "IfcBeam", "IfcColumn"], ["estrutura"]),
        (["IfcRoof"], ["cobertura", "telhado"]),
        (["IfcWindow", "IfcDoor"], ["esquadria"]),
        (["IfcCovering"], ["revestimento", "acabamento"]),
        (["IfcFlowSegment", "IfcPipeSegment"], ["hidr"]),
        (["IfcCableSegment"], ["eletr"]),
    ];

    /// <summary>
    /// Model mapper without a language model: for each category present in the model, the first stage (by order) whose folded
    /// name contains one of the class's keywords. Category bindings only.
    /// </summary>
    public static BuildMapperSuggestion MapperFallback(Guid versionId, IReadOnlyCollection<string> categories, IReadOnlyList<BuildAiStageRef> stages)
    {
        var ordered = stages.OrderBy(s => s.Order).ThenBy(s => s.Name).Select(s => (Stage: s, Folded: Fold(s.Name))).ToList();
        var list = new List<BuildMapperBinding>();
        foreach (var category in categories.OrderBy(c => c, StringComparer.Ordinal))
        {
            var row = MapperTable.FirstOrDefault(r => r.Classes.Contains(category, StringComparer.OrdinalIgnoreCase));
            if (row.Classes is null) continue;
            foreach (var keyword in row.Keywords)
            {
                var match = ordered.FirstOrDefault(s => s.Folded.Contains(keyword, StringComparison.Ordinal));
                if (match.Stage is null) continue;
                list.Add(new BuildMapperBinding($"b{list.Count + 1}", match.Stage.Id, match.Stage.Name, "Category", category, null, null, 0.6m,
                    $"Regra: {category} → etapas com \"{keyword}\" no nome."));
                break;
            }
        }
        return new BuildMapperSuggestion(versionId, list, 0);
    }

    /// <summary>A factual client update built only from the numbers and published texts (no generated prose).</summary>
    public static BuildClientUpdateDraft ClientUpdateFallback(BuildClientFacts f, string kind)
    {
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var title = kind == "WeeklyDigest"
            ? string.Create(PtBr, $"Resumo da semana — {f.From:dd/MM} a {f.To:dd/MM}")
            : string.Create(PtBr, $"Atualização da obra — {f.From:dd/MM} a {f.To:dd/MM}");
        var lines = new List<string>();
        var highlights = new List<string>();
        if (f.ActualPercent is { } now)
        {
            var line = $"Avanço geral da obra: {now.ToString("0.#", pt)}%";
            if (f.ActualPercentAtStart is { } start && now > start)
                line += $" (+{(now - start).ToString("0.#", pt)} ponto(s) no período)";
            lines.Add(line + ".");
            highlights.Add($"{now.ToString("0.#", pt)}% concluído");
        }
        if (f.CompletedStages.Count > 0)
        {
            lines.Add("Etapas concluídas no período: " + string.Join(", ", f.CompletedStages) + ".");
            highlights.AddRange(f.CompletedStages.Select(s => $"{s} concluída"));
        }
        foreach (var (date, summary) in f.VisibleLogSummaries.OrderBy(x => x.Date))
            lines.Add(string.Create(PtBr, $"{date:dd/MM}: {summary}"));
        foreach (var co in f.ApprovedChangeOrders)
            lines.Add($"Aditivo nº {co.Number} aprovado: {co.Title}"
                      + (co.ScheduleImpactDays != 0 ? string.Create(PtBr, $" (prazo: {co.ScheduleImpactDays:+0;-0} dia(s))") : "")
                      + (co.CostImpact is { } ci ? $" (valor: {ci.ToString("C", pt)})" : "") + ".");
        foreach (var m in f.ApprovedMeasurements)
            lines.Add(string.Create(PtBr, $"Medição nº {m.Number} aprovada (até {m.PeriodEnd:dd/MM})") + (m.Amount is { } a ? $": {a.ToString("C", pt)}" : "") + ".");
        if (f.UpcomingMilestones.Count > 0)
            lines.Add("Próximas etapas previstas: " + string.Join("; ", f.UpcomingMilestones.Select(m => string.Create(PtBr, $"{m.Stage} (até {m.PlannedEnd:dd/MM})"))) + ".");
        if (f.ForecastDelivery is { } fd)
            lines.Add(string.Create(PtBr, $"Previsão de entrega: {fd:dd/MM/yyyy}."));
        if (lines.Count == 0)
            lines.Add("Sem novidades registradas no período.");
        return new BuildClientUpdateDraft(kind, f.From, f.To, title, string.Join("\n", lines), highlights.Take(10).ToList());
    }
}
