using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Application.Features.Auth;

/// <summary>
/// Per-account wrong-password counter, shared by every place a password is checked (login,
/// platform login, manager verification, change-password). The count is an atomic increment
/// (concurrent guesses can't share one slot); reaching the limit sets a lock flag for the window.
/// While locked, callers must not check the password at all — a guess can't be confirmed.
/// </summary>
public sealed class LoginAttemptGuard
{
    public const int MaxFailures = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ICacheService _cache;
    private readonly ILogger<LoginAttemptGuard> _logger;

    public LoginAttemptGuard(ICacheService cache, ILogger<LoginAttemptGuard> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    private static string FailKey(string account) => $"login:fail:{account}";
    private static string LockKey(string account) => $"login:locked:{account}";

    public Task<bool> IsLockedAsync(string account, CancellationToken ct = default)
        => _cache.ExistsAsync(LockKey(account), ct);

    /// <summary>Counts a wrong password; true when this one locked the account.</summary>
    public async Task<bool> RegisterFailureAsync(string account, CancellationToken ct = default)
    {
        var count = await _cache.IncrementAsync(FailKey(account), Window, ct);
        if (count is null)
        {
            _logger.LogWarning("Login attempt counter unavailable (cache down) — per-account lockout is off for now.");
            return false;
        }
        if (count < MaxFailures) return false;
        await _cache.SetFlagAsync(LockKey(account), Window, ct);
        return count == MaxFailures;
    }

    /// <summary>Right password, or the password was replaced: the count starts over and any lock ends.</summary>
    public async Task ResetAsync(string account, CancellationToken ct = default)
    {
        await _cache.RemoveAsync(FailKey(account), ct);
        await _cache.RemoveAsync(LockKey(account), ct);
    }

    public static string ForUser(Guid userId) => userId.ToString();
    public static string ForPlatform(Guid platformUserId) => $"platform:{platformUserId}";
}
