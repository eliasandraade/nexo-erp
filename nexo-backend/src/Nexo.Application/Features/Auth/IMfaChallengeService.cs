using Nexo.Domain.Entities;

namespace Nexo.Application.Features.Auth;

/// <summary>Sign-in second step: short-lived challenges and code checks.</summary>
public interface IMfaChallengeService
{
    Task<string> CreateChallengeAsync(Guid userId, CancellationToken ct = default);
    Task<Guid?> ResolveChallengeAsync(string? challenge, CancellationToken ct = default);
    Task DropChallengeAsync(string? challenge, CancellationToken ct = default);
    /// <summary>TOTP (once per step) or an unused recovery code (consumed) — claimed atomically.</summary>
    Task<bool> CheckCodeAsync(User user, string? code, string? recoveryCode, CancellationToken ct = default);
}
