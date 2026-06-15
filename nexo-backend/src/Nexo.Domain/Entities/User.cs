using Nexo.Domain.Common;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Entities;

/// <summary>
/// Represents a user belonging to a specific tenant.
/// Login is the unique identifier for authentication within a tenant (not email).
/// PasswordHash is always a BCrypt hash — never store plaintext.
/// </summary>
public class User : TenantEntity
{
    private User() { } // EF Core constructor

    public string FullName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Login { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public UserRole Role { get; private set; }
    public UserStatus Status { get; private set; }
    public bool RequirePasswordChange { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? LastAccessAt { get; private set; }
    public DateTime? PasswordChangedAt { get; private set; }

    /// <summary>
    /// Opaque stamp that changes on password reset or force-logout.
    /// Included in JWT claims. If the stored stamp differs from the token's,
    /// the session is considered revoked.
    /// </summary>
    public string SecurityStamp { get; private set; } = Guid.NewGuid().ToString("N");

    // Email verification — stored in PostgreSQL (not Redis) so token survives Redis failures.
    public string? VerificationToken { get; private set; }
    public DateTime? VerificationTokenExpiry { get; private set; }

    // Navigation
    public Tenant? Tenant { get; private set; }

    public static User Create(
        Guid tenantId,
        string fullName,
        string email,
        string login,
        string passwordHash,
        UserRole role,
        string? phone = null,
        string? notes = null,
        bool requirePasswordChange = false)
    {
        return new User(tenantId)
        {
            FullName = fullName.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            Login = login.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            Phone = phone?.Trim(),
            Role = role,
            Status = UserStatus.Active,
            RequirePasswordChange = requirePasswordChange,
            Notes = notes?.Trim(),
        };
    }

    private User(Guid tenantId) : base(tenantId) { }

    public void UpdateProfile(
        string fullName,
        string email,
        string? phone,
        UserRole role,
        string? notes)
    {
        FullName = fullName.Trim();
        Email = email.Trim().ToLowerInvariant();
        Phone = phone?.Trim();
        Role = role;
        Notes = notes?.Trim();
        SetUpdatedAt();
    }

    public void ChangePasswordHash(string newHash, bool clearRequireChange = true)
    {
        PasswordHash = newHash;
        PasswordChangedAt = DateTime.UtcNow;
        if (clearRequireChange) RequirePasswordChange = false;
        SetUpdatedAt();
    }

    public void SetStatus(UserStatus status)
    {
        Status = status;
        SetUpdatedAt();
    }

    public void Activate()
    {
        Status = UserStatus.Active;
        SetUpdatedAt();
    }

    public void RecordAccess()
    {
        LastAccessAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    /// <summary>
    /// Rotates the SecurityStamp, invalidating all existing JWTs for this user.
    /// Call on password reset or forced logout.
    /// </summary>
    public void BumpSecurityStamp()
    {
        SecurityStamp = Guid.NewGuid().ToString("N");
        SetUpdatedAt();
    }

    public void SetVerificationToken(string token, DateTime expiry)
    {
        VerificationToken       = token;
        VerificationTokenExpiry = expiry;
        SetUpdatedAt();
    }

    public void ClearVerificationToken()
    {
        VerificationToken       = null;
        VerificationTokenExpiry = null;
        SetUpdatedAt();
    }

    /// <summary>Returns true if this user has a management role (Gerente or Diretoria).</summary>
    public bool IsManager() =>
        Role == UserRole.Gerente || Role == UserRole.Diretoria;

    /// <summary>Throws ForbiddenException if the user cannot authorize management actions.</summary>
    public void EnsureCanAuthorize()
    {
        if (!IsManager())
            throw new ForbiddenException("Only Gerente or Diretoria users can authorize this action.");
        if (Status != UserStatus.Active)
            throw new ForbiddenException("Inactive or blocked users cannot authorize actions.");
    }
}
