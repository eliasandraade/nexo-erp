using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Build;

/// <summary>
/// The public client portal contract as files the Astra team can consume: <c>docs/build/openapi-portal.json</c> (the OpenAPI
/// document of the <c>/api/public/build</c> paths, with the schemas they reference) and <c>docs/build/portal-types.ts</c>
/// (TypeScript types generated from that document). Both are generated from the running API's Swagger document.
///
/// Refresh (from nexo-backend):
/// <code>ORKEN_WRITE_PORTAL_OPENAPI=1 dotnet test tests/Nexo.IntegrationTests --filter "FullyQualifiedName~BuildPortalOpenApiTests"</code>
/// Without the variable the test fails when the committed files differ from the API (contract drift).
/// </summary>
[Collection("Integration")]
public class BuildPortalOpenApiTests
{
    public const string WriteVariable = "ORKEN_WRITE_PORTAL_OPENAPI";
    private const string PublicPrefix = "/api/public/build";
    private const string ContractVersion = "1.6";

    private readonly TestWebApplicationFactory _factory;

    public BuildPortalOpenApiTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Portal_openapi_and_typescript_types_match_the_api()
    {
        var swagger = await _factory.CreateApiClient().GetStringAsync("/swagger/v1/swagger.json");
        var (openApi, types) = Export(swagger);

        // every public action documents its success type and the shared error body
        var doc = JsonNode.Parse(openApi)!;
        var paths = doc["paths"]!.AsObject();
        paths.Count.Should().BeGreaterThan(15);
        foreach (var (path, item) in paths)
            foreach (var (method, op) in item!.AsObject())
            {
                var responses = op!["responses"]!.AsObject();
                responses.Select(r => r.Key).Should().Contain(k => k.StartsWith('2'), $"{method} {path} declares its success");
                responses["404"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>()
                    .Should().Be("#/components/schemas/PublicBuildErrorDto", $"{method} {path} declares the error body");
            }
        var schemas = doc["components"]!["schemas"]!.AsObject().Select(s => s.Key).ToList();
        schemas.Should().Contain(["PublicBuildBootstrapDto", "PublicBuildErrorDto", "PublicBuildTwinStateDto", "PublicBuildDocumentsDto"]);
        // shared value types (curve points, zone geometry) are the only non-public records the public DTOs reuse
        var sharedValueTypes = new[] { "BuildProgressPointDto", "BuildSimplifiedBox", "BuildZonePoint" };
        schemas.Should().OnlyContain(s => s.StartsWith("PublicBuild") || s.StartsWith("CreatePublicBuild") || !s.StartsWith("Build")
                                          || sharedValueTypes.Contains(s), "only public DTOs are part of the portal contract");
        openApi.Should().NotContain("/api/v1/build").And.NotContain("sourceFileKey").And.NotContain("storageKey");
        types.Should().Contain("export interface PublicBuildBootstrapDto").And.Contain("export interface PublicBuildErrorDto");

        var docs = DocsDirectory();
        var jsonPath = Path.Combine(docs, "openapi-portal.json");
        var tsPath = Path.Combine(docs, "portal-types.ts");
        if (Environment.GetEnvironmentVariable(WriteVariable) == "1")
        {
            await File.WriteAllTextAsync(jsonPath, openApi, new UTF8Encoding(false));
            await File.WriteAllTextAsync(tsPath, types, new UTF8Encoding(false));
        }
        File.Exists(jsonPath).Should().BeTrue($"generate it with {WriteVariable}=1");
        Normalize(await File.ReadAllTextAsync(jsonPath)).Should().Be(Normalize(openApi),
            $"docs/build/openapi-portal.json must match the API — regenerate with {WriteVariable}=1");
        Normalize(await File.ReadAllTextAsync(tsPath)).Should().Be(Normalize(types),
            $"docs/build/portal-types.ts must match the API — regenerate with {WriteVariable}=1");
    }

    // ── Export ────────────────────────────────────────────────────────────────

    /// <summary>The public paths with the schemas they reach (transitively), and the TypeScript of those schemas.</summary>
    public static (string OpenApi, string TypeScript) Export(string swaggerJson)
    {
        var root = JsonNode.Parse(swaggerJson)!.AsObject();
        var allSchemas = root["components"]?["schemas"]?.AsObject() ?? new JsonObject();
        var paths = new JsonObject();
        foreach (var (path, item) in root["paths"]!.AsObject().Where(p => p.Key.StartsWith(PublicPrefix, StringComparison.Ordinal))
                     .OrderBy(p => p.Key, StringComparer.Ordinal))
            paths[path] = item!.DeepClone();

        var reached = new SortedSet<string>(StringComparer.Ordinal);
        var queue = new Queue<JsonNode>([paths]);
        while (queue.Count > 0)
            foreach (var name in Refs(queue.Dequeue()))
                if (reached.Add(name) && allSchemas[name] is { } schema)
                    queue.Enqueue(schema);

        var schemas = new JsonObject();
        foreach (var name in reached)
            if (allSchemas[name] is { } schema) schemas[name] = schema.DeepClone();

        var doc = new JsonObject
        {
            ["openapi"] = root["openapi"]?.DeepClone(),
            ["info"] = new JsonObject
            {
                ["title"] = "Orken Build — Portal do Cliente (API pública)",
                ["version"] = ContractVersion,
                ["description"] = "Gerado da API (Swagger) por BuildPortalOpenApiTests. Contrato humano: docs/build/client-portal-contract.md.",
            },
            ["paths"] = paths,
            ["components"] = new JsonObject { ["schemas"] = schemas },
        };
        var json = doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        return (Normalize(json) + "\n", TypeScript(schemas));
    }

    private static IEnumerable<string> Refs(JsonNode node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, value) in o)
                {
                    if (key == "$ref" && value?.GetValue<string>() is { } r && r.StartsWith("#/components/schemas/", StringComparison.Ordinal))
                        yield return r["#/components/schemas/".Length..];
                    else if (value is not null)
                        foreach (var x in Refs(value)) yield return x;
                }
                break;
            case JsonArray a:
                foreach (var item in a)
                    if (item is not null)
                        foreach (var x in Refs(item)) yield return x;
                break;
        }
    }

    private static string TypeScript(JsonObject schemas)
    {
        var sb = new StringBuilder();
        sb.Append("// Orken Build — Portal do Cliente (API pública), contrato v").Append(ContractVersion).Append('\n');
        sb.Append("// GERADO de docs/build/openapi-portal.json por BuildPortalOpenApiTests — não edite à mão.\n");
        sb.Append("// Regenerar (em nexo-backend): ").Append(WriteVariable)
            .Append("=1 dotnet test tests/Nexo.IntegrationTests --filter \"FullyQualifiedName~BuildPortalOpenApiTests\"\n");
        sb.Append("// Campos com `?` podem vir null ou ausentes. Datas: \"yyyy-MM-dd\" (date) ou ISO 8601 (date-time).\n");
        foreach (var (name, node) in schemas)
        {
            var schema = node!.AsObject();
            sb.Append('\n');
            if (schema["description"]?.GetValue<string>() is { Length: > 0 } description)
                sb.Append("/** ").Append(OneLine(description)).Append(" */\n");
            if (schema["enum"] is JsonArray values)
            {
                sb.Append("export type ").Append(name).Append(" = ").Append(EnumUnion(values)).Append(";\n");
                continue;
            }
            sb.Append("export interface ").Append(name).Append(" {\n");
            foreach (var (prop, propNode) in schema["properties"]?.AsObject() ?? new JsonObject())
            {
                var p = propNode!.AsObject();
                var nullable = p["nullable"]?.GetValue<bool>() == true;
                if (p["description"]?.GetValue<string>() is { Length: > 0 } d) sb.Append("  /** ").Append(OneLine(d)).Append(" */\n");
                sb.Append("  ").Append(prop).Append(nullable ? "?: " : ": ").Append(Type(p)).Append(nullable ? " | null" : "").Append(";\n");
            }
            sb.Append("}\n");
        }
        return sb.ToString();
    }

    private static string Type(JsonObject s)
    {
        if (s["$ref"]?.GetValue<string>() is { } r) return r[(r.LastIndexOf('/') + 1)..];
        if (s["allOf"] is JsonArray all && all.Count == 1 && all[0] is JsonObject only) return Type(only);
        if (s["enum"] is JsonArray values) return EnumUnion(values);
        return s["type"]?.GetValue<string>() switch
        {
            "string" => "string",
            "integer" or "number" => "number",
            "boolean" => "boolean",
            "array" => s["items"] is JsonObject items ? Wrap(Type(items)) + "[]" : "unknown[]",
            "object" => s["additionalProperties"] is JsonObject add ? $"Record<string, {Type(add)}>" : "Record<string, unknown>",
            _ => "unknown",
        };
    }

    private static string Wrap(string t) => t.Contains(' ') ? $"({t})" : t;

    private static string EnumUnion(JsonArray values)
        => string.Join(" | ", values.Select(v => v!.GetValueKind() == JsonValueKind.String ? $"\"{v.GetValue<string>()}\"" : v.ToJsonString()));

    private static string OneLine(string text) => text.ReplaceLineEndings(" ").Replace("*/", "* /").Trim();

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd() + "\n";

    private static string DocsDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "build");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "client-portal-contract.md"))) return candidate;
        }
        throw new DirectoryNotFoundException("docs/build not found above the test output directory.");
    }
}
