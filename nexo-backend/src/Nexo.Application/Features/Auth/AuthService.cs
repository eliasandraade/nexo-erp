using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Enums;

namespace Nexo.Application.Features.Auth;

public class AuthService
{
    private readonly IUserRepository _users;
    private readonly ITenantRepository _tenants;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;
    private readonly ICacheService _cache;

    public AuthService(
        IUserRepository users,
        ITenantRepository tenants,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        ICacheService cache)
    {
        _users = users;
        _tenants = tenants;
        _hasher = hasher;
        _jwt = jwt;
        _cache = cache;
    }

    /// <summary>
    /// Authenticates a user and returns access + refresh tokens.
    /// Returns null if credentials are invalid or the account is inactive/blocked.
    /// </summary>
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await _users.GetByLoginAsync(request.Login.Trim().ToLowerInvariant(), ct);
        if (user is null) return null;

        if (user.Status == UserStatus.Inactive || user.Status == UserStatus.Blocked)
            return null;

        if (!_hasher.Verify(request.Password, user.PasswordHash))
            return null;

        user.RecordAccess();
        await _users.SaveChangesAsync(ct);

        // Load tenant slug + active modules for JWT claims
        var tenant = await _tenants.GetByIdAsync(user.TenantId, ct);
        if (tenant is null) return null;

        var activeModules = await _tenants.GetActiveModuleKeysAsync(user.TenantId, ct);

        var tokens = _jwt.GenerateTokenPair(user, tenant.Slug, activeModules);

        // Store refresh token as valid in cache (TTL = refresh token expiry)
        // Key structure: refresh:valid:{jti} — validated on refresh endpoint
        var refreshClaims = _jwt.ValidateRefreshToken(tokens.RefreshToken);
        if (refreshClaims is not null)
        {
            var ttl = tokens.RefreshTokenExpiresAt - DateTime.UtcNow;
            await _cache.SetAsync(
                $"refresh:valid:{refreshClaims.Jti}",
                new RefreshTokenEntry(user.Id, user.TenantId),
                ttl, ct);
        }

        var session = new SessionDto(
            UserId:    user.Id.ToString(),
            TenantId:  user.TenantId.ToString(),
            Name:      user.FullName,
            Role:      user.Role.ToString().ToLowerInvariant(),
            Login:     user.Login,
            Email:     user.Email,
            ActiveModules: activeModules.ToList());

        return new LoginResponse(
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.AccessTokenExpiresAt,
            session);
    }

    /// <summary>
    /// Rotates a refresh token: validates the old one, issues a new access token.
    /// Returns null if the refresh token is invalid, expired, or already revoked.
    /// </summary>
    public async Task<RefreshResponse?> RefreshAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        var claims = _jwt.ValidateRefreshToken(request.RefreshToken);
        if (claims is null) return null;

        // Check token is still in Redis (not revoked)
        var cacheKey = $"refresh:valid:{claims.Jti}";
        var entry = await _cache.GetAsync<RefreshTokenEntry>(cacheKey, ct);
        if (entry is null) return null;

        // Load user to make sure account is still active
        var user = await _users.GetByIdAsync(claims.UserId, ct);
        if (user is null || user.Status == Domain.Enums.UserStatus.Inactive || user.Status == Domain.Enums.UserStatus.Blocked)
            return null;

        var tenant = await _tenants.GetByIdAsync(claims.TenantId, ct);
        if (tenant is null || tenant.Status != Domain.Enums.TenantStatus.Active)
            return null;

        var activeModules = await _tenants.GetActiveModuleKeysAsync(claims.TenantId, ct);
        var tokens = _jwt.GenerateTokenPair(user, tenant.Slug, activeModules);

        // Revoke old refresh token, store new one
        await _cache.RemoveAsync(cacheKey, ct);
        var newClaims = _jwt.ValidateRefreshToken(tokens.RefreshToken);
        if (newClaims is not null)
        {
            var ttl = tokens.RefreshTokenExpiresAt - DateTime.UtcNow;
            await _cache.SetAsync(
                $"refresh:valid:{newClaims.Jti}",
                new RefreshTokenEntry(user.Id, user.TenantId),
                ttl, ct);
        }

        return new RefreshResponse(
            tokens.AccessToken,
            tokens.AccessTokenExpiresAt,
            tokens.RefreshToken,
            tokens.RefreshTokenExpiresAt);
    }

    /// <summary>
    /// Revokes the given refresh token so it cannot be used again.
    /// If the token is already expired or not in Redis, the call is a no-op
    /// (idempotent — safe to call multiple times).
    /// </summary>
    public async Task LogoutAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;

        var claims = _jwt.ValidateRefreshToken(refreshToken);
        if (claims is null) return; // already expired or invalid — nothing to revoke

        await _cache.RemoveAsync($"refresh:valid:{claims.Jti}", ct);
    }

    /// <summary>
    /// Validates manager credentials for the manager-challenge flow.
    /// Used before sensitive operations (sale cancellation, high discounts, etc).
    /// </summary>
    public async Task<VerifyManagerResponse> VerifyManagerAsync(
        VerifyManagerRequest request,
        CancellationToken ct = default)
    {
        var user = await _users.GetByLoginAsync(request.Login.Trim().ToLowerInvariant(), ct);

        if (user is null || user.Status != UserStatus.Active)
            return new VerifyManagerResponse(false, null, null, null);

        if (!_hasher.Verify(request.Password, user.PasswordHash))
            return new VerifyManagerResponse(false, null, null, null);

        if (!user.IsManager())
            return new VerifyManagerResponse(false, null, null, null);

        return new VerifyManagerResponse(
            Authorized:    true,
            ManagerUserId: user.Id.ToString(),
            ManagerName:   user.FullName,
            Role:          user.Role.ToString().ToLowerInvariant());
    }
}

/// <summary>Serializable refresh token cache entry.</summary>
public record RefreshTokenEntry(Guid UserId, Guid TenantId);
