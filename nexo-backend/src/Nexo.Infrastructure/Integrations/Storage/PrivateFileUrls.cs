using Microsoft.Extensions.Logging;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.Options;

namespace Nexo.Infrastructure.Integrations.Storage;

/// <summary>
/// Signed URLs for private files, valid for <see cref="Ttl"/>. A screen that shows a record or a site
/// photo gets a fresh URL each time it loads it; a URL copied out of the app stops working minutes
/// later. Signing is local (no request to the provider), so this is cheap.
/// </summary>
public sealed class PrivateFileUrls : IPrivateFileUrls
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    private readonly IStorageProvider _storage;
    private readonly IIntegrationFeatureFlags _flags;
    private readonly ILogger<PrivateFileUrls> _logger;

    public PrivateFileUrls(IStorageProvider storage, IIntegrationFeatureFlags flags, ILogger<PrivateFileUrls> logger)
    {
        _storage = storage;
        _flags   = flags;
        _logger  = logger;
    }

    public string? For(string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || !_flags.StorageEnabled) return null;
        try
        {
            return _storage.GetSignedUrl(storageKey, Ttl);
        }
        catch (Exception ex)
        {
            // misconfigured credentials: show "file unavailable", never fail the whole screen
            _logger.LogWarning(ex, "Could not sign a private file URL.");
            return null;
        }
    }
}
