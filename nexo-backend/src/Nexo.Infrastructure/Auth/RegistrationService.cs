using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Auth;

/// <summary>
/// Handles user self-registration, email verification and resend.
/// Encapsulates the full tenant bootstrapping logic:
///   Register → [Tenant + ModuleSubscription + Store + User + AppSettings] → SendEmail
///   Verify   → Activate user + issue JWT (auto-login)
/// </summary>
public class RegistrationService
{
    private readonly NexoDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;
    private readonly ICacheService _cache;
    private readonly IEmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<RegistrationService> _logger;

    public RegistrationService(
        NexoDbContext db,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        ICacheService cache,
        IEmailService email,
        IConfiguration config,
        ILogger<RegistrationService> logger)
    {
        _db     = db;
        _hasher = hasher;
        _jwt    = jwt;
        _cache  = cache;
        _email  = email;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Creates tenant + user + store + subscription + settings.
    /// Sends verification email.
    /// Returns null on success or an error code string.
    /// </summary>
    public async Task<string?> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var name  = request.Name.Trim();

        // 1. Email uniqueness check (all tenants)
        var emailExists = await _db.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email == email, ct);
        if (emailExists) return "email_already_registered";

        // 2. Validate password length
        if (request.Password.Length < 6) return "password_too_short";

        // 3. Create tenant
        // TaxId uses a unique placeholder — the user will fill the real CNPJ in settings.
        // Empty string would violate the unique index when multiple tenants register.
        var tenant = Tenant.Create(
            companyName:  name,
            taxId:        $"_pending_{Guid.NewGuid():N}",
            email:        email);
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(ct);

        // 4. Create varejo module subscription
        var subscription = ModuleSubscription.CreateAdminGrant(tenant.Id, "varejo");
        _db.ModuleSubscriptions.Add(subscription);
        await _db.SaveChangesAsync(ct);

        // 5. Create default store
        var store = Store.Create(
            tenantId:             tenant.Id,
            name:                 "Loja Principal",
            slug:                 "loja-principal",
            moduleSubscriptionId: subscription.Id);
        _db.Stores.Add(store);
        await _db.SaveChangesAsync(ct);

        // 6. Create user (PendingVerification)
        var user = User.Create(
            tenantId:     tenant.Id,
            fullName:     name,
            email:        email,
            login:        email,
            passwordHash: _hasher.Hash(request.Password),
            role:         UserRole.Diretoria);
        user.SetStatus(UserStatus.PendingVerification);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        // 7. Create default AppSettings
        var escapedName = name.Replace("\"", "\\\"");
        var settings = AppSettings.CreateForTenant(
            tenantId:      tenant.Id,
            companyJson:   $"{{\"name\":\"{escapedName}\",\"tradeName\":\"{escapedName}\",\"cnpj\":\"\",\"email\":\"{email}\",\"phone\":\"\"}}",
            operationJson: "{\"defaultOperator\":\"\"}",
            inventoryJson: "{\"noMovementAlertDays\":30,\"minStockBehavior\":\"alert\",\"enableLowStockAlerts\":true,\"enableZeroStockAlerts\":true,\"enableHighRotationAlerts\":false}",
            commissionJson:"{\"defaultCommissionRate\":3,\"enableProductCommission\":false,\"policyNotes\":\"\"}",
            posJson:       "{\"allowValueDiscount\":true,\"allowPercentDiscount\":true,\"requireManagerAuth\":false,\"maxDiscountPercent\":20}",
            systemJson:    "{\"language\":\"pt-BR\",\"dateFormat\":\"dd/MM/yyyy\",\"currencySymbol\":\"R$\"}");
        _db.AppSettings.Add(settings);
        await _db.SaveChangesAsync(ct);

        // 8. Generate verification token and store in Redis (24h TTL)
        var token = Guid.NewGuid().ToString("N");
        await _cache.SetAsync(
            $"verify:token:{token}",
            user.Id.ToString(),
            TimeSpan.FromHours(24),
            ct);

        // 9. Send verification email
        var frontendUrl = _config["App:FrontendUrl"] ?? "http://localhost:5173";
        var verificationUrl = $"{frontendUrl}/verify-email?token={token}";
        await _email.SendVerificationEmailAsync(email, name, verificationUrl, ct);

        _logger.LogInformation(
            "Registration: new tenant {TenantId}, user {UserId}, email {Email}",
            tenant.Id, user.Id, email);

        return null; // success
    }

    /// <summary>
    /// Validates email verification token, activates user, and issues JWT (auto-login).
    /// Returns null if token is invalid or expired.
    /// </summary>
    public async Task<LoginResponse?> VerifyEmailAsync(string token, CancellationToken ct = default)
    {
        var cacheKey = $"verify:token:{token}";
        var userIdStr = await _cache.GetAsync<string>(cacheKey, ct);
        if (userIdStr is null) return null;

        if (!Guid.TryParse(userIdStr, out var userId)) return null;

        // Load user (bypass query filters — no tenant context yet)
        var user = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null || user.Status != UserStatus.PendingVerification) return null;

        // Activate user
        user.Activate();
        await _db.SaveChangesAsync(ct);

        // Remove token (one-time use)
        await _cache.RemoveAsync(cacheKey, ct);

        // Build JWT (need tenant + stores)
        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstAsync(t => t.Id == user.TenantId, ct);

        var activeModules = await _db.ModuleSubscriptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.TenantId == tenant.Id && s.Status == SubscriptionStatus.Active)
            .Select(s => s.ModuleKey)
            .ToListAsync(ct);

        var stores = await _db.Stores
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.TenantId == tenant.Id && s.Status == StoreStatus.Active)
            .ToListAsync(ct);

        var defaultStore = stores.FirstOrDefault();
        var storeId  = defaultStore?.Id ?? Guid.Empty;
        var storeIds = stores.Select(s => s.Id).ToList();

        var companyName = tenant.TradeName ?? tenant.CompanyName;
        var tokens = _jwt.GenerateTokenPair(user, tenant.Slug, companyName, activeModules, storeId, storeIds);

        // Store refresh token in Redis
        var refreshClaims = _jwt.ValidateRefreshToken(tokens.RefreshToken);
        if (refreshClaims is not null)
        {
            var ttl = tokens.RefreshTokenExpiresAt - DateTime.UtcNow;
            await _cache.SetAsync(
                $"refresh:valid:{refreshClaims.Jti}",
                new RefreshTokenEntry(user.Id, user.TenantId),
                ttl, ct);
        }

        return new LoginResponse(
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.AccessTokenExpiresAt,
            new SessionDto(
                UserId:        user.Id.ToString(),
                TenantId:      user.TenantId.ToString(),
                Name:          user.FullName,
                Role:          user.Role.ToString().ToLowerInvariant(),
                Login:         user.Login,
                Email:         user.Email,
                ActiveModules: activeModules,
                StoreId:       storeId == Guid.Empty ? null : storeId.ToString(),
                StoreIds:      storeIds.Select(id => id.ToString()).ToList(),
                CompanyName:   companyName,
                IsNewAccount:  true));
    }

    /// <summary>
    /// Resends verification email. Regenerates token (old one remains valid until TTL).
    /// Silently succeeds even if email not found (prevent email enumeration).
    /// </summary>
    public async Task ResendVerificationAsync(string email, CancellationToken ct = default)
    {
        var user = await _db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant()
                                   && u.Status == UserStatus.PendingVerification, ct);

        if (user is null) return; // Silent — don't leak whether email exists

        var token = Guid.NewGuid().ToString("N");
        await _cache.SetAsync($"verify:token:{token}", user.Id.ToString(), TimeSpan.FromHours(24), ct);

        var frontendUrl = _config["App:FrontendUrl"] ?? "http://localhost:5173";
        var verificationUrl = $"{frontendUrl}/verify-email?token={token}";
        await _email.SendVerificationEmailAsync(email, user.FullName, verificationUrl, ct);
    }
}
