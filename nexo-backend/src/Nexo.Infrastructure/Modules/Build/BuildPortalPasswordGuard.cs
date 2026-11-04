using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Process-wide guard of the client portal's password checks (singleton). BCrypt is deliberately slow, so an anonymous
/// caller could otherwise saturate the CPU with password attempts: at most <see cref="MaxConcurrent"/> verifications run at
/// once (<c>Build:Portal:MaxConcurrentPasswordChecks</c>, default 4); a caller waits up to <see cref="Wait"/>
/// (<c>Build:Portal:PasswordCheckWaitMs</c>, default 2000) for a slot, else gets <c>429 portal_busy</c>.
/// Also derives the cache keys of the per-client lockout (the client IP is only ever stored hashed).
/// </summary>
public sealed class BuildPortalPasswordGuard : IDisposable
{
    public const int DefaultMaxConcurrent = 4;
    public const int DefaultWaitMs = 2000;

    private readonly SemaphoreSlim _slots;

    public BuildPortalPasswordGuard(IConfiguration config)
    {
        MaxConcurrent = Math.Clamp(config.GetValue("Build:Portal:MaxConcurrentPasswordChecks", DefaultMaxConcurrent), 1, 64);
        Wait = TimeSpan.FromMilliseconds(Math.Clamp(config.GetValue("Build:Portal:PasswordCheckWaitMs", DefaultWaitMs), 0, 30_000));
        _slots = new SemaphoreSlim(MaxConcurrent, MaxConcurrent);
    }

    public int MaxConcurrent { get; }
    public TimeSpan Wait { get; }

    /// <summary>A verification slot (dispose to release), or null when every slot stayed busy for <see cref="Wait"/>.</summary>
    public async Task<IDisposable?> TryEnterAsync(CancellationToken ct)
        => await _slots.WaitAsync(Wait, ct) ? new Slot(_slots) : null;

    /// <summary>An opaque, stable key for a client IP (SHA-256, 32 hex chars) — the raw IP is never stored.</summary>
    public static string ClientKey(string? clientIp)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(clientIp) ? "unknown" : clientIp.Trim())))
            [..32].ToLowerInvariant();

    public static string FailuresKey(Guid portalId, string clientKey) => $"build-portal:pw-fail:{portalId:N}:{clientKey}";
    public static string LockKey(Guid portalId, string clientKey) => $"build-portal:pw-lock:{portalId:N}:{clientKey}";

    public void Dispose() => _slots.Dispose();

    private sealed class Slot : IDisposable
    {
        private SemaphoreSlim? _owner;
        public Slot(SemaphoreSlim owner) => _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}

/// <summary>A per-client lock of a portal's password (cache value).</summary>
public sealed record BuildPortalClientLock(DateTime Until);
