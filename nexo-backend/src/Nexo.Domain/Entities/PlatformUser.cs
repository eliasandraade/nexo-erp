using Nexo.Domain.Common;

namespace Nexo.Domain.Entities;

/// <summary>
/// Andrade Systems platform administrator.
/// Has access to the /platform portal — can manage tenants, grant module subscriptions, etc.
/// These users are NOT tenant-scoped (no TenantId).
/// Sign-in is a revocable session (<see cref="PlatformSession"/>) bound to <see cref="SecurityStamp"/>; two-step verification
/// (TOTP + recovery codes) uses the same primitives as the tenant users.
/// </summary>
public class PlatformUser : BaseEntity
{
    private PlatformUser() { } // EF Core constructor

    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>"super_admin" | "support"</summary>
    public string Role { get; private set; } = "support";

    /// <summary>Changes whenever every session must end (password changed, two-step turned on or off, admin reset).</summary>
    public string SecurityStamp { get; private set; } = Guid.NewGuid().ToString("N");

    public bool MfaEnabled { get; private set; }
    /// <summary>Encrypted TOTP secret in use (IMfaSecretProtector, bound to this user's id).</summary>
    public string? MfaSecret { get; private set; }
    /// <summary>Encrypted secret being set up — becomes <see cref="MfaSecret"/> once a code proves it.</summary>
    public string? MfaPendingSecret { get; private set; }
    /// <summary>Last TOTP time step accepted: the same code (or an older one) never works twice.</summary>
    public long? MfaLastUsedStep { get; private set; }
    /// <summary>Hashes of the unused recovery codes, ';'-separated.</summary>
    public string? MfaRecoveryCodes { get; private set; }

    public static PlatformUser Create(string email, string passwordHash, string role = "support")
    {
        return new PlatformUser
        {
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            Role = role,
        };
    }

    public bool IsSuperAdmin => Role == "super_admin";

    /// <summary>New password: every existing session ends.</summary>
    public void ChangePasswordHash(string newHash)
    {
        PasswordHash = newHash;
        BumpSecurityStamp();
    }

    public void BumpSecurityStamp()
    {
        SecurityStamp = Guid.NewGuid().ToString("N");
        SetUpdatedAt();
    }

    public void StartMfaSetup(string protectedSecret)
    {
        MfaPendingSecret = protectedSecret;
        SetUpdatedAt();
    }

    public void EnableMfa(string protectedSecret, long usedStep, IEnumerable<string> recoveryCodeHashes)
    {
        MfaEnabled = true;
        MfaSecret = protectedSecret;
        MfaPendingSecret = null;
        MfaLastUsedStep = usedStep;
        MfaRecoveryCodes = string.Join(';', recoveryCodeHashes);
        BumpSecurityStamp();
    }

    public void DisableMfa()
    {
        MfaEnabled = false;
        MfaSecret = null;
        MfaPendingSecret = null;
        MfaLastUsedStep = null;
        MfaRecoveryCodes = null;
        BumpSecurityStamp();
    }

    public void RecordMfaStep(long step)
    {
        MfaLastUsedStep = step;
        SetUpdatedAt();
    }

    /// <summary>Consumes a recovery code (by hash); false when it isn't one of the unused ones.</summary>
    public bool UseRecoveryCode(string codeHash)
    {
        var codes = (MfaRecoveryCodes ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!codes.Remove(codeHash)) return false;
        MfaRecoveryCodes = string.Join(';', codes);
        SetUpdatedAt();
        return true;
    }

    public int MfaRecoveryCodesLeft => (MfaRecoveryCodes ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries).Length;
}

/// <summary>
/// One sign-in of a platform administrator: identifiable, revocable, bound to the user's security stamp. The refresh token
/// is kept only as its SHA-256; it rotates on every use and a reused (stolen) one ends the session.
/// </summary>
public class PlatformSession : BaseEntity
{
    private PlatformSession() { }

    public Guid PlatformUserId { get; private set; }
    public string RefreshTokenHash { get; private set; } = string.Empty;
    /// <summary>The hash the last rotation replaced: presented again (outside the short grace of another tab) it is a theft.</summary>
    public string? PreviousRefreshTokenHash { get; private set; }
    public DateTime? RotatedAt { get; private set; }
    /// <summary>The stamp of the user when the session started; a different current stamp means the session is over.</summary>
    public string SecurityStamp { get; private set; } = string.Empty;
    /// <summary>True when the sign-in passed two-step verification.</summary>
    public bool MfaVerified { get; private set; }
    /// <summary>
    /// The password was right and the second step is still due: the row then holds the challenge (its hash), lives a few
    /// minutes and grants nothing. Kept in the database, so a cache outage never locks an administrator out.
    /// </summary>
    public bool PendingMfa { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime LastUsedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }

    public static PlatformSession Start(Guid platformUserId, string refreshTokenHash, string securityStamp, bool mfaVerified,
        DateTime expiresAt, string? ipAddress, string? userAgent, bool pendingMfa = false)
        => new()
        {
            PlatformUserId = platformUserId,
            RefreshTokenHash = refreshTokenHash,
            SecurityStamp = securityStamp,
            MfaVerified = mfaVerified,
            PendingMfa = pendingMfa,
            ExpiresAt = expiresAt,
            LastUsedAt = DateTime.UtcNow,
            IpAddress = ipAddress is { Length: > 64 } ? ipAddress[..64] : ipAddress,
            UserAgent = userAgent is { Length: > 300 } ? userAgent[..300] : userAgent,
        };

    public bool IsActiveAt(DateTime now, string currentStamp)
        => !PendingMfa && RevokedAt is null && ExpiresAt > now && SecurityStamp == currentStamp;

    /// <summary>The second step passed: the pending row becomes the session, with its first refresh token.</summary>
    public void CompleteMfa(string refreshTokenHash, DateTime expiresAt)
    {
        PendingMfa = false;
        MfaVerified = true;
        RefreshTokenHash = refreshTokenHash;
        ExpiresAt = expiresAt;
        LastUsedAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void Revoke(string reason)
    {
        if (RevokedAt is not null) return;
        RevokedAt = DateTime.UtcNow;
        RevokedReason = reason.Length > 60 ? reason[..60] : reason;
        SetUpdatedAt();
    }
}
