using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Domain.Entities;
using Nexo.Domain.Exceptions;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Auth;

/// <summary>Where a platform sign-in stands.</summary>
public sealed record PlatformSignIn(PlatformTokens? Tokens, string? MfaChallenge, string? ErrorCode, PlatformUser? User);

public sealed record PlatformTokens(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt);

public sealed record PlatformSessionView(Guid Id, DateTime CreatedAt, DateTime LastUsedAt, DateTime ExpiresAt, bool MfaVerified,
    string? IpAddress, string? UserAgent, bool Current);

public sealed record PlatformMfaSetup(string Secret, string OtpAuthUri);

/// <summary>
/// Platform administrators' sign-in: a revocable session per sign-in (<see cref="PlatformSession"/>), a 15-minute access
/// token carrying the session id and the user's security stamp, a refresh token that rotates on every use (kept only as a
/// hash; a reused one ends the session), two-step verification with TOTP and recovery codes (same primitives and replay
/// protection as the tenant users), and an audit record for every sign-in, failure, logout and change.
/// Platform challenges and refresh tokens start with <see cref="Prefix"/>, so the shared login form can route them here.
/// </summary>
public sealed class PlatformAuthService
{
    public const string Prefix = "p.";
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
    /// <summary>A second tab refreshing with the token another tab has just rotated is a race, not a theft.</summary>
    public static readonly TimeSpan RotationGrace = TimeSpan.FromSeconds(30);
    public const int RecoveryCodeCount = 8;

    private readonly NexoDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;
    private readonly IMfaSecretProtector _protector;
    private readonly LoginAttemptGuard _attempts;
    private readonly IAuditWriter _audit;
    private readonly TimeSpan _sessionLifetime;

    public PlatformAuthService(NexoDbContext db, IPasswordHasher hasher, IJwtTokenService jwt, IMfaSecretProtector protector,
        LoginAttemptGuard attempts, IAuditWriter audit, IConfiguration config)
    {
        _db = db;
        _hasher = hasher;
        _jwt = jwt;
        _protector = protector;
        _attempts = attempts;
        _audit = audit;
        _sessionLifetime = TimeSpan.FromHours(Math.Clamp(config.GetValue("Platform:SessionHours", 12), 1, 24));
    }

    public static bool IsPlatformToken(string? value) => value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    // ── Sign-in ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Password check (per-account lockout shared with the tenant guard). Wrong password → audited failure. With two-step
    /// verification on, answers a challenge; otherwise the session starts (and the admin-only actions stay closed until the
    /// administrator enrols, when the deployment requires it).
    /// </summary>
    public async Task<PlatformSignIn?> SignInAsync(string email, string password, string? ip, string? userAgent, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null) return null;

        var account = LoginAttemptGuard.ForPlatform(user.Id);
        if (await _attempts.IsLockedAsync(account, ct)) return new PlatformSignIn(null, null, "account_locked", user);
        if (!_hasher.Verify(password, user.PasswordHash))
        {
            await _attempts.RegisterFailureAsync(account, ct);
            Audit(AuditActions.PlatformLoginFailed, AuditSeverity.Warning, user, $"Senha errada no login da plataforma ({user.Email}).");
            await _db.SaveChangesAsync(ct);
            return new PlatformSignIn(null, null, "invalid_credentials", user);
        }
        await _attempts.ResetAsync(account, ct);

        if (user.MfaEnabled)
        {
            var challenge = NewToken();
            _db.PlatformSessions.Add(PlatformSession.Start(user.Id, Hash(challenge), user.SecurityStamp, mfaVerified: false,
                DateTime.UtcNow.Add(ChallengeLifetime), ip, userAgent, pendingMfa: true));
            await _db.SaveChangesAsync(ct);
            return new PlatformSignIn(null, challenge, null, user);
        }
        var tokens = await StartSessionAsync(user, mfaVerified: false, ip, userAgent, ct);
        return new PlatformSignIn(tokens, null, null, user);
    }

    /// <summary>The second step: the challenge plus a TOTP code (claimed atomically: never twice) or a recovery code.</summary>
    public async Task<PlatformSignIn> VerifyMfaAsync(string challenge, string? code, string? recoveryCode, CancellationToken ct)
    {
        var hash = Hash(challenge);
        var now = DateTime.UtcNow;
        var pending = await _db.PlatformSessions.FirstOrDefaultAsync(s => s.RefreshTokenHash == hash && s.PendingMfa && s.RevokedAt == null && s.ExpiresAt > now, ct);
        if (pending is null) return new PlatformSignIn(null, null, "invalid_challenge", null);
        var user = await _db.PlatformUsers.FirstAsync(u => u.Id == pending.PlatformUserId, ct);
        var account = LoginAttemptGuard.ForPlatform(user.Id);
        if (await _attempts.IsLockedAsync(account, ct)) return new PlatformSignIn(null, null, "account_locked", user);
        if (!user.MfaEnabled || !await CheckCodeAsync(user, code, recoveryCode, ct))
        {
            await _attempts.RegisterFailureAsync(account, ct);
            Audit(AuditActions.PlatformMfaFailed, AuditSeverity.Warning, user, $"Código de verificação errado no login da plataforma ({user.Email}).");
            await _db.SaveChangesAsync(ct);
            return new PlatformSignIn(null, null, "invalid_code", user);
        }
        await _attempts.ResetAsync(account, ct);

        var refresh = NewToken();
        pending.CompleteMfa(Hash(refresh), now.Add(_sessionLifetime));
        Audit(AuditActions.PlatformLoggedIn, AuditSeverity.Warning, user, $"Login na plataforma com verificação em duas etapas ({user.Email}).",
            new { sessionId = pending.Id, mfa = true });
        await _db.SaveChangesAsync(ct);
        var access = _jwt.GeneratePlatformToken(user, pending.Id, mfaVerified: true);
        return new PlatformSignIn(new PlatformTokens(access.AccessToken, access.ExpiresAt, refresh, pending.ExpiresAt), null, null, user);
    }

    private async Task<PlatformTokens> StartSessionAsync(PlatformUser user, bool mfaVerified, string? ip, string? userAgent, CancellationToken ct)
    {
        var refresh = NewToken();
        var session = PlatformSession.Start(user.Id, Hash(refresh), user.SecurityStamp, mfaVerified, DateTime.UtcNow.Add(_sessionLifetime), ip, userAgent);
        _db.PlatformSessions.Add(session);
        Audit(AuditActions.PlatformLoggedIn, AuditSeverity.Warning, user,
            $"Login na plataforma ({user.Email}){(mfaVerified ? " com verificação em duas etapas" : " sem verificação em duas etapas")}.",
            new { sessionId = session.Id, mfa = mfaVerified });
        await _db.SaveChangesAsync(ct);
        var access = _jwt.GeneratePlatformToken(user, session.Id, mfaVerified);
        return new PlatformTokens(access.AccessToken, access.ExpiresAt, refresh, session.ExpiresAt);
    }

    // ── Refresh, logout, sessions ───────────────────────────────────────────────

    /// <summary>
    /// Rotates the refresh token. The old one stops working at once (conditional update: two concurrent refreshes can't
    /// both win). Presented again within <see cref="RotationGrace"/> → "already rotated" (the other tab won; read the new
    /// token); later → treated as stolen: the session ends.
    /// </summary>
    public async Task<(PlatformTokens? Tokens, string? ErrorCode)> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var hash = Hash(refreshToken);
        var now = DateTime.UtcNow;
        var session = await _db.PlatformSessions.FirstOrDefaultAsync(s => !s.PendingMfa && (s.RefreshTokenHash == hash || s.PreviousRefreshTokenHash == hash), ct);
        if (session is null) return (null, "invalid_refresh");
        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == session.PlatformUserId, ct);
        if (user is null || !session.IsActiveAt(now, user.SecurityStamp)) return (null, "session_ended");

        if (session.PreviousRefreshTokenHash == hash)
        {
            if (session.RotatedAt is { } rotated && now - rotated < RotationGrace) return (null, "refresh_already_rotated");
            session.Revoke("refresh_reuse");
            Audit(AuditActions.PlatformSessionRevoked, AuditSeverity.Critical, user, "Sessão da plataforma encerrada: refresh token reutilizado (possível roubo).",
                new { sessionId = session.Id });
            await _db.SaveChangesAsync(ct);
            return (null, "session_ended");
        }

        var next = NewToken();
        var rotatedRows = await _db.PlatformSessions
            .Where(s => s.Id == session.Id && s.RefreshTokenHash == hash && s.RevokedAt == null)
            .ExecuteUpdateAsync(x => x
                .SetProperty(s => s.PreviousRefreshTokenHash, hash)
                .SetProperty(s => s.RefreshTokenHash, Hash(next))
                .SetProperty(s => s.RotatedAt, now)
                .SetProperty(s => s.LastUsedAt, now), ct);
        if (rotatedRows != 1) return (null, "refresh_already_rotated");

        var access = _jwt.GeneratePlatformToken(user, session.Id, session.MfaVerified);
        return (new PlatformTokens(access.AccessToken, access.ExpiresAt, next, session.ExpiresAt), null);
    }

    public async Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var session = await _db.PlatformSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.PlatformUserId == userId, ct);
        if (session is null || session.RevokedAt is not null) return;
        session.Revoke("logout");
        var user = await _db.PlatformUsers.FirstAsync(u => u.Id == userId, ct);
        Audit(AuditActions.PlatformLoggedOut, AuditSeverity.Info, user, $"Logout da plataforma ({user.Email}).", new { sessionId });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PlatformSessionView>> SessionsAsync(Guid userId, Guid? currentSessionId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var stamp = await _db.PlatformUsers.Where(u => u.Id == userId).Select(u => u.SecurityStamp).FirstAsync(ct);
        var rows = await _db.PlatformSessions.AsNoTracking()
            .Where(s => s.PlatformUserId == userId && !s.PendingMfa && s.RevokedAt == null && s.ExpiresAt > now && s.SecurityStamp == stamp)
            .OrderByDescending(s => s.LastUsedAt).Take(50).ToListAsync(ct);
        return rows.Select(s => new PlatformSessionView(s.Id, s.CreatedAt, s.LastUsedAt, s.ExpiresAt, s.MfaVerified, s.IpAddress, s.UserAgent,
            s.Id == currentSessionId)).ToList();
    }

    public async Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var session = await _db.PlatformSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.PlatformUserId == userId, ct)
            ?? throw new NotFoundException("Sessão", sessionId);
        if (session.RevokedAt is not null) return;
        session.Revoke("revoked_by_user");
        var user = await _db.PlatformUsers.FirstAsync(u => u.Id == userId, ct);
        Audit(AuditActions.PlatformSessionRevoked, AuditSeverity.Warning, user, "Sessão da plataforma encerrada pelo próprio administrador.", new { sessionId });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Ends every session of the user (security stamp rotated: every access token stops at its next request).</summary>
    public async Task RevokeAllAsync(Guid userId, Guid actorId, string reason, CancellationToken ct)
    {
        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("Usuário da plataforma", userId);
        user.BumpSecurityStamp();
        Audit(AuditActions.PlatformSessionRevoked, AuditSeverity.Critical, user, $"Todas as sessões de {user.Email} encerradas: {reason}.",
            new { reason, actorId });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Is the session of this access token still on? Read on every platform request — revocation, logout, a new password or
    /// two-step change apply at once, not when the token expires.
    /// </summary>
    public async Task<bool> IsSessionActiveAsync(Guid userId, Guid sessionId, string tokenStamp, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var row = await _db.PlatformSessions.AsNoTracking()
            .Where(s => s.Id == sessionId && s.PlatformUserId == userId)
            .Select(s => new { Session = s, Stamp = _db.PlatformUsers.Where(u => u.Id == userId).Select(u => u.SecurityStamp).FirstOrDefault() })
            .FirstOrDefaultAsync(ct);
        return row is not null && row.Stamp == tokenStamp && row.Session.IsActiveAt(now, row.Stamp);
    }

    // ── Two-step verification ───────────────────────────────────────────────────

    public async Task<(bool Enabled, bool SetupPending, int RecoveryCodesLeft)> MfaStatusAsync(Guid userId, CancellationToken ct)
    {
        var user = await LoadAsync(userId, ct);
        return (user.MfaEnabled, user.MfaPendingSecret is not null && !user.MfaEnabled, user.MfaRecoveryCodesLeft);
    }

    /// <summary>Starts the setup — the password again, so a session left open can't lock the owner out.</summary>
    public async Task<PlatformMfaSetup> StartMfaSetupAsync(Guid userId, string? password, CancellationToken ct)
    {
        var user = await LoadAsync(userId, ct);
        if (user.MfaEnabled) throw new DomainException("A verificação em duas etapas já está ativa.", "mfa_already_enabled");
        if (string.IsNullOrEmpty(password) || !_hasher.Verify(password, user.PasswordHash))
            throw new DomainException("Senha incorreta.", "invalid_password");
        var secret = Totp.NewSecret();
        user.StartMfaSetup(_protector.Protect(secret, user.Id));
        await _db.SaveChangesAsync(ct);
        return new PlatformMfaSetup(Totp.ToBase32(secret), Totp.OtpAuthUri("Orken Plataforma", user.Email, secret));
    }

    /// <summary>A code proves the authenticator works: two-step verification on, recovery codes shown once, every session ends.</summary>
    public async Task<IReadOnlyList<string>> EnableMfaAsync(Guid userId, string? code, CancellationToken ct)
    {
        var user = await LoadAsync(userId, ct);
        if (user.MfaEnabled) throw new DomainException("A verificação em duas etapas já está ativa.", "mfa_already_enabled");
        var secret = user.MfaPendingSecret is null ? null : _protector.Unprotect(user.MfaPendingSecret, user.Id);
        if (secret is null) throw new DomainException("Comece a configuração de novo.", "mfa_setup_missing");
        var step = Totp.Verify(secret, code, DateTime.UtcNow, null)
            ?? throw new DomainException("Código inválido. Confira a hora do celular e tente o código atual.", "invalid_code");
        var codes = Enumerable.Range(0, RecoveryCodeCount).Select(_ => NewRecoveryCode()).ToList();
        user.EnableMfa(user.MfaPendingSecret!, step, codes.Select(HashRecovery));
        Audit(AuditActions.PlatformMfaEnabled, AuditSeverity.Critical, user, $"Verificação em duas etapas ativada para {user.Email}; todas as sessões encerradas.");
        await _db.SaveChangesAsync(ct);
        return codes;
    }

    public async Task DisableMfaAsync(Guid userId, string? password, string? code, string? recoveryCode, CancellationToken ct)
    {
        var user = await LoadAsync(userId, ct);
        if (!user.MfaEnabled) return;
        if (string.IsNullOrEmpty(password) || !_hasher.Verify(password, user.PasswordHash))
            throw new DomainException("Senha incorreta.", "invalid_password");
        if (!await CheckCodeAsync(user, code, recoveryCode, ct)) throw new DomainException("Código inválido.", "invalid_code");
        user.DisableMfa();
        Audit(AuditActions.PlatformMfaDisabled, AuditSeverity.Critical, user, $"Verificação em duas etapas desativada por {user.Email}; todas as sessões encerradas.");
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Another super_admin turns it off for someone who lost the phone and the codes (never for oneself).</summary>
    public async Task AdminResetMfaAsync(Guid targetUserId, Guid actorId, string? actorEmail, CancellationToken ct)
    {
        if (targetUserId == actorId) throw new DomainException("Use os códigos de recuperação para a sua própria conta.", "mfa_self_reset");
        var user = await LoadAsync(targetUserId, ct);
        if (!user.MfaEnabled) return;
        user.DisableMfa();
        Audit(AuditActions.PlatformMfaDisabled, AuditSeverity.Critical, user, $"Verificação em duas etapas de {user.Email} desativada por {actorEmail}.",
            new { actorId }, actorId, actorEmail);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<bool> CheckCodeAsync(PlatformUser user, string? code, string? recoveryCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recoveryCode) && !string.IsNullOrWhiteSpace(code) && !Totp.LooksLikeCode(code))
            recoveryCode = code;
        if (!string.IsNullOrWhiteSpace(recoveryCode))
        {
            var before = user.MfaRecoveryCodes;
            if (!user.UseRecoveryCode(HashRecovery(recoveryCode))) return false;
            // claimed atomically: two concurrent uses of the same code can't both pass
            return await _db.PlatformUsers.Where(u => u.Id == user.Id && u.MfaRecoveryCodes == before)
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.MfaRecoveryCodes, user.MfaRecoveryCodes), ct) == 1;
        }
        var secret = user.MfaSecret is null ? null : _protector.Unprotect(user.MfaSecret, user.Id);
        if (secret is null) return false;
        var step = Totp.Verify(secret, code, DateTime.UtcNow, user.MfaLastUsedStep);
        if (step is null) return false;
        var claimed = await _db.PlatformUsers.Where(u => u.Id == user.Id && (u.MfaLastUsedStep == null || u.MfaLastUsedStep < step))
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.MfaLastUsedStep, step), ct) == 1;
        if (claimed) user.RecordMfaStep(step.Value);
        return claimed;
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private async Task<PlatformUser> LoadAsync(Guid userId, CancellationToken ct)
        => await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("Usuário da plataforma", userId);

    private static string NewToken()
        => Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string NewRecoveryCode()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
        var chars = Enumerable.Range(0, 10).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray();
        return $"{new string(chars, 0, 5)}-{new string(chars, 5, 5)}";
    }

    private string HashRecovery(string code)
        => _protector.HashRecoveryCode(new string(code.Trim().ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray()));

    private void Audit(string action, string severity, PlatformUser user, string description, object? metadata = null, Guid? actorId = null, string? actorEmail = null)
        => _audit.Stage(action, severity, "PlatformUser", user.Id.ToString(), description, tenantId: null,
            actorId: actorId ?? user.Id, actorName: actorEmail ?? user.Email, actorType: "platform", metadata: metadata);
}
