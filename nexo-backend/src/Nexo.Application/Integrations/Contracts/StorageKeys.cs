namespace Nexo.Application.Integrations.Contracts;

/// <summary>
/// How object keys are laid out. Every key starts with the tenant (<c>tenants/{tenantId}/…</c>);
/// private files (records, site photos) also start with <c>private/</c> and are only ever handed
/// out as short-lived signed URLs — never through the public base URL.
/// </summary>
public static class StorageKeys
{
    public const string PrivatePrefix = "private/";

    public static string Build(Guid tenantId, string pathSegment, string fileName, bool isPrivate)
        => $"{(isPrivate ? PrivatePrefix : string.Empty)}tenants/{tenantId}/{pathSegment}/{fileName}";

    public static bool IsPrivate(string key) => key.StartsWith(PrivatePrefix, StringComparison.Ordinal);

    /// <summary>The tenant's own folder (private or public) — what a tenant may delete or sign.</summary>
    public static bool IsOfTenant(string? key, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains("..", StringComparison.Ordinal)) return false;
        var rest = IsPrivate(key) ? key[PrivatePrefix.Length..] : key;
        return rest.StartsWith($"tenants/{tenantId}/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A key that may be attached to a record of this tenant in this place (e.g. "service/records"):
    /// the tenant's private folder for that place. Keys of another tenant, of another place or
    /// invented by the caller are refused.
    /// </summary>
    public static bool IsPrivateOf(string? key, Guid tenantId, string pathSegment)
        => !string.IsNullOrWhiteSpace(key)
           && !key.Contains("..", StringComparison.Ordinal)
           && key.StartsWith($"{PrivatePrefix}tenants/{tenantId}/{pathSegment}/", StringComparison.OrdinalIgnoreCase)
           && key.Length > $"{PrivatePrefix}tenants/{tenantId}/{pathSegment}/".Length;
}

/// <summary>Short-lived signed URLs for private files (records, site photos).</summary>
public interface IPrivateFileUrls
{
    /// <summary>A URL valid for a few minutes, or null when storage is off or the key is blank.</summary>
    string? For(string? storageKey);
}
