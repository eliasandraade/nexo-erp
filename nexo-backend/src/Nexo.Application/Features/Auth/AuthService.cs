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

    private readonly IAuditWriter? _audit;
    private readonly LoginAttemptGuard? _attempts;
    private readonly IMfaChallengeService? _mfa;
    private readonly RefreshBudget? _budget;

    public AuthService(
        IUserRepository users,
        ITenantRepository tenants,
        IStoreRepository stores,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        ICacheService cache,
        ISessionStore sessions,
        ICurrentTenant currentTenant,
        IAuditWriter? audit = null,
        LoginAttemptGuard? attempts = null,
        IMfaChallengeService? mfa = null,
        RefreshBudget? budget = null)
    {
        _users    = users;
        _tenants  = tenants;
        _stores   = stores;
        _hasher   = hasher;
        _jwt      = jwt;
        _cache    = cache;
        _sessions = sessions;
        _audit = audit;
        _attempts = attempts;
        _mfa = mfa;
        _budget = budget;
        _currentTenant = currentTenant;
    }

    public static readonly TimeSpan LockoutWindow = LoginAttemptGuard.Window;

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

        // Per-account lockout: the IP limiter alone is easy to dodge (rotating addresses). While
        // locked the password is not even checked — a guess can't be confirmed meanwhile.
        var account = LoginAttemptGuard.ForUser(user.Id);
        if (_attempts is not null && await _attempts.IsLockedAsync(account, ct))
            return LoginOutcome.Fail("account_locked");

        if (!_hasher.Verify(request.Password, user.PasswordHash))
            return await WrongPasswordAsync(user, account, ct);

        // Two-step verification: the password alone opens nothing — a short-lived challenge waits for the
        // code. The wrong-attempt count is NOT reset here (only after the code): otherwise re-entering
        // the known password between guesses would allow endless code guessing.
        if (user.MfaEnabled && _mfa is not null)
            return LoginOutcome.NeedsMfa(await _mfa.CreateChallengeAsync(user.Id, ct));

        if (_attempts is not null) await _attempts.ResetAsync(account, ct);

        return await CompleteLoginAsync(user, ipAddress, userAgent, ct);
    }

    /// <summary>
    /// Second step: a TOTP (or a recovery code) for the challenge issued by <see cref="LoginAsync"/>.
    /// Wrong codes count toward the same per-account lockout as wrong passwords.
    /// </summary>
    public async Task<LoginOutcome> VerifyMfaAsync(
        MfaVerifyRequest request, string? ipAddress = null, string? userAgent = null, CancellationToken ct = default)
    {
        if (_mfa is null) return LoginOutcome.Fail("invalid_challenge");
        var userId = await _mfa.ResolveChallengeAsync(request.Challenge, ct);
        if (userId is null) return LoginOutcome.Fail("invalid_challenge");

        var user = await _users.GetByIdAcrossTenantsAsync(userId.Value, ct);
        if (user is null || user.Status != UserStatus.Active || !user.MfaEnabled) return LoginOutcome.Fail("invalid_challenge");

        var account = LoginAttemptGuard.ForUser(user.Id);
        if (_attempts is not null && await _attempts.IsLockedAsync(account, ct))
            return LoginOutcome.Fail("account_locked");

        if (!await _mfa.CheckCodeAsync(user, request.Code, request.RecoveryCode, ct))
        {
            var failed = await WrongPasswordAsync(user, account, ct, AuditActions.UserMfaFailed);
            if (failed.ErrorCode == "account_locked") await _mfa.DropChallengeAsync(request.Challenge, ct);
            return failed.ErrorCode == "account_locked" ? failed : LoginOutcome.Fail("invalid_code");
        }

        await _mfa.DropChallengeAsync(request.Challenge, ct);
        if (_attempts is not null) await _attempts.ResetAsync(account, ct);
        return await CompleteLoginAsync(user, ipAddress, userAgent, ct);
    }

    /// <summary>Credentials (and the second step, when on) are proven: record the access and issue the session.</summary>
    private async Task<LoginOutcome> CompleteLoginAsync(Domain.Entities.User user, string? ipAddress, string? userAgent, CancellationToken ct)
    {
        user.RecordAccess();
        _audit?.Stage(AuditActions.UserLoggedIn, AuditSeverity.Info, "User", user.Id.ToString(),
            $"User '{user.Login}' signed in.", tenantId: user.TenantId, actorId: user.Id, actorName: user.FullName);
        await _users.SaveChangesAsync(ct);

        // Load tenant slug + active modules for JWT claims
        var tenant = await _tenants.GetByIdAsync(user.TenantId, ct);
        if (tenant is null) return LoginOutcome.Fail("tenant_not_found");

        var activeModules = await _tenants.GetActiveModuleKeysAsync(user.TenantId, ct);
        var expiredModules = await _tenants.GetExpiredModuleKeysAsync(user.TenantId, ct);
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
            TrialEndsAt:   trialEndsAt,
            RequirePasswordChange: user.RequirePasswordChange,
            ReadOnlyModules: expiredModules.ToList());

        return LoginOutcome.Ok(new LoginResponse(
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.AccessTokenExpiresAt,
            session));
    }

    /// <summary>Counts the wrong password (maybe locking the account) and audits it.</summary>
    private async Task<LoginOutcome> WrongPasswordAsync(Domain.Entities.User user, string account, CancellationToken ct,
        string failedAction = AuditActions.UserLoginFailed)
    {
        var lockedNow = _attempts is not null && await _attempts.RegisterFailureAsync(account, ct);
        _audit?.Stage(lockedNow ? AuditActions.UserLoginLocked : failedAction,
            lockedNow ? AuditSeverity.Critical : AuditSeverity.Warning, "User", user.Id.ToString(),
            lockedNow ? $"Login of '{user.Login}' paused for {LockoutWindow.TotalMinutes:0} min after {LoginAttemptGuard.MaxFailures} wrong passwords."
                      : failedAction == AuditActions.UserMfaFailed ? $"Wrong two-step code for '{user.Login}'." : $"Wrong password for '{user.Login}'.",
            tenantId: user.TenantId, actorName: "anonymous", actorType: "system");
        if (_audit is not null) await _users.SaveChangesAsync(ct);
        return LoginOutcome.Fail(lockedNow ? "account_locked" : "invalid_credentials");
    }

    /// <summary>
    /// Uma segunda aba que renova com o token que outra aba acabou de trocar é uma corrida, não um roubo: dentro desta
    /// janela ela recebe "já renovado" (409, nada é emitido) e lê o par novo que a outra aba gravou. Depois dela, o token
    /// já usado voltando indica uma cópia em outras mãos e encerra a família inteira. Mesma regra das sessões da plataforma.
    /// </summary>
    public static readonly TimeSpan RotationGrace = TimeSpan.FromSeconds(30);

    // Vida da reserva de uso único de um token: só a troca em si precisa caber nela. Depois, a entrada do token já
    // saiu do cache e quem responde é o registro da família.
    private static readonly TimeSpan RotationClaimTtl = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Troca um refresh token por um par novo da mesma família (sessão). Uso único: só a primeira requisição que
    /// reserva o token (INCR atômico) emite o par; as concorrentes recebem <see cref="RefreshErrors.AlreadyRotated"/>
    /// sem nenhum token irmão. Um token já trocado apresentado depois de <see cref="RotationGrace"/> encerra a família.
    /// </summary>
    public Task<RefreshOutcome> RefreshAsync(RefreshTokenRequest request, CancellationToken ct = default)
        => RefreshAsync(request, clientIp: null, ct);

    /// <param name="request">The token presented.</param>
    /// <param name="clientIp">Who presented it, for the failure budget. Null counts nothing.</param>
    /// <param name="ct">The request's cancellation.</param>
    public async Task<RefreshOutcome> RefreshAsync(RefreshTokenRequest request, string? clientIp, CancellationToken ct = default)
    {
        var claims = _jwt.ValidateRefreshToken(request.RefreshToken);
        if (claims is null) return await RefuseUnreadableTokenAsync(request.RefreshToken, clientIp, ct);

        // Check token is still in Redis (not revoked)
        var cacheKey = $"refresh:valid:{claims.Jti}";
        var entry = await _cache.GetAsync<RefreshTokenEntry>(cacheKey, ct);
        if (entry is null)
        {
            // Without the cache, "no entry" is no answer: it cannot be taken for a spent token nor for an ended session.
            if (_cache.IsUnavailable) return await RefuseWithoutCacheAsync(claims, ct);
            var refused = await RefuseSpentTokenAsync(claims, ct);
            return refused.ErrorCode == RefreshErrors.Invalid && _cache.IsUnavailable
                ? await RefuseWithoutCacheAsync(claims, ct)
                : refused;
        }

        if (await IsFamilyRevokedAsync(claims.Family, ct))
        {
            await _cache.RemoveAsync(cacheKey, ct);
            return RefreshOutcome.Fail(RefreshErrors.SessionEnded);
        }

        // Only a token that can still rotate gets here: a spent one was already judged above, so holding a family over budget
        // can never hide a reused token from the reuse detection. Before the database, so a loop costs little.
        if (_budget is not null && await _budget.SpendFamilyAsync(claims.Family, ct) is { } wait)
            return RefreshOutcome.OverBudget(wait);

        // Load user to make sure account is still active. This endpoint is anonymous
        // (no tenant context resolved), so we must bypass the tenant query filter —
        // otherwise GetByIdAsync filters on Guid.Empty and returns null, yielding a
        // spurious 401 on every refresh once the cache actually persists the token.
        var user = await _users.GetByIdAcrossTenantsAsync(claims.UserId, ct);
        if (user is null || user.Status == Domain.Enums.UserStatus.Inactive || user.Status == Domain.Enums.UserStatus.Blocked)
            return RefreshOutcome.Fail(RefreshErrors.SessionEnded);

        // Defence in depth: the user loaded cross-tenant must match the token's tenant
        // claim and the stored entry. Guards against a token whose userId/tenantId were
        // ever decoupled (and against a future bug in token issuance).
        if (user.TenantId != claims.TenantId || entry.UserId != claims.UserId)
            return RefreshOutcome.Fail(RefreshErrors.Invalid);

        // Sessions ended since this token was issued (entries from before the stamp was recorded pass once)
        if (entry.SecurityStamp is not null && entry.SecurityStamp != user.SecurityStamp)
        {
            await _cache.RemoveAsync(cacheKey, ct);
            return RefreshOutcome.Fail(RefreshErrors.SessionEnded);
        }

        var tenant = await _tenants.GetByIdAsync(claims.TenantId, ct);
        if (tenant is null || tenant.Status != Domain.Enums.TenantStatus.Active)
            return RefreshOutcome.Fail(RefreshErrors.SessionEnded);

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

        // Reserva logo antes de emitir: perder aqui é a corrida com outra aba, e nada é emitido duas vezes.
        // Without an answer from the cache nobody rotates, and that is no race: a 409 would lead the client to end the session.
        var claimed = await ClaimRotationAsync(claims.Jti, ct);
        if (claimed is null) return RefreshOutcome.Unavailable();
        if (claimed == false) return RefreshOutcome.Fail(RefreshErrors.AlreadyRotated);

        var tokens = _jwt.GenerateTokenPair(user, tenant.Slug, tenant.TradeName ?? tenant.CompanyName, activeModules, storeId, storeIds,
            claims.Family);
        await RotateIntoAsync(claims, tokens, new RefreshTokenEntry(user.Id, user.TenantId, storeId, user.SecurityStamp), ct);

        return RefreshOutcome.Ok(new RefreshResponse(
            tokens.AccessToken,
            tokens.AccessTokenExpiresAt,
            tokens.RefreshToken,
            tokens.RefreshTokenExpiresAt));
    }

    /// <summary>
    /// A token that does not validate. Only one we did not issue spends the failure budget: an expired token of ours is a
    /// terminal coming back after the weekend, and it needs its 401 to go to the login. A 429 would leave it stuck, because a
    /// 429 does not end a session.
    /// </summary>
    private async Task<RefreshOutcome> RefuseUnreadableTokenAsync(string token, string? clientIp, CancellationToken ct)
    {
        if (_budget is not null && clientIp is not null && !_jwt.IsRefreshTokenOurs(token)
            && await _budget.SpendFailureAsync(clientIp, ct) is { } wait)
            return RefreshOutcome.OverBudget(wait);
        return RefreshOutcome.Fail(RefreshErrors.Invalid);
    }

    /// <summary>
    /// The cache is not answering, so the entry of this token cannot be read. The session table still knows the ones that ended
    /// (role, status or password changed, signed out) and the token a session is on. Any other answer would be false: a 401 for
    /// a live session signs its terminal out because the cache had a bad moment, and the circuit keeps it open for 10 s.
    /// </summary>
    private async Task<RefreshOutcome> RefuseWithoutCacheAsync(RefreshTokenClaims claims, CancellationToken ct)
        => await _sessions.IsActiveAsync(claims.Jti, ct)
            ? RefreshOutcome.Unavailable()
            : RefreshOutcome.Fail(RefreshErrors.SessionEnded);

    /// <summary>
    /// Token assinado e no prazo, mas sem entrada válida. Se a família o registra como o token que acabou de ser
    /// trocado, dentro da carência é a corrida de outra aba; um token já trocado fora dela (ou mais antigo) significa
    /// que existe outra cópia: a família inteira é encerrada e o caso fica na auditoria.
    /// </summary>
    private async Task<RefreshOutcome> RefuseSpentTokenAsync(RefreshTokenClaims claims, CancellationToken ct)
    {
        if (await IsFamilyRevokedAsync(claims.Family, ct)) return RefreshOutcome.Fail(RefreshErrors.SessionEnded);

        var head = await _cache.GetAsync<RefreshFamilyHead>(FamilyKey(claims.Family), ct);
        // família desconhecida (expirada, ou anterior a este controle) ou o próprio token atual já revogado (logout)
        if (head is null || head.CurrentJti == claims.Jti) return RefreshOutcome.Fail(RefreshErrors.Invalid);

        if (head.PreviousJti == claims.Jti && DateTime.UtcNow - head.RotatedAt < RotationGrace)
            return RefreshOutcome.Fail(RefreshErrors.AlreadyRotated);

        await RevokeFamilyAsync(claims.Family, ct);
        _audit?.Stage(AuditActions.UserSessionRevoked, AuditSeverity.Critical, "User", claims.UserId.ToString(),
            "Sessão encerrada: um refresh token já usado foi apresentado de novo (possível cópia do token).",
            tenantId: claims.TenantId, actorName: "anonymous", actorType: "system",
            metadata: new { reason = "refresh_reuse", family = claims.Family });
        if (_audit is not null) await _users.SaveChangesAsync(ct);
        return RefreshOutcome.Fail(RefreshErrors.SessionEnded);
    }

    /// <summary>Reserva de uso único do token (INCR atômico): só quem chega primeiro troca. Sem cache (null), ninguém troca.</summary>
    private async Task<bool?> ClaimRotationAsync(string jti, CancellationToken ct)
        => await _cache.IncrementAsync($"refresh:claim:{jti}", RotationClaimTtl, ct) is { } count ? count == 1 : null;

    /// <summary>
    /// Grava o par novo e aposenta o token trocado, nesta ordem: o novo passa a valer, a família aponta para ele (com o
    /// trocado como anterior, para a carência) e só então o antigo deixa de valer. Uma requisição concorrente sempre
    /// encontra um dos dois estados. A sessão registrada passa a seguir o token novo.
    /// </summary>
    private async Task RotateIntoAsync(RefreshTokenClaims spent, TokenPair tokens, RefreshTokenEntry entry, CancellationToken ct)
    {
        var next = _jwt.ValidateRefreshToken(tokens.RefreshToken);
        if (next is not null)
        {
            var ttl = tokens.RefreshTokenExpiresAt - DateTime.UtcNow;
            await _cache.SetAsync($"refresh:valid:{next.Jti}", entry, ttl, ct);
            await _cache.SetAsync(FamilyKey(spent.Family), new RefreshFamilyHead(next.Jti, spent.Jti, DateTime.UtcNow), ttl, ct);
        }
        await _cache.RemoveAsync($"refresh:valid:{spent.Jti}", ct);
        if (next is not null) await _sessions.TouchAsync(spent.Jti, next.Jti, ct);
    }

    private static string FamilyKey(string family) => $"refresh:family:{family}";
    private static string FamilyRevokedKey(string family) => $"refresh:family:{family}:revoked";

    private Task<bool> IsFamilyRevokedAsync(string family, CancellationToken ct)
        => _cache.ExistsAsync(FamilyRevokedKey(family), ct);

    /// <summary>
    /// Encerra a família inteira: nenhum token dela renova mais (a marca vive tanto quanto o mais novo deles poderia), o
    /// token atual sai do cache e a sessão registrada deixa a lista de dispositivos.
    /// </summary>
    private async Task RevokeFamilyAsync(string family, CancellationToken ct)
    {
        await _cache.SetFlagAsync(FamilyRevokedKey(family), _jwt.GetRefreshTokenExpiration() - DateTime.UtcNow, ct);
        var head = await _cache.GetAsync<RefreshFamilyHead>(FamilyKey(family), ct);
        if (head is null) return;
        await _cache.RemoveAsync($"refresh:valid:{head.CurrentJti}", ct);
        await _sessions.RevokeAsync(head.CurrentJti, ct);
    }

    /// <summary>
    /// Issues a new access token scoped to a different store.
    /// Validates the requested store belongs to the current user's tenant
    /// and is in the user's accessible store list.
    /// O par novo nasce do refresh token da sessão (trocado como numa renovação, na mesma família) — nunca só do access
    /// token: sem essa exigência, um access token de 15 minutos virava uma sessão de 7 dias, inclusive depois do logout.
    /// </summary>
    public async Task<SwitchStoreOutcome> SwitchStoreAsync(
        Guid userId,
        Guid tenantId,
        Guid requestedStoreId,
        string refreshToken,
        CancellationToken ct = default)
    {
        // Validate the requested store exists and belongs to this tenant
        var store = await _stores.GetByIdAsync(requestedStoreId, ct);
        if (store is null || store.TenantId != tenantId)
            return SwitchStoreOutcome.Fail(SwitchStoreErrors.StoreForbidden);

        if (store.Status != Domain.Enums.StoreStatus.Active)
            return SwitchStoreOutcome.Fail(SwitchStoreErrors.StoreForbidden);

        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || user.Status != UserStatus.Active)
            return SwitchStoreOutcome.Fail(SwitchStoreErrors.StoreForbidden);

        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null) return SwitchStoreOutcome.Fail(SwitchStoreErrors.StoreForbidden);

        // o refresh token tem de ser o da própria sessão, ainda válido
        var spent = _jwt.ValidateRefreshToken(refreshToken);
        if (spent is null || spent.UserId != userId || spent.TenantId != tenantId)
            return SwitchStoreOutcome.Fail(RefreshErrors.Invalid);
        var entry = await _cache.GetAsync<RefreshTokenEntry>($"refresh:valid:{spent.Jti}", ct);
        if (entry is null)
        {
            // Mesma leitura da renovação. Sem ela, quem perdia a corrida para outra troca de loja (ou renovação) da mesma
            // sessão e chegava depois de a vencedora concluir recebia 401 em vez de 409, e o SPA, ao repetir a troca com
            // o token já gasto, encerrava a sessão de todas as abas. Dentro da carência → 409; fora dela, ou um token
            // mais antigo → a família é encerrada, como na renovação.
            var refused = await RefuseSpentTokenAsync(spent, ct);
            return SwitchStoreOutcome.Fail(refused.ErrorCode == RefreshErrors.AlreadyRotated
                ? RefreshErrors.AlreadyRotated
                : RefreshErrors.Invalid);
        }
        if (entry.UserId != userId
            || (entry.SecurityStamp is not null && entry.SecurityStamp != user.SecurityStamp)
            || await IsFamilyRevokedAsync(spent.Family, ct))
            return SwitchStoreOutcome.Fail(RefreshErrors.Invalid);

        var activeModules = await _tenants.GetActiveModuleKeysAsync(tenantId, ct);
        var expiredModules = await _tenants.GetExpiredModuleKeysAsync(tenantId, ct);
        var trialEndsAt   = await _tenants.GetTrialEndsAtAsync(tenantId, ct);
        var stores = await _stores.GetByTenantIdAsync(tenantId, ct);
        var storeIds = stores.Select(s => s.Id).ToList();

        if (await ClaimRotationAsync(spent.Jti, ct) != true)
            return SwitchStoreOutcome.Fail(RefreshErrors.AlreadyRotated);

        var tokens = _jwt.GenerateTokenPair(user, tenant.Slug, tenant.TradeName ?? tenant.CompanyName, activeModules, requestedStoreId, storeIds,
            spent.Family);
        await RotateIntoAsync(spent, tokens, new RefreshTokenEntry(user.Id, user.TenantId, requestedStoreId, user.SecurityStamp), ct);

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
            TrialEndsAt:   trialEndsAt,
            RequirePasswordChange: user.RequirePasswordChange,
            ReadOnlyModules: expiredModules.ToList());

        return SwitchStoreOutcome.Ok(new SwitchStoreResponse(
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.AccessTokenExpiresAt,
            tokens.RefreshTokenExpiresAt,
            requestedStoreId.ToString(),
            session));
    }

    /// <summary>
    /// Revokes the given refresh token so it cannot be used again.
    /// Idempotent — safe to call multiple times.
    /// Encerra a família inteira do token: as outras abas da mesma sessão e um par trocado agora mesmo por uma
    /// renovação concorrente também deixam de renovar. Outros logins (outros dispositivos) não são afetados.
    /// </summary>
    public async Task LogoutAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;

        var claims = _jwt.ValidateRefreshToken(refreshToken);
        if (claims is null) return;

        await RevokeFamilyAsync(claims.Family, ct);
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

        var account = LoginAttemptGuard.ForUser(user.Id);
        if (_attempts is not null && await _attempts.IsLockedAsync(account, ct))
            return new VerifyManagerResponse(false, null, null, null);
        if (!_hasher.Verify(request.Password, user.PasswordHash))
        {
            if (_attempts is not null) await _attempts.RegisterFailureAsync(account, ct);
            return new VerifyManagerResponse(false, null, null, null);
        }

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

/// <summary>
/// Estado da família (sessão) no cache: o token atual e o que ele substituiu (com a hora da troca), para distinguir a
/// corrida entre abas (carência) da reapresentação de um token já usado.
/// </summary>
public record RefreshFamilyHead(string CurrentJti, string PreviousJti, DateTime RotatedAt);

/// <summary>Códigos de recusa de uma renovação.</summary>
public static class RefreshErrors
{
    /// <summary>Token inválido, desconhecido ou já revogado (401).</summary>
    public const string Invalid = "invalid_refresh";
    /// <summary>A sessão terminou: família encerrada, conta ou empresa inativa, senha trocada (401).</summary>
    public const string SessionEnded = "session_ended";
    /// <summary>Outra aba trocou este token há instantes; o par novo está com ela (409).</summary>
    public const string AlreadyRotated = "refresh_already_rotated";
    /// <summary>Over a budget of the renewal policy: the session is fine, wait for the time given (429).</summary>
    public const string OverBudget = "rate_limited";
    /// <summary>The cache that holds the sessions is not answering: nothing was decided, come back (503).</summary>
    public const string Unavailable = "session_renewal_unavailable";

    /// <summary>A little more than the cache's circuit stays open after a failure.</summary>
    public static readonly TimeSpan UnavailableRetry = TimeSpan.FromSeconds(15);
}

public static class SwitchStoreErrors
{
    /// <summary>Loja inexistente, de outra empresa, inativa, ou usuário sem acesso (403).</summary>
    public const string StoreForbidden = "store_forbidden";
}

public sealed record RefreshOutcome(RefreshResponse? Tokens, string? ErrorCode, TimeSpan? RetryAfter = null)
{
    public static RefreshOutcome Ok(RefreshResponse tokens) => new(tokens, null);
    public static RefreshOutcome Fail(string code) => new(null, code);
    public static RefreshOutcome OverBudget(TimeSpan retryAfter) => new(null, RefreshErrors.OverBudget, retryAfter);
    public static RefreshOutcome Unavailable() => new(null, RefreshErrors.Unavailable, RefreshErrors.UnavailableRetry);
}

public sealed record SwitchStoreOutcome(SwitchStoreResponse? Response, string? ErrorCode)
{
    public static SwitchStoreOutcome Ok(SwitchStoreResponse response) => new(response, null);
    public static SwitchStoreOutcome Fail(string code) => new(null, code);
}
