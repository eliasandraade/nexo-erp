using Nexo.Domain.Common;

namespace Nexo.Domain.Entities;

/// <summary>
/// Andrade Systems platform administrator.
/// Has access to the /platform portal — can manage tenants, grant module subscriptions, etc.
/// These users are NOT tenant-scoped (no TenantId).
/// </summary>
public class PlatformUser : BaseEntity
{
    private PlatformUser() { } // EF Core constructor

    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>"super_admin" | "support"</summary>
    public string Role { get; private set; } = "support";

    public static PlatformUser Create(string email, string passwordHash, string role = "support")
    {
        return new PlatformUser
        {
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            Role = role,
        };
    }

    public void ChangePasswordHash(string newHash)
    {
        PasswordHash = newHash;
        SetUpdatedAt();
    }
}
