using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Auth;

public sealed record UserSessionDto(Guid Id, DateTime StartedAt, DateTime LastUsedAt, string? IpAddress, string? UserAgent);

/// <summary>
/// Account recovery and session control for tenant users: password reset by e-mail (one-time,
/// hashed, 1 h), "sair de todos os dispositivos", and the revocation every password change uses.
/// </summary>
public sealed class AccountSecurityService : IUserSessionRevoker, IUserInviter
{
    public const int MinPasswordLength = 6;
    private static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);
    /// <summary>No new link within this time of the last one: spamming the form can't flood the
    /// inbox nor keep replacing (and so invalidating) the link the person is about to use.</summary>
    private static readonly TimeSpan ResetCooldown = TimeSpan.FromMinutes(5);
    private const int MaxResetsPerHourPerAddress = 5;

    private sealed record Counter(int Count);

    private readonly NexoDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ICacheService _cache;
    private readonly IEmailService _email;
    private readonly IAuditWriter _audit;
    private readonly IConfiguration _config;
    private readonly ILogger<AccountSecurityService> _logger;

    public AccountSecurityService(
        NexoDbContext db, IPasswordHasher hasher, ICacheService cache, IEmailService email,
        IAuditWriter audit, IConfiguration config, ILogger<AccountSecurityService> logger)
    {
        _db = db; _hasher = hasher; _cache = cache; _email = email; _audit = audit; _config = config; _logger = logger;
    }

    // ── Password reset ────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a reset link to every active account with this e-mail. Says nothing about whether one
    /// exists (the endpoint always answers the same) — no account enumeration.
    /// </summary>
    public async Task RequestPasswordResetAsync(string? email, CancellationToken ct = default)
    {
        var address = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(address)) return;

        // per-address cap (whatever IP the requests come from)
        var counterKey = $"pwreset:count:{Hash(address)}";
        var counter = await _cache.GetAsync<Counter>(counterKey, ct);
        if (counter is { Count: >= MaxResetsPerHourPerAddress }) return;
        await _cache.SetAsync(counterKey, new Counter((counter?.Count ?? 0) + 1), TimeSpan.FromHours(1), ct);

        var now = DateTime.UtcNow;
        var accounts = await (
                from u in _db.Users.IgnoreQueryFilters()
                join t in _db.Tenants.IgnoreQueryFilters() on u.TenantId equals t.Id
                where u.Email == address
                   && (u.Status == UserStatus.Active || u.Status == UserStatus.PendingVerification)
                   && t.Status == TenantStatus.Active
                select new { User = u, Company = t.TradeName ?? t.CompanyName })
            .ToListAsync(ct);
        if (accounts.Count == 0) return;

        var frontendUrl = (_config["App:FrontendUrl"] ?? string.Empty).TrimEnd('/');
        foreach (var (user, company) in accounts.Select(a => (a.User, a.Company)))
        {
            // a reset link sent in the last minutes stays the valid one (an invite — longer-lived —
            // doesn't hold back a reset: asking to reset replaces it)
            if (user.PasswordResetExpiresAt is { } exp
                && exp > now.Add(ResetLifetime - ResetCooldown) && exp <= now.Add(ResetLifetime)) continue;

            var token = NewToken();
            user.SetPasswordResetToken(Hash(token), now.Add(ResetLifetime));
            await _db.SaveChangesAsync(ct);

            var link = $"{frontendUrl}/redefinir-senha?token={Uri.EscapeDataString(token)}";
            var result = await _email.SendAsync(user.Email, $"Redefinir sua senha — {company}",
                ResetHtml(user.FullName, user.Login, company, link), ct);
            if (result != EmailSendResult.Sent)
                _logger.LogWarning("Password reset e-mail for user {UserId} not delivered ({Result})", user.Id, result);
        }
    }

    /// <summary>Sets the new password from a valid link; ends every session. False when the link is invalid or expired.</summary>
    public async Task<bool> ResetPasswordAsync(string? token, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var hash = Hash(token.Trim());
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.PasswordResetTokenHash == hash, ct);
        if (user is null || user.PasswordResetExpiresAt is not { } exp || exp < DateTime.UtcNow) return false;
        if (user.Status is not (UserStatus.Active or UserStatus.PendingVerification)) return false;

        user.ChangePasswordHash(_hasher.Hash(newPassword), clearRequireChange: true);
        user.ClearPasswordResetToken();
        if (user.Status == UserStatus.PendingVerification)
        {
            // the link reached the inbox: the address is proven
            user.Activate();
            user.ClearVerificationToken();
        }
        user.BumpSecurityStamp();
        await RevokeAllAsync(user.Id, ct);

        _audit.Stage(AuditActions.UserPasswordChanged, AuditSeverity.Warning, "User", user.Id.ToString(),
            $"User '{user.Login}' reset the password through the e-mail link; all sessions ended.",
            tenantId: user.TenantId, actorId: user.Id, actorName: user.FullName);
        await _db.SaveChangesAsync(ct);
        await ForgetStampAsync(user.Id, ct);
        return true;
    }

    // ── Invite ────────────────────────────────────────────────────────────────

    private static readonly TimeSpan InviteLifetime = TimeSpan.FromHours(72);

    /// <summary>
    /// E-mails a just-created user a link (72 h, one use) to choose their own password — the
    /// manager never knows it. Reuses the reset link (same endpoint and page).
    /// </summary>
    public bool CanInvite => _email.CanDeliver;

    public async Task SendInviteAsync(Guid userId, CancellationToken ct = default)
    {
        var row = await (
                from u in _db.Users.IgnoreQueryFilters()
                join t in _db.Tenants.IgnoreQueryFilters() on u.TenantId equals t.Id
                where u.Id == userId
                select new { User = u, Company = t.TradeName ?? t.CompanyName })
            .FirstOrDefaultAsync(ct);
        if (row is null || string.IsNullOrWhiteSpace(row.User.Email)) return;

        var token = NewToken();
        row.User.SetPasswordResetToken(Hash(token), DateTime.UtcNow.Add(InviteLifetime));
        _audit.Stage(AuditActions.UserInvited, AuditSeverity.Info, "User", row.User.Id.ToString(),
            $"Invite e-mailed to '{row.User.Login}'.", tenantId: row.User.TenantId);
        await _db.SaveChangesAsync(ct);

        var frontendUrl = (_config["App:FrontendUrl"] ?? string.Empty).TrimEnd('/');
        var link = $"{frontendUrl}/redefinir-senha?token={Uri.EscapeDataString(token)}&convite=1";
        var result = await _email.SendAsync(row.User.Email, $"Seu acesso ao Orken — {row.Company}",
            InviteHtml(row.User.FullName, row.User.Login, row.Company, link), ct);
        if (result != EmailSendResult.Sent)
            _logger.LogWarning("Invite e-mail for user {UserId} not delivered ({Result})", row.User.Id, result);
    }

    private static string InviteHtml(string name, string login, string company, string link)
    {
        var e = (string v) => WebUtility.HtmlEncode(v);
        return $"""
            <!DOCTYPE html>
            <html lang="pt-BR"><body style="margin:0;padding:24px;background:#f6f7f9;font-family:Arial,Helvetica,sans-serif;color:#111827">
              <div style="max-width:480px;margin:0 auto;background:#ffffff;border-radius:12px;padding:28px">
                <p style="margin:0 0 12px;font-size:15px">Olá, {e(name)}.</p>
                <p style="margin:0 0 8px;font-size:15px"><strong>{e(company)}</strong> criou um acesso para você no Orken.</p>
                <p style="margin:0 0 20px;font-size:15px">Seu login: <strong>{e(login)}</strong></p>
                <p style="margin:0 0 20px"><a href="{e(link)}" style="display:inline-block;background:#4f46e5;color:#ffffff;padding:12px 20px;border-radius:8px;text-decoration:none;font-weight:600">Criar minha senha</a></p>
                <p style="margin:0;font-size:13px;color:#6b7280">O link vale por 72 horas e só pode ser usado uma vez. Se expirar, use "Esqueci a senha" na tela de login.</p>
              </div>
            </body></html>
            """;
    }

    // ── Sessions ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<UserSessionDto>> ListSessionsAsync(Guid userId, CancellationToken ct = default)
        => await _db.UserSessions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsRevoked && s.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(s => s.LastUsedAt)
            .Select(s => new UserSessionDto(s.Id, s.CreatedAt, s.LastUsedAt, s.IpAddress, s.UserAgent))
            .ToListAsync(ct);

    /// <summary>"Sair de todos os dispositivos": new security stamp + every session revoked.</summary>
    public async Task SignOutEverywhereAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return;
        user.BumpSecurityStamp();
        await RevokeAllAsync(user.Id, ct);
        _audit.Stage(AuditActions.UserSessionRevoked, AuditSeverity.Warning, "User", user.Id.ToString(),
            $"User '{user.Login}' signed out of every device.",
            tenantId: user.TenantId, actorId: user.Id, actorName: user.FullName);
        await _db.SaveChangesAsync(ct);
        await ForgetStampAsync(user.Id, ct);
    }

    /// <summary>
    /// Drops the cached stamp once the new one is committed — a request racing the change could
    /// have re-cached the old stamp in between (the cache is also cleared before, in RevokeAllAsync).
    /// </summary>
    public Task ForgetStampAsync(Guid userId, CancellationToken ct = default)
        => _cache.RemoveAsync($"user:stamp:{userId}", ct);

    public async Task RevokeAllAsync(Guid userId, CancellationToken ct = default)
    {
        var sessions = await _db.UserSessions.IgnoreQueryFilters()
            .Where(s => s.UserId == userId && !s.IsRevoked)
            .ToListAsync(ct);
        foreach (var s in sessions)
        {
            s.Revoke();
            await _cache.RemoveAsync($"refresh:valid:{s.RefreshJti}", ct);
        }
        await _cache.RemoveAsync($"user:stamp:{userId}", ct);
        // a new password ends any lockout (the 429 tells people to use "Esqueci a senha")
        await _cache.RemoveAsync($"login:fail:{userId}", ct);
        await _cache.RemoveAsync($"login:locked:{userId}", ct);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string NewToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string ResetHtml(string name, string login, string company, string link)
    {
        var e = (string v) => WebUtility.HtmlEncode(v);
        return $"""
            <!DOCTYPE html>
            <html lang="pt-BR"><body style="margin:0;padding:24px;background:#f6f7f9;font-family:Arial,Helvetica,sans-serif;color:#111827">
              <div style="max-width:480px;margin:0 auto;background:#ffffff;border-radius:12px;padding:28px">
                <p style="margin:0 0 12px;font-size:15px">Olá, {e(name)}.</p>
                <p style="margin:0 0 20px;font-size:15px">Recebemos um pedido para redefinir a senha da sua conta <strong>{e(login)}</strong> em <strong>{e(company)}</strong>, no Orken.</p>
                <p style="margin:0 0 20px"><a href="{e(link)}" style="display:inline-block;background:#4f46e5;color:#ffffff;padding:12px 20px;border-radius:8px;text-decoration:none;font-weight:600">Criar nova senha</a></p>
                <p style="margin:0;font-size:13px;color:#6b7280">O link vale por 1 hora e só pode ser usado uma vez. Se não foi você, ignore este e-mail — sua senha continua a mesma.</p>
              </div>
            </body></html>
            """;
    }
}
