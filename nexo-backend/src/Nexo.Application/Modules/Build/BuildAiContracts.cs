using System.Globalization;
using System.Text.Json;
using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

/// <summary>A stage as the AI capabilities see it (ids are the only references the model may use).</summary>
public sealed record BuildAiStageRef(Guid Id, string Name, int Order, int Percent);

// ── Canonical (validated) outputs — what is stored in OutputJson ─────────────

public sealed record BuildCopilotActivity(Guid? StageId, string? StageName, string Description, int? ProgressHint, decimal Confidence);
public sealed record BuildCopilotWorkforce(string Role, int Count);
public sealed record BuildDailyLogDraft(DateOnly Date, IReadOnlyList<BuildCopilotActivity> Activities, IReadOnlyList<BuildCopilotWorkforce> Workforce,
    IReadOnlyList<string> Equipment, IReadOnlyList<string> Issues, string? WeatherNote, string? ClientSummary, decimal Confidence, int DiscardedRefs);

public sealed record BuildProgressSuggestionItem(string Id, Guid StageId, string StageName, int CurrentPercent, int SuggestedPercent,
    string Rationale, IReadOnlyList<string> EvidenceRefs, decimal Confidence);
public sealed record BuildProgressSuggestion(IReadOnlyList<BuildProgressSuggestionItem> Items, int DiscardedRefs);

public sealed record BuildMapperBinding(string Id, Guid StageId, string StageName, string Scope, string? Category, Guid? ZoneId, string? ZoneName,
    decimal Confidence, string Rationale);
public sealed record BuildMapperSuggestion(Guid ModelVersionId, IReadOnlyList<BuildMapperBinding> Bindings, int DiscardedRefs);

public sealed record BuildClientUpdateDraft(string Kind, DateOnly From, DateOnly To, string Title, string Body, IReadOnlyList<string> Highlights);

public sealed record BuildRiskItemExplanation(string Key, string Explanation);
public sealed record BuildRiskAction(string Text, IReadOnlyList<string> ItemKeys);
public sealed record BuildRiskExplanation(string Kind, string Summary, IReadOnlyList<BuildRiskItemExplanation> Items,
    IReadOnlyList<BuildRiskAction> RecommendedActions, BuildRiskAnalysisDto Analysis, int DiscardedRefs);

public sealed record BuildDocumentItem(string Description, decimal? Quantity, decimal? UnitPrice, decimal? Total);
public sealed record BuildDocumentExtraction(string? DocumentType, string? Issuer, string? IssuerCnpj, string? Number, DateOnly? IssueDate,
    DateOnly? DueDate, DateOnly? ValidUntil, decimal? TotalAmount, IReadOnlyList<BuildDocumentItem> Items, string? Summary, decimal Confidence);

/// <summary>
/// The JSON contracts of the Build AI capabilities: the schema sent with each call and the post-validation of the answer —
/// every id must be one Orken sent (invented ids are dropped and counted in <c>DiscardedRefs</c>), percentages and amounts are
/// bounded, texts are cleaned and capped. Pure: unit-tested without a provider.
/// </summary>
public static class BuildAiContracts
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public const int MaxText = 2000;
    public const int MaxShort = 300;
    public const decimal MaxAmount = 1_000_000_000m;

    private static Dictionary<string, JsonElement> Schema(string json)
        => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    // ── Schemas ──────────────────────────────────────────────────────────────

    public static Dictionary<string, JsonElement> DailyLogSchema => Schema("""
        {"type":"object","additionalProperties":false,
         "required":["activities","workforce","equipment","issues","weatherNote","clientSummary","confidence"],
         "properties":{
           "activities":{"type":"array","items":{"type":"object","additionalProperties":false,
              "required":["stageId","description","progressHint","confidence"],
              "properties":{"stageId":{"type":["string","null"]},"description":{"type":"string"},
                            "progressHint":{"type":["number","null"]},"confidence":{"type":"number"}}}},
           "workforce":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["role","count"],
              "properties":{"role":{"type":"string"},"count":{"type":"number"}}}},
           "equipment":{"type":"array","items":{"type":"string"}},
           "issues":{"type":"array","items":{"type":"string"}},
           "weatherNote":{"type":["string","null"]},
           "clientSummary":{"type":["string","null"]},
           "confidence":{"type":"number"}}}
        """);

    public static Dictionary<string, JsonElement> ProgressSchema => Schema("""
        {"type":"object","additionalProperties":false,"required":["items"],
         "properties":{"items":{"type":"array","items":{"type":"object","additionalProperties":false,
            "required":["stageId","suggestedPercent","rationale","evidenceRefs","confidence"],
            "properties":{"stageId":{"type":"string"},"suggestedPercent":{"type":"number"},"rationale":{"type":"string"},
                          "evidenceRefs":{"type":"array","items":{"type":"string"}},"confidence":{"type":"number"}}}}}}
        """);

    public static Dictionary<string, JsonElement> MapperSchema => Schema("""
        {"type":"object","additionalProperties":false,"required":["bindings"],
         "properties":{"bindings":{"type":"array","items":{"type":"object","additionalProperties":false,
            "required":["stageId","scope","category","zoneId","confidence","rationale"],
            "properties":{"stageId":{"type":"string"},"scope":{"type":"string","enum":["Category","Zone"]},
                          "category":{"type":["string","null"]},"zoneId":{"type":["string","null"]},
                          "confidence":{"type":"number"},"rationale":{"type":"string"}}}}}}
        """);

    public static Dictionary<string, JsonElement> ClientUpdateSchema => Schema("""
        {"type":"object","additionalProperties":false,"required":["title","body","highlights"],
         "properties":{"title":{"type":"string"},"body":{"type":"string"},"highlights":{"type":"array","items":{"type":"string"}}}}
        """);

    public static Dictionary<string, JsonElement> RiskExplainSchema => Schema("""
        {"type":"object","additionalProperties":false,"required":["summary","items","recommendedActions"],
         "properties":{"summary":{"type":"string"},
           "items":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["key","explanation"],
              "properties":{"key":{"type":"string"},"explanation":{"type":"string"}}}},
           "recommendedActions":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["text","itemKeys"],
              "properties":{"text":{"type":"string"},"itemKeys":{"type":"array","items":{"type":"string"}}}}}}}
        """);

    public static Dictionary<string, JsonElement> DocumentSchema => Schema("""
        {"type":"object","additionalProperties":false,
         "required":["documentType","issuer","issuerCnpj","number","issueDate","dueDate","validUntil","totalAmount","items","summary","confidence"],
         "properties":{
           "documentType":{"type":["string","null"],"enum":["Contrato","Projeto","ArtRrt","Alvara","NotaFiscal","Laudo","Manual","Garantia","Foto","Outro",null]},
           "issuer":{"type":["string","null"]},"issuerCnpj":{"type":["string","null"]},"number":{"type":["string","null"]},
           "issueDate":{"type":["string","null"]},"dueDate":{"type":["string","null"]},"validUntil":{"type":["string","null"]},
           "totalAmount":{"type":["number","null"]},
           "items":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["description","quantity","unitPrice","total"],
              "properties":{"description":{"type":"string"},"quantity":{"type":["number","null"]},"unitPrice":{"type":["number","null"]},"total":{"type":["number","null"]}}}},
           "summary":{"type":["string","null"]},"confidence":{"type":"number"}}}
        """);

    // ── Parsing / filtering ──────────────────────────────────────────────────

    /// <summary>Copilot: activities keep their text; a stage id that is not one of the project's is dropped (counted).</summary>
    public static BuildDailyLogDraft ParseDailyLog(JsonElement o, DateOnly date, IReadOnlyList<BuildAiStageRef> stages)
    {
        var byId = stages.ToDictionary(s => s.Id);
        var discarded = 0;
        var activities = new List<BuildCopilotActivity>();
        foreach (var a in Array(o, "activities").Take(50))
        {
            var description = Text(a, "description", MaxText);
            if (description is null) continue;
            Guid? stageId = null;
            if (Str(a, "stageId") is { } raw)
            {
                if (Guid.TryParse(raw, out var g) && byId.ContainsKey(g)) stageId = g;
                else discarded++;
            }
            var hint = Num(a, "progressHint") is { } h ? (int?)Math.Clamp((int)Math.Round(h), 0, 100) : null;
            activities.Add(new BuildCopilotActivity(stageId, stageId is { } s ? byId[s].Name : null, description, hint, Confidence(a)));
        }
        var workforce = Array(o, "workforce").Take(30)
            .Select(w => (Role: Text(w, "role", 100), Count: Num(w, "count")))
            .Where(w => w.Role is not null && w.Count is > 0 and < 1000)
            .Select(w => new BuildCopilotWorkforce(w.Role!, (int)Math.Round(w.Count!.Value))).ToList();
        return new BuildDailyLogDraft(date, activities, workforce, Strings(o, "equipment", 30, MaxShort), Strings(o, "issues", 30, MaxText),
            Text(o, "weatherNote", MaxShort), Text(o, "clientSummary", MaxText), Confidence(o), discarded);
    }

    /// <summary>
    /// Progress: only the project's stages, suggested % ≥ the current one (lower → dropped) and ≤ 100, evidence ids restricted
    /// to the ones Orken sent. One item per stage (the first wins).
    /// </summary>
    public static BuildProgressSuggestion ParseProgress(JsonElement o, IReadOnlyList<BuildAiStageRef> stages, IReadOnlySet<string> evidenceIds)
    {
        var byId = stages.ToDictionary(s => s.Id);
        var discarded = 0;
        var items = new List<BuildProgressSuggestionItem>();
        foreach (var i in Array(o, "items").Take(200))
        {
            if (!Guid.TryParse(Str(i, "stageId"), out var id) || !byId.TryGetValue(id, out var stage) || items.Any(x => x.StageId == id))
            {
                discarded++;
                continue;
            }
            var pct = Num(i, "suggestedPercent") is { } p ? (int)Math.Round(Math.Clamp(p, 0, 100)) : -1;
            if (pct < stage.Percent || pct == stage.Percent) continue;   // never backwards, nothing to change
            var evidence = Strings(i, "evidenceRefs", 50, 100);
            var kept = evidence.Where(evidenceIds.Contains).ToList();
            discarded += evidence.Count - kept.Count;
            items.Add(new BuildProgressSuggestionItem(id.ToString(), id, stage.Name, stage.Percent, pct,
                Text(i, "rationale", MaxText) ?? "", kept, Confidence(i)));
        }
        return new BuildProgressSuggestion(items, discarded);
    }

    /// <summary>Mapper: stage, category and zone must be the ones sent; duplicates collapse.</summary>
    public static BuildMapperSuggestion ParseMapper(JsonElement o, Guid versionId, IReadOnlyList<BuildAiStageRef> stages,
        IReadOnlySet<string> categories, IReadOnlyDictionary<Guid, string> zones)
    {
        var byId = stages.ToDictionary(s => s.Id);
        var discarded = 0;
        var list = new List<BuildMapperBinding>();
        foreach (var b in Array(o, "bindings").Take(300))
        {
            if (!Guid.TryParse(Str(b, "stageId"), out var sid) || !byId.TryGetValue(sid, out var stage)) { discarded++; continue; }
            var scope = Str(b, "scope");
            string? category = null;
            Guid? zoneId = null;
            if (scope == "Category")
            {
                category = Str(b, "category");
                if (category is null || !categories.Contains(category)) { discarded++; continue; }
            }
            else if (scope == "Zone")
            {
                if (!Guid.TryParse(Str(b, "zoneId"), out var z) || !zones.ContainsKey(z)) { discarded++; continue; }
                zoneId = z;
            }
            else { discarded++; continue; }
            if (list.Any(x => x.StageId == sid && x.Scope == scope && x.Category == category && x.ZoneId == zoneId)) continue;
            list.Add(new BuildMapperBinding($"b{list.Count + 1}", sid, stage.Name, scope!, category, zoneId,
                zoneId is { } zz ? zones[zz] : null, Confidence(b), Text(b, "rationale", MaxShort) ?? ""));
        }
        return new BuildMapperSuggestion(versionId, list, discarded);
    }

    public static BuildClientUpdateDraft ParseClientUpdate(JsonElement o, string kind, DateOnly from, DateOnly to)
    {
        var title = Text(o, "title", 200) ?? $"Atualização da obra — {from:dd/MM} a {to:dd/MM}";
        var body = Text(o, "body", 10000, keepNewLines: true)
                   ?? throw new Nexo.Domain.Exceptions.DomainException("A IA não escreveu o texto da atualização.", "ai_empty_output");
        return new BuildClientUpdateDraft(kind, from, to, title, body, Strings(o, "highlights", 10, MaxShort));
    }

    /// <summary>The LLM explains only items Orken computed: unknown keys are dropped, it cannot add items nor numbers of its own.</summary>
    public static BuildRiskExplanation ParseRiskExplanation(JsonElement o, BuildRiskAnalysisDto analysis)
    {
        var keys = analysis.Items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
        var discarded = 0;
        var items = new List<BuildRiskItemExplanation>();
        foreach (var i in Array(o, "items").Take(100))
        {
            var key = Str(i, "key");
            if (key is null || !keys.Contains(key) || items.Any(x => x.Key == key)) { discarded++; continue; }
            if (Text(i, "explanation", MaxText) is { } text) items.Add(new BuildRiskItemExplanation(key, text));
        }
        var actions = new List<BuildRiskAction>();
        foreach (var a in Array(o, "recommendedActions").Take(20))
        {
            var refs = Strings(a, "itemKeys", 20, 200);
            var kept = refs.Where(keys.Contains).ToList();
            discarded += refs.Count - kept.Count;
            if (Text(a, "text", MaxShort * 2) is { } t) actions.Add(new BuildRiskAction(t, kept));
        }
        return new BuildRiskExplanation(analysis.Kind, Text(o, "summary", MaxText, keepNewLines: true) ?? "", items, actions, analysis, discarded);
    }

    public static BuildDocumentExtraction ParseDocument(JsonElement o)
    {
        var cnpj = Str(o, "issuerCnpj") is { } c ? new string(c.Where(char.IsAsciiDigit).ToArray()) : null;
        var items = Array(o, "items").Take(200)
            .Select(i => (Desc: Text(i, "description", MaxShort), i))
            .Where(x => x.Desc is not null)
            .Select(x => new BuildDocumentItem(x.Desc!, Amount(x.i, "quantity"), Amount(x.i, "unitPrice"), Amount(x.i, "total"))).ToList();
        return new BuildDocumentExtraction(
            Str(o, "documentType") is { } t && Enum.TryParse<BuildDocumentCategory>(t, out var cat) && Enum.IsDefined(cat) ? cat.ToString() : null,
            Text(o, "issuer", MaxShort), cnpj is { Length: 14 } ? cnpj : null, Text(o, "number", 100),
            Date(o, "issueDate"), Date(o, "dueDate"), Date(o, "validUntil"), Amount(o, "totalAmount"), items,
            Text(o, "summary", MaxText), Confidence(o));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Json)!;

    private static IEnumerable<JsonElement> Array(JsonElement o, string name)
        => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];

    private static string? Str(JsonElement o, string name)
        => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!.Trim() is { Length: > 0 } s ? s : null
            : null;

    private static double? Num(JsonElement o, string name)
        => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
           && v.TryGetDouble(out var d) && !double.IsNaN(d) && !double.IsInfinity(d) ? d : null;

    /// <summary>Text without control characters (new lines optionally kept), trimmed; empty → null; capped at <paramref name="max"/>.</summary>
    public static string? Text(JsonElement o, string name, int max, bool keepNewLines = false) => Clean(Str(o, name), max, keepNewLines);

    public static string? Clean(string? s, int max, bool keepNewLines = false)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var clean = new string(s.Where(ch => !char.IsControl(ch) || (keepNewLines && ch == '\n')).ToArray()).Trim();
        if (clean.Length == 0) return null;
        return clean.Length > max ? clean[..max] : clean;
    }

    private static IReadOnlyList<string> Strings(JsonElement o, string name, int maxItems, int maxLen)
        => Array(o, name).Where(e => e.ValueKind == JsonValueKind.String).Select(e => Clean(e.GetString(), maxLen)).OfType<string>()
            .Distinct().Take(maxItems).ToList();

    private static decimal Confidence(JsonElement o)
        => Num(o, "confidence") is { } d ? Math.Round((decimal)Math.Clamp(d, 0, 1), 3) : 0m;

    private static decimal? Amount(JsonElement o, string name)
    {
        if (Num(o, name) is not { } d || d < 0 || d >= (double)MaxAmount) return null;
        return Math.Round((decimal)d, 2, MidpointRounding.AwayFromZero);
    }

    private static DateOnly? Date(JsonElement o, string name)
        => Str(o, name) is { } s && DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
           && d.Year is >= 2000 and <= 2100 ? d : null;
}
