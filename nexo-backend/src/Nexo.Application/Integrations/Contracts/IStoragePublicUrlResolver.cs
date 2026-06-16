namespace Nexo.Application.Integrations.Contracts;

/// <summary>
/// Resolves a public URL for a stored object key, composing it from the configured
/// storage public base <em>at read time</em>.
///
/// Why a resolver instead of a persisted URL column: the storage key is the durable
/// primitive. Composing the URL on read means the public domain / CDN / bucket /
/// provider can change without invalidating existing rows — no stale data, no backfill.
///
/// Returns <c>null</c> when storage is not configured (no public base), so callers can
/// render an honest "image unavailable" state instead of a broken link.
/// </summary>
public interface IStoragePublicUrlResolver
{
    string? ResolvePublicUrl(string? storageKey);
}
