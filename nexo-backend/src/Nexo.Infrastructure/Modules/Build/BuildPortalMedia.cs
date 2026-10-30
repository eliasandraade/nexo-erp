using Microsoft.Extensions.Logging;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.Options;
using Nexo.Infrastructure.Integrations.Storage;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// URLs of the client portal's media, always checked against an explicit tenant (the portal's — a public
/// request has no current tenant). Branding (logo, cover) lives in the tenant's public portal folder and
/// gets a public URL; site photos stay private and only ever get a signed URL valid for
/// <see cref="PrivateFileUrls.Ttl"/>.
/// </summary>
public sealed class BuildPortalMedia
{
    public const string PortalFolder = "build/portal/";

    private readonly IStoragePublicUrlResolver _publicUrls;
    private readonly IStorageProvider _storage;
    private readonly IIntegrationFeatureFlags _flags;
    private readonly ILogger<BuildPortalMedia> _logger;

    public BuildPortalMedia(IStoragePublicUrlResolver publicUrls, IStorageProvider storage,
        IIntegrationFeatureFlags flags, ILogger<BuildPortalMedia> logger)
    {
        _publicUrls = publicUrls;
        _storage = storage;
        _flags = flags;
        _logger = logger;
    }

    public static TimeSpan SignedUrlTtl => PrivateFileUrls.Ttl;

    /// <summary>A branding key the company may use: its own public portal folder (upload contexts build-portal-logo/cover).</summary>
    public static bool IsBrandingKeyOf(string? key, Guid tenantId)
        => !string.IsNullOrWhiteSpace(key)
           && !StorageKeys.IsPrivate(key)
           && StorageKeys.IsOfTenant(key, tenantId)
           && key.StartsWith($"tenants/{tenantId}/{PortalFolder}", StringComparison.Ordinal);

    public string? BrandingUrl(string? key, Guid tenantId)
        => IsBrandingKeyOf(key, tenantId) ? _publicUrls.ResolvePublicUrl(key) : null;

    /// <summary>A short-lived signed URL for a private file of <paramref name="tenantId"/>, or null.</summary>
    public string? SignedUrl(string? key, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(key) || !_flags.StorageEnabled) return null;
        if (!StorageKeys.IsOfTenant(key, tenantId)) return null;
        try
        {
            return _storage.GetSignedUrl(key, SignedUrlTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not sign a client portal file URL.");
            return null;
        }
    }
}
