using System.Text.Json;

namespace Nexo.Application.Modules.Interpreter;

/// <summary>
/// Validates a model's answer against the JSON schema that was requested — the subset the platform uses: <c>type</c>
/// (a name or a list, <c>null</c> included), <c>properties</c>, <c>required</c>, <c>additionalProperties: false</c>,
/// <c>items</c>, <c>enum</c>, <c>minimum</c>/<c>maximum</c>, <c>maxLength</c>, <c>maxItems</c>. An LLM answer is untrusted data:
/// even with structured output enabled, nothing is used before it passes here.
/// </summary>
public static class AiJsonSchemaValidator
{
    public const int MaxErrors = 20;

    /// <summary>The list of violations (empty = valid), each with the JSON path (e.g. <c>$.activities[2].stageId</c>).</summary>
    public static IReadOnlyList<string> Validate(JsonElement value, IReadOnlyDictionary<string, JsonElement> schema)
    {
        var errors = new List<string>();
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(schema));
        Check(value, doc.RootElement, "$", errors);
        return errors;
    }

    public static IReadOnlyList<string> Validate(JsonElement value, JsonElement schema)
    {
        var errors = new List<string>();
        Check(value, schema, "$", errors);
        return errors;
    }

    private static void Check(JsonElement value, JsonElement schema, string path, List<string> errors)
    {
        if (errors.Count >= MaxErrors || schema.ValueKind != JsonValueKind.Object) return;

        if (schema.TryGetProperty("type", out var type) && !TypeMatches(value, type))
        {
            errors.Add($"{path}: tipo {KindName(value)} não permitido ({type.GetRawText()}).");
            return;
        }
        if (schema.TryGetProperty("enum", out var allowed) && allowed.ValueKind == JsonValueKind.Array
            && !allowed.EnumerateArray().Any(a => JsonElementEquals(a, value)))
        {
            errors.Add($"{path}: valor fora da lista permitida.");
            return;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                CheckObject(value, schema, path, errors);
                break;
            case JsonValueKind.Array:
                if (schema.TryGetProperty("maxItems", out var maxItems) && maxItems.TryGetInt32(out var mi) && value.GetArrayLength() > mi)
                    errors.Add($"{path}: mais de {mi} itens.");
                if (schema.TryGetProperty("items", out var items))
                {
                    var i = 0;
                    foreach (var item in value.EnumerateArray()) Check(item, items, $"{path}[{i++}]", errors);
                }
                break;
            case JsonValueKind.Number:
                var d = value.GetDouble();
                if (schema.TryGetProperty("minimum", out var min) && min.ValueKind == JsonValueKind.Number && d < min.GetDouble())
                    errors.Add($"{path}: abaixo do mínimo {min.GetRawText()}.");
                if (schema.TryGetProperty("maximum", out var max) && max.ValueKind == JsonValueKind.Number && d > max.GetDouble())
                    errors.Add($"{path}: acima do máximo {max.GetRawText()}.");
                break;
            case JsonValueKind.String:
                if (schema.TryGetProperty("maxLength", out var ml) && ml.TryGetInt32(out var maxLen) && value.GetString()!.Length > maxLen)
                    errors.Add($"{path}: texto com mais de {maxLen} caracteres.");
                break;
        }
    }

    private static void CheckObject(JsonElement value, JsonElement schema, string path, List<string> errors)
    {
        var props = schema.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object ? p : (JsonElement?)null;
        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
            foreach (var name in required.EnumerateArray().Select(r => r.GetString()).OfType<string>())
                if (!value.TryGetProperty(name, out _))
                    errors.Add($"{path}.{name}: campo obrigatório ausente.");

        var closed = schema.TryGetProperty("additionalProperties", out var ap) && ap.ValueKind == JsonValueKind.False;
        foreach (var prop in value.EnumerateObject())
        {
            if (props is { } ps && ps.TryGetProperty(prop.Name, out var propSchema))
                Check(prop.Value, propSchema, $"{path}.{prop.Name}", errors);
            else if (closed)
                errors.Add($"{path}.{prop.Name}: campo não previsto.");
        }
    }

    private static bool TypeMatches(JsonElement value, JsonElement type) => type.ValueKind switch
    {
        JsonValueKind.String => Is(value, type.GetString()!),
        JsonValueKind.Array => type.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String && Is(value, t.GetString()!)),
        _ => true,
    };

    private static bool Is(JsonElement value, string type) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => false,
    };

    private static string KindName(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.True or JsonValueKind.False => "boolean",
        _ => v.ValueKind.ToString().ToLowerInvariant(),
    };

    private static bool JsonElementEquals(JsonElement a, JsonElement b)
        => a.ValueKind == b.ValueKind && a.GetRawText() == b.GetRawText();
}
