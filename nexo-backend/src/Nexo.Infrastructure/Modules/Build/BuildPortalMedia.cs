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

    /// <summary>The check behind <see cref="SignedUrl"/>: the tenant's PRIVATE folder for that section.</summary>
    public static bool IsSignable(string? key, Guid tenantId, string expectedFolder)
        => !string.IsNullOrWhiteSpace(expectedFolder) && StorageKeys.IsPrivateOf(key, tenantId, expectedFolder.Trim('/'));

    public string? BrandingUrl(string? key, Guid tenantId)
        => IsBrandingKeyOf(key, tenantId) ? _publicUrls.ResolvePublicUrl(key) : null;

    /// <summary>
    /// A short-lived signed URL for a private file of <paramref name="tenantId"/> in the private folder the section's files
    /// are uploaded to (<paramref name="expectedFolder"/>, e.g. <c>build/daily-logs</c>), or null. A key of another tenant,
    /// a public key or a key of another folder (a record pointing at a file it does not own) is never signed.
    /// </summary>
    public string? SignedUrl(string? key, Guid tenantId, string expectedFolder)
    {
        if (string.IsNullOrWhiteSpace(key) || !_flags.StorageEnabled) return null;
        if (!IsSignable(key, tenantId, expectedFolder)) return null;
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
