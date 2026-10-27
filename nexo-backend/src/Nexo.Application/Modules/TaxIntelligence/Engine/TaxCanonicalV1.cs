using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexo.Application.Modules.TaxIntelligence.Engine;

/// <summary>
/// Canonical form V1 — FROZEN, part of <see cref="TaxEngineV1"/>'s contract: equal data always gives
/// equal bytes and equal hashes. camelCase names in declaration order, enums by name, no indentation,
/// decimals without trailing zeros (10.50 and 10.5 are the same amount), SHA-256 in lowercase hex.
/// Never edit: a different canonical form is a new class owned by a new engine version, so the hashes
/// of calculations made by V1 stay reproducible.
/// </summary>
public sealed class TaxCanonicalV1 : ITaxCanonicalizer
{
    public static readonly TaxCanonicalV1 Instance = new();

    private TaxCanonicalV1() { }

    public string Id => "orken-canonical/1";

    public JsonSerializerOptions Options { get; } = Build();

    public string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public T Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException($"Empty {typeof(T).Name} document.");

    public string Hash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public string HashOf<T>(T value) => Hash(Serialize(value));

    /// <summary>Fingerprint of a rule version's content — what makes two versions behave the same.</summary>
    public string RuleContentHash(
        string key, int version, DateOnly validFrom, DateOnly? validTo, int priority,
        IReadOnlyList<TaxCondition> conditions, TaxRuleResult result)
        => HashOf(new RuleContent(key, version, validFrom, validTo, priority, conditions, result));

    private sealed record RuleContent(
        string Key, int Version, DateOnly ValidFrom, DateOnly? ValidTo, int Priority,
        IReadOnlyList<TaxCondition> Conditions, TaxRuleResult Result);

    private static JsonSerializerOptions Build()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        o.Converters.Add(new JsonStringEnumConverter());
        o.Converters.Add(new CanonicalDecimalConverter());
        o.MakeReadOnly(populateMissingResolver: true);
        return o;
    }

    private sealed class CanonicalDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.GetDecimal();

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
            => writer.WriteRawValue(value.ToString("0.############################", CultureInfo.InvariantCulture), skipInputValidation: true);
    }
}
