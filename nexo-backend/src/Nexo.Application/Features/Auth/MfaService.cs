using System.Security.Cryptography;
using System.Text;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Auth;

/// <summary>
/// Two-step verification with an authenticator app (TOTP): set up (secret + QR link), enable
/// with a first code (returns 8 one-use recovery codes, stored hashed), disable (password + code),
/// and the sign-in challenge. Secrets are encrypted at rest.
/// </summary>
public sealed class MfaService : IMfaChallengeService
{
    public const string Issuer = "Orken";
    public const int RecoveryCodeCount = 8;
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IMfaSecretProtector _protector;
    private readonly ICacheService _cache;
    private readonly IAuditWriter _audit;
    private readonly LoginAttemptGuard? _attempts;
    private readonly IUserSessionRevoker? _sessions;

    public MfaService(IUserRepository users, IPasswordHasher hasher, IMfaSecretProtector protector, ICacheService cache, IAuditWriter audit,
        LoginAttemptGuard? attempts = null, IUserSessionRevoker? sessions = null)
    {
        _users = users; _hasher = hasher; _protector = protector; _cache = cache; _audit = audit;
        _attempts = attempts; _sessions = sessions;
    }

    /// <summary>
    /// The password (and a code, when given) of the signed-in person, under the same lockout as sign-in:
    /// a stolen session can't brute-force it here. One error message for either wrong part.
    /// </summary>
    private async Task ProveItIsYouAsync(User user, string? password, Func<bool>? codeCheck, CancellationToken ct)
    {
        var account = LoginAttemptGuard.ForUser(user.Id);
        if (_attempts is not null && await _attempts.IsLockedAsync(account, ct))
            throw new ForbiddenException("Muitas tentativas. Tente de novo em 15 minutos.");
        var ok = !string.IsNullOrEmpty(password) && _hasher.Verify(password, user.PasswordHash) && (codeCheck?.Invoke() ?? true);
        if (ok) return;
        if (_attempts is not null) await _attempts.RegisterFailureAsync(account, ct);
        throw new ForbiddenException("Senha ou código incorretos.");
    }

    private sealed record Challenge(Guid UserId);

    // ── Status / setup / enable / disable (signed-in user) ─────────────────────

    public async Task<MfaStatusDto> GetStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await Load(userId, ct);
        return new MfaStatusDto(user.MfaEnabled, user.MfaPendingSecret is not null && !user.MfaEnabled, user.MfaRecoveryCodesLeft);
    }

    public async Task<MfaSetupDto> StartSetupAsync(Guid userId, string? password, CancellationToken ct = default)
    {
        var user = await Load(userId, ct);
        if (user.MfaEnabled) throw new DomainException("A verificação em duas etapas já está ativa.");
        // the password, not just a session: someone using a session left open can't lock the owner out
        await ProveItIsYouAsync(user, password, null, ct);
        var secret = Totp.NewSecret();
        user.StartMfaSetup(_protector.Protect(secret, user.Id));
        await _users.SaveChangesAsync(ct);
        return new MfaSetupDto(Totp.ToBase32(secret), Totp.OtpAuthUri(Issuer, user.Login, secret));
    }

    public async Task<MfaEnabledDto> EnableAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var user = await Load(userId, ct);
        if (user.MfaEnabled) throw new DomainException("A verificação em duas etapas já está ativa.");
        if (user.MfaPendingSecret is null) throw new DomainException("Comece a configuração de novo.");
        var secret = _protector.Unprotect(user.MfaPendingSecret, user.Id)
            ?? throw new DomainException("Comece a configuração de novo.");
        var step = Totp.Verify(secret, code, DateTime.UtcNow, null)
            ?? throw new DomainException("Código inválido. Confira a hora do celular e tente o código atual.");

        var codes = Enumerable.Range(0, RecoveryCodeCount).Select(_ => NewRecoveryCode()).ToList();
        user.EnableMfa(user.MfaPendingSecret, step, codes.Select(HashRecovery));
        // every other session (maybe someone else's) ends: from now on, entering takes the code
        user.BumpSecurityStamp();
        if (_sessions is not null) await _sessions.RevokeAllAsync(user.Id, ct);
        _audit.Stage(AuditActions.UserMfaEnabled, AuditSeverity.Warning, "User", user.Id.ToString(),
            $"User '{user.Login}' turned on two-step verification; all sessions ended.", tenantId: user.TenantId, actorId: user.Id, actorName: user.FullName);
        await _users.SaveChangesAsync(ct);
        if (_sessions is not null) await _sessions.ForgetStampAsync(user.Id, ct);
        return new MfaEnabledDto(codes);
    }

    public async Task DisableAsync(Guid userId, MfaDisableRequest request, CancellationToken ct = default)
    {
        var user = await Load(userId, ct);
        if (!user.MfaEnabled) return;
        var codeOk = await CheckCodeAsync(user, request.Code, request.RecoveryCode, ct);
        await ProveItIsYouAsync(user, request.Password, () => codeOk, ct);
        user.DisableMfa();
        _audit.Stage(AuditActions.UserMfaDisabled, AuditSeverity.Warning, "User", user.Id.ToString(),
            $"User '{user.Login}' turned off two-step verification.", tenantId: user.TenantId, actorId: user.Id, actorName: user.FullName);
        await _users.SaveChangesAsync(ct);
    }

    /// <summary>A director turns it off for someone who lost the phone and the codes (same tenant only).</summary>
    public async Task AdminDisableAsync(Guid userId, Guid actorId, string actorName, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);
        if (!user.MfaEnabled) return;
        user.DisableMfa();
        _audit.Stage(AuditActions.UserMfaDisabled, AuditSeverity.Critical, "User", user.Id.ToString(),
            $"Two-step verification of '{user.Login}' turned off by a director.", tenantId: user.TenantId, actorId: actorId, actorName: actorName);
        await _users.SaveChangesAsync(ct);
    }

    // ── Sign-in challenge ─────────────────────────────────────────────────────

    public async Task<string> CreateChallengeAsync(Guid userId, CancellationToken ct = default)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await _cache.SetAsync(ChallengeKey(token), new Challenge(userId), ChallengeLifetime, ct);
        return token;
    }

    public async Task<Guid?> ResolveChallengeAsync(string? challenge, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(challenge) ? null : (await _cache.GetAsync<Challenge>(ChallengeKey(challenge), ct))?.UserId;

    public Task DropChallengeAsync(string? challenge, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(challenge) ? Task.CompletedTask : _cache.RemoveAsync(ChallengeKey(challenge), ct);

    /// <summary>
    /// TOTP or recovery code (a "code" that isn't 6 digits is taken as a recovery code). The use is
    /// claimed atomically in the database, so two concurrent requests with the same code can't both pass.
    /// </summary>
    public async Task<bool> CheckCodeAsync(User user, string? code, string? recoveryCode, CancellationToken ct = default)
    {
        var digits = new string((code ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (string.IsNullOrWhiteSpace(recoveryCode) && !string.IsNullOrWhiteSpace(code) && digits.Length != Totp.Digits)
            recoveryCode = code;

        if (!string.IsNullOrWhiteSpace(recoveryCode))
        {
            var before = user.MfaRecoveryCodes;
            if (!user.UseRecoveryCode(HashRecovery(recoveryCode))) return false;
            return await _users.TryReplaceRecoveryCodesAsync(user.Id, before, user.MfaRecoveryCodes ?? string.Empty, ct);
        }
        if (user.MfaSecret is null) return false;
        var secret = _protector.Unprotect(user.MfaSecret, user.Id);
        if (secret is null) return false;
        var step = Totp.Verify(secret, code, DateTime.UtcNow, user.MfaLastUsedStep);
        if (step is null || !await _users.TryClaimMfaStepAsync(user.Id, step.Value, ct)) return false;
        user.RecordMfaStep(step.Value);
        return true;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<User> Load(Guid userId, CancellationToken ct)
        => await _users.GetByIdAcrossTenantsAsync(userId, ct) ?? throw new NotFoundException("User", userId);

    private static string ChallengeKey(string token)
        => "mfa:challenge:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>10 characters from an unambiguous alphabet, shown as xxxxx-xxxxx.</summary>
    private static string NewRecoveryCode()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
        var chars = Enumerable.Range(0, 10).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray();
        return $"{new string(chars, 0, 5)}-{new string(chars, 5, 5)}";
    }

    private string HashRecovery(string code)
        => _protector.HashRecoveryCode(new string(code.Trim().ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray()));
}
