using Microsoft.Extensions.Options;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.Options;

namespace Nexo.Infrastructure.Integrations.Storage;

/// <summary>
/// Composes a public URL as {StorageOptions.R2.PublicUrl}/{storageKey} at read time —
/// the same shape produced by <see cref="CloudflareR2Provider"/> on upload.
/// Returns null when the public base is unset (storage disabled / not configured).
/// </summary>
public sealed class StoragePublicUrlResolver : IStoragePublicUrlResolver
{
    private readonly StorageOptions _opts;

    public StoragePublicUrlResolver(IOptions<StorageOptions> opts) => _opts = opts.Value;

    public string? ResolvePublicUrl(string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey)) return null;

        var baseUrl = _opts.R2.PublicUrl;
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;

        return $"{baseUrl.TrimEnd('/')}/{storageKey.TrimStart('/')}";
    }
}
