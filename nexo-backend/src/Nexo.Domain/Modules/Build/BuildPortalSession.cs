using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

/// <summary>
/// A session of a password-protected client portal. The client holds an opaque random token; only its
/// SHA-256 (hex) is stored. Valid until <see cref="ExpiresAt"/>, unless revoked or the portal's password
/// changed after it was opened (<see cref="PasswordVersion"/> no longer matches).
/// </summary>
public class BuildPortalSession : TenantEntity
{
    private BuildPortalSession() { }
    private BuildPortalSession(Guid tenantId) : base(tenantId) { }

    public Guid      PortalId        { get; private set; }
    public string    TokenHash       { get; private set; } = string.Empty;
    public int       PasswordVersion { get; private set; }
    public DateTime  ExpiresAt       { get; private set; }
    public DateTime? RevokedAt       { get; private set; }

    public static BuildPortalSession Open(Guid tenantId, Guid portalId, string tokenHash, int passwordVersion,
        DateTime nowUtc, TimeSpan lifetime)
    {
        if (portalId == Guid.Empty) throw new DomainException("Portal inválido.");
        if (string.IsNullOrWhiteSpace(tokenHash) || tokenHash.Length != 64) throw new DomainException("Sessão inválida.");
        if (lifetime <= TimeSpan.Zero) throw new DomainException("Duração de sessão inválida.");
        return new BuildPortalSession(tenantId)
        {
            PortalId        = portalId,
            TokenHash       = tokenHash,
            PasswordVersion = passwordVersion,
            ExpiresAt       = nowUtc.Add(lifetime),
        };
    }

    public bool IsValid(DateTime nowUtc, int currentPasswordVersion)
        => RevokedAt is null && ExpiresAt > nowUtc && PasswordVersion == currentPasswordVersion;

    public void Revoke(DateTime nowUtc)
    {
        if (RevokedAt is not null) return;
        RevokedAt = nowUtc;
        SetUpdatedAt();
    }
}
