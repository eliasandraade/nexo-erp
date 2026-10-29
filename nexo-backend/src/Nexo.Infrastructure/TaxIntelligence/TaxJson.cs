using System.Text.Json;

namespace Nexo.Infrastructure.TaxIntelligence;

internal static class TaxJson
{
    /// <summary>A detached copy of a stored JSON document, to return as-is in API responses.</summary>
    public static JsonElement Element(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
