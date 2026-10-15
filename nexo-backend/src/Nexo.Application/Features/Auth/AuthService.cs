using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Enums;

namespace Nexo.Application.Features.Auth;

public class AuthService
{
    private readonly IUserRepository _users;
    private readonly ITenantRepository _tenants;
    private readonly IStoreRepository _stores;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;
    private readonly ICacheService _cache;
    private readonly ISessionStore _sessions;
    private readonly ICurrentTenant _currentTenant;

    public AuthService(
        IUserRepository users,
        ITenantRepository tenants,
        IStoreRepository stores,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        ICacheService cache,
        ISessionStore sessions,
        ICurrentTenant currentTenant)
    {
        _users    = users;
        _tenants  = tenants;
        _stores   = stores;
        _hasher   = hasher;
        _jwt      = jwt;
        _cache    = cache;
        _sessions = sessions;
        _currentTenant = currentTenant;
    }

    /// <summary>
    /// Authenticates a user and returns access + refresh tokens wrapped in LoginOutcome.
    /// Returns LoginOutcome.Fail with an error code if credentials are invalid or account is blocked/pending.
    /// </summary>
    public async Task<LoginOutcome> LoginAsync(
        LoginRequest request,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken ct = default)
    {
        var user = await _users.GetByLoginAsync(request.Login.Trim().ToLowerInvariant(), ct);
        if (user is null) return LoginOutcome.Fail("invalid_credentials");

        if (user.Status == UserStatus.PendingVerification)
            return LoginOutcome.Fail("email_not_verified");

        if (user.Status == UserStatus.Inactive || user.Status == UserStatus.Blocked)
            return LoginOutcome.Fail("account_blocked");

        if (!_hasher.Verify(request.Password, user.PasswordHash))
            return LoginOutcome.Fail("invalid_credentials");

        user.RecordAccess();
        await _users.SaveChangesAsync(ct);

        // Load tenant slug + active modules for JWT claims
        var tenant = await _tenants.GetByIdAsync(user.TenantId, ct);
        if (tenant is null) return LoginOutcome.Fail("tenant_not_found");

        var activeModules = await _tenants.GetActiveModuleKeysAsync(user.TenantId, ct);
        var trialEndsAt   = await _tenants.GetTrialEndsAtAsync(user.TenantId, ct);

        // Load stores for this tenant — active store defaults to the first one
        var stores = await _stores.GetByTenantIdAsync(user.TenantId, ct);
        var defaultStore = stores.FirstOrDefault();
        var storeId = defaultStore?.Id ?? Guid.Empty;
        var storeIds = stores.Select(s => s.Id).ToList();

        var companyName = tenant.TradeName ?? tenant.CompanyName;
        var tokens = _jwt.GenerateTokenPair(user, tenant.Slug, companyName, activeModules, storeId, storeIds);

        // Store refresh token as valid in cache (TTL = refresh token expiry)
        var refreshClaims = _jwt.ValidateRefreshToken(tokens.RefreshToken);
        if (refreshClaims is not null)
        {
            var ttl = tokens.RefreshTokenExpiresAt - DateTime.UtcNow;
            await _cache.SetAsync(
                $"refresh:valid:{refreshClaims.Jti}",
                new RefreshTokenEntry(user.Id, user.TenantId, storeId, user.SecurityStamp),
                ttl, ct);

            // Track session for platform admin visibility
            await _sessions.CreateAsync(
                user.Id, user.TenantId,
                refreshClaims.Jti,
                ipAddress, userAgent,
                tokens.RefreshTokenExpiresAt, ct);
        }

        var session = new SessionDto(
            UserId:        user.Id.ToString(),
            TenantId:      user.TenantId.ToString(),
            Name:          user.FullName,
            Role:          user.Role.ToString().ToLowerInvariant(),
            Login:         user.Login,
            Email:         user.Email,
            ActiveModules: activeModules.ToList(),
            StoreId:       storeId == Guid.Empty ? null : storeId.ToString(),
            StoreIds:      storeIds.Select(id => id.ToString()).ToList(),
            CompanyName:   companyName,
            TrialEndsAt:   trialEndsAt);

        return LoginOutcome.Ok(new LoginResponse(
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.AccessTokenExpiresAt,
            session));
    }

    // Reuse grace for a rotated refresh token. Tabs share one stored refresh token, so two
    // tabs often refresh with the same token at nearly the same time; the one that lands
    // after the rotation must get the same successor, not a 401 (the SPA clears the shared
    // tokens on a failed refresh, logging every tab out). The grace never mints anything:
    // it only hands back the successor already issued, and it ends early as soon as that
    // successor is used or revoked.
    private static readonly TimeSpan RefreshReuseGrace = TimeSpan.FromSeconds(10);

    // How long a request waits for a concurrent rotation of the same token to finish.
    private static readonly TimeSpan RotationWaitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RotationPollInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>
    /// Rotates a refresh token: validates the old one, issues a new access token.
    /// Returns null if the refresh token is invalid, expired, or already revoked.
    ///
    /// Rotation is single-flight per token: one request claims the JTI and rotates it;
    /// concurrent (or slightly late) requests with the same token receive that same
    /// successor pair. Previously each overlapping request minted its own sibling token
    /// (only one tracked by the session store, so the rest survived a session revoke),
    /// and a request landing after the revoke got a spurious 401.
    /// </summary>
    public async Task<RefreshResponse?> RefreshAsync(
        RefreshTokenRequest request,
        CancellationToken ct = default)
    {
        var claims = _jwt.ValidateRefreshToken(request.RefreshToken);
        if (claims is null) return null;

        var rotatingKey = $"refresh:rotating:{claims.Jti}";
        var deadline = DateTime.UtcNow + RotationWaitTimeout;
        while (true)
        {
            var rotated = await _cache.GetAsync<RotatedRefreshEntry>($"refresh:rotated:{claims.Jti}", ct);
            if (rotated is not null)
            {
                // Grace ends once the successor is used (rotated away) or revoked (logout,
                // platform session revoke): from then on the old token is dead.
                return await _cache.ExistsAsync($"refresh:valid:{rotated.SuccessorJti}", ct)
                    ? rotated.Response
                    : null;
            }

            if (await _cache.TrySetFlagAsync(rotatingKey, RefreshReuseGrace, ct))
                break;

            // Another request is rotating this token right now — wait for its result. If it
            // fails it releases the claim and the next iteration claims it here instead.
            if (DateTime.UtcNow >= deadline) return null;
            await Task.Delay(RotationPollInterval, ct);
        }

        RefreshResponse? result = null;
        try
        {
            result = await RotateAsync(claims, ct);
            return result;
        }
        finally
        {
            if (result is null)
                await _cache.RemoveAsync(rotatingKey, CancellationToken.None);
        }
    }

    private async Task<RefreshResponse?> RotateAsync(RefreshTokenClaims claims, CancellationToken ct)
    {
        // Check token is still in Redis (not revoked)
        var cacheKey = $"refresh:valid:{claims.Jti}";
        var entry = await _cache.GetAsync<RefreshTokenEntry>(cacheKey, ct);
        if (entry is null) return null;

        // Load user to make sure account is still active. This endpoint is anonymous
        // (no tenant context resolved), so we must bypass the tenant query filter —
        // otherwise GetByIdAsync filters on Guid.Empty and returns null, yielding a
        // spurious 401 on every refresh once the cache actually persists the token.
        var user = await _users.GetByIdAcrossTenantsAsync(claims.UserId, ct);
        if (user is null || user.Status == Domain.Enums.UserStatus.Inactive || user.Status == Domain.Enums.UserStatus.Blocked)
            return null;

        // Defence in depth: the user loaded cross-tenant must match the token's tenant
        // claim and the stored entry. Guards against a token whose userId/tenantId were
        // ever decoupled (and against a future bug in token issuance).
        if (user.TenantId != claims.TenantId || entry.UserId != claims.UserId)
            return null;

        // Sessions ended since this token was issued (entries from before the stamp was recorded pass once)
        if (entry.SecurityStamp is not null && entry.SecurityStamp != user.SecurityStamp)
        {
            await _cache.RemoveAsync(cacheKey, ct);
            return null;
        }

        var tenant = await _tenants.GetByIdAsync(claims.TenantId, ct);
        if (tenant is null || tenant.Status != Domain.Enums.TenantStatus.Active)
            return null;

        var activeModules = await _tenants.GetActiveModuleKeysAsync(claims.TenantId, ct);

        // Re-load stores to refresh the list in the new token. Keep the store the session was
        // working in (recorded with the refresh token at login / switch-store): falling back to
        // the first store silently moved multi-store users — and everything they recorded — to
        // another store every time the access token was refreshed.
        var stores = await _stores.GetByTenantIdAsync(claims.TenantId, ct);
        var storeIds = stores.Select(s => s.Id).ToList();
        var storeId = entry.StoreId is { } activeStoreId && storeIds.Contains(activeStoreId)
            ? activeStoreId
            : stores.FirstOrDefault()?.Id ?? Guid.Empty;

        var tokens = _jwt.GenerateTokenPair(user, tenant.Slug, tenant.TradeName ?? tenant.CompanyName, activeModules, storeId, storeIds);

        var response = new RefreshResponse(
            tokens.AccessToken,
            tokens.AccessTokenExpiresAt,
            tokens.RefreshToken,
            tokens.RefreshTokenExpiresAt);

        // Store the new token and point the old one at it BEFORE revoking the old one, so a
        // concurrent caller always finds either the old token valid or its successor.
        var newClaims = _jwt.ValidateRefreshToken(tokens.RefreshToken);
        if (newClaims is not null)
        {
            var ttl = tokens.RefreshTokenExpiresAt - DateTime.UtcNow;
            await _cache.SetAsync(
                $"refresh:valid:{newClaims.Jti}",
                new RefreshTokenEntry(user.Id, user.TenantId, storeId, user.SecurityStamp),
                ttl, ct);
            await _cache.SetAsync(
                $"refresh:rotated:{claims.Jti}",
                new RotatedRefreshEntry(newClaims.Jti, response),
                RefreshReuseGrace, ct);

            // Update session JTI so platform can still revoke if needed
            await _sessions.TouchAsync(claims.Jti, newClaims.Jti, ct);
        }

        await _cache.RemoveAsync(cacheKey, ct);

        return response;
    }

    /// <summary>
    /// Issues a new access token scoped to a different store.
    /// Validates the requested store belongs to the current user's tenant
    /// and is in the user's accessible store list.
    /// Returns null if the store is invalid or inaccessible.
    /// </summary>
    public async Task<SwitchStoreResponse?> SwitchStoreAsync(
        Guid userId,
        Guid tenantId,
        Guid requestedStoreId,
        string refreshToken,
        CancellationToken ct = default)
    {
        // Validate the requested store exists and belongs to this tenant
        var store = await _stores.GetByIdAsync(requestedStoreId, ct);
        if (store is null || store.TenantId != tenantId)
            return null;

        if (store.Status != Domain.Enums.StoreStatus.Active)
            return null;

        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || user.Status != UserStatus.Active)
            return null;

        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null) return null;

        var activeModules = await _tenants.GetActiveModuleKeysAsync(tenantId, ct);
        var trialEndsAt   = await _tenants.GetTrialEndsAtAsync(tenantId, ct);
        var stores = await _stores.GetByTenantIdAsync(tenantId, ct);
        var storeIds = stores.Select(s => s.Id).ToList();

        // Revoke old refresh token and issue new token pair for the switched store
        var oldClaims = _jwt.ValidateRefreshToken(refreshToken);
        if (oldClaims is not null)
            await _cache.RemoveAsync($"refresh:valid:{oldClaims.Jti}", ct);

        var tokens = _jwt.GenerateTokenPair(user, tenant.Slug, tenant.TradeName ?? tenant.CompanyName, activeModules, requestedStoreId, storeIds);

        var newClaims = _jwt.ValidateRefreshToken(tokens.RefreshToken);
        if (newClaims is not null)
        {
            var ttl = tokens.RefreshTokenExpiresAt - DateTime.UtcNow;
            await _cache.SetAsync(
                $"refresh:valid:{newClaims.Jti}",
                new RefreshTokenEntry(user.Id, user.TenantId, requestedStoreId, user.SecurityStamp),
                ttl, ct);
        }

        var session = new SessionDto(
            UserId:        user.Id.ToString(),
            TenantId:      user.TenantId.ToString(),
            Name:          user.FullName,
            Role:          user.Role.ToString().ToLowerInvariant(),
            Login:         user.Login,
            Email:         user.Email,
            ActiveModules: activeModules.ToList(),
            StoreId:       requestedStoreId.ToString(),
            StoreIds:      storeIds.Select(id => id.ToString()).ToList(),
            CompanyName:   tenant.TradeName ?? tenant.CompanyName,
            TrialEndsAt:   trialEndsAt);

        return new SwitchStoreResponse(
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.AccessTokenExpiresAt,
            tokens.RefreshTokenExpiresAt,
            requestedStoreId.ToString(),
            session);
    }

    /// <summary>
    /// Revokes the given refresh token so it cannot be used again.
    /// Idempotent — safe to call multiple times.
    /// </summary>
    public async Task LogoutAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;

        var claims = _jwt.ValidateRefreshToken(refreshToken);
        if (claims is null) return;

        await _cache.RemoveAsync($"refresh:valid:{claims.Jti}", ct);
        await _sessions.RevokeAsync(claims.Jti, ct);
    }

    /// <summary>
    /// Validates manager credentials for the manager-challenge flow.
    /// Used before sensitive operations (sale cancellation, high discounts, etc).
    /// </summary>
    public async Task<VerifyManagerResponse> VerifyManagerAsync(
        VerifyManagerRequest request,
        CancellationToken ct = default)
    {
        // SECURITY (tenant isolation): scope the lookup to the CALLER'S tenant from
        // the start. Using the global GetByLoginAsync here would let a manager from
        // ANOTHER tenant authorize privileged actions (sale cancellation, high
        // discounts) in this tenant — a cross-tenant authorization bug.
        var user = await _users.GetByLoginInTenantAsync(
            request.Login.Trim().ToLowerInvariant(), _currentTenant.Id, ct);

        if (user is null || user.Status != UserStatus.Active)
            return new VerifyManagerResponse(false, null, null, null);

        // Defense-in-depth: the scoped query already guarantees this; assert the
        // invariant explicitly so a future refactor can't silently reintroduce the
        // cross-tenant leak.
        if (user.TenantId != _currentTenant.Id)
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
/// <summary>StoreId: the store the token was issued for (null on entries cached before this field existed).</summary>
/// <summary>
/// What Redis keeps per refresh token. <see cref="SecurityStamp"/>: the user's stamp when it was
/// issued — a refresh after the stamp changed (password change, "sair de todos") is refused even
/// if the key itself was never tracked as a session.
/// </summary>
public record RefreshTokenEntry(Guid UserId, Guid TenantId, Guid? StoreId = null, string? SecurityStamp = null);

/// <summary>Successor issued for a rotated refresh token, served during the reuse grace.</summary>
public record RotatedRefreshEntry(string SuccessorJti, RefreshResponse Response);
