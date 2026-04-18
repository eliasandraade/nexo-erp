using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using DomainUser = Nexo.Domain.Entities.User;

namespace Nexo.Api.Controllers;

/// <summary>
/// Platform administration endpoints. Require a valid platform JWT (type: "platform").
/// No tenant context — queries bypass Global Query Filters to see all data.
/// </summary>
[ApiController]
[Route("api/platform")]
[Authorize]
public class PlatformController : ControllerBase
{
    private readonly NexoDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly IPasswordHasher _hasher;

    public PlatformController(NexoDbContext db, IJwtTokenService jwt, IPasswordHasher hasher)
    {
        _db     = db;
        _jwt    = jwt;
        _hasher = hasher;
    }

    private bool IsPlatformUser() =>
        User.FindFirstValue("type") == "platform";

    private Guid? GetPlatformUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TENANT LIST & DETAIL
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Returns all tenants with their stores, active modules, and user counts.</summary>
    [HttpGet("tenants")]
    public async Task<IActionResult> GetTenants(CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var tenants = await _db.Tenants
            .IgnoreQueryFilters()
            .OrderBy(t => t.CompanyName)
            .ToListAsync(ct);

        var tenantIds = tenants.Select(t => t.Id).ToList();

        var stores = await _db.Stores
            .IgnoreQueryFilters()
            .Where(s => tenantIds.Contains(s.TenantId))
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        var subscriptions = await _db.ModuleSubscriptions
            .IgnoreQueryFilters()
            .Where(s => tenantIds.Contains(s.TenantId) && s.Status == SubscriptionStatus.Active)
            .ToListAsync(ct);

        var userCounts = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => tenantIds.Contains(u.TenantId))
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var result = tenants.Select(t => new
        {
            id          = t.Id,
            companyName = t.CompanyName,
            tradeName   = t.TradeName,
            slug        = t.Slug,
            status      = t.Status.ToString(),
            email       = t.Email,
            taxId       = t.TaxId,
            phone       = t.Phone,
            businessType = t.BusinessType,
            createdAt   = t.CreatedAt,
            modules     = subscriptions
                            .Where(s => s.TenantId == t.Id)
                            .Select(s => s.ModuleKey)
                            .ToList(),
            stores      = stores
                            .Where(s => s.TenantId == t.Id)
                            .Select(s => new { id = s.Id, name = s.Name, slug = s.Slug, status = s.Status.ToString() })
                            .ToList(),
            userCount   = userCounts.FirstOrDefault(u => u.TenantId == t.Id)?.Count ?? 0,
        });

        return Ok(result);
    }

    /// <summary>Returns a single tenant with full detail: stores, subscriptions, users.</summary>
    [HttpGet("tenants/{tenantId:guid}")]
    public async Task<IActionResult> GetTenant(Guid tenantId, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        if (tenant is null) return NotFound();

        var stores = await _db.Stores
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        var subscriptions = await _db.ModuleSubscriptions
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId)
            .ToListAsync(ct);

        var users = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.FullName)
            .ToListAsync(ct);

        return Ok(new
        {
            id            = tenant.Id,
            companyName   = tenant.CompanyName,
            tradeName     = tenant.TradeName,
            slug          = tenant.Slug,
            status        = tenant.Status.ToString(),
            email         = tenant.Email,
            taxId         = tenant.TaxId,
            phone         = tenant.Phone,
            businessType  = tenant.BusinessType,
            createdAt     = tenant.CreatedAt,
            trialEndsAt   = tenant.TrialEndsAt,
            subscriptions = subscriptions.Select(s => new
            {
                id               = s.Id,
                moduleKey        = s.ModuleKey,
                status           = s.Status.ToString(),
                planType         = s.PlanType.ToString(),
                currentPeriodEnd = s.CurrentPeriodEnd,
                cancelAtPeriodEnd= s.CancelAtPeriodEnd,
            }),
            stores = stores.Select(s => new
            {
                id     = s.Id,
                name   = s.Name,
                slug   = s.Slug,
                status = s.Status.ToString(),
            }),
            users = users.Select(u => new
            {
                id     = u.Id,
                name   = u.FullName,
                login  = u.Login,
                email  = u.Email,
                role   = u.Role.ToString(),
                status = u.Status.ToString(),
                lastAccessAt = u.LastAccessAt,
            }),
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CREATE TENANT
    // ─────────────────────────────────────────────────────────────────────────

    public record CreateTenantRequest(
        string CompanyName,
        string TaxId,
        string Email,
        string? TradeName,
        string? Phone,
        string? BusinessType,
        string[] Modules,
        // Initial admin user
        string AdminName,
        string AdminLogin,
        string AdminPassword,
        string? AdminEmail);

    /// <summary>Creates a new tenant with an initial Diretoria admin user and module grants.</summary>
    [HttpPost("tenants")]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest req, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        // Check uniqueness
        var emailExists = await _db.Tenants
            .IgnoreQueryFilters()
            .AnyAsync(t => t.Email == req.Email.Trim().ToLowerInvariant(), ct);
        if (emailExists)
            return Conflict(new { error = "E-mail já cadastrado para outro tenant." });

        var platformUserId = GetPlatformUserId();

        // Create tenant
        var tenant = Tenant.Create(
            companyName:  req.CompanyName,
            taxId:        req.TaxId,
            email:        req.Email,
            tradeName:    req.TradeName,
            phone:        req.Phone,
            businessType: req.BusinessType);

        _db.Tenants.Add(tenant);

        // Create initial store (same name as company)
        var storeName = req.TradeName ?? req.CompanyName;
        var storeSlug = storeName.Trim().ToLowerInvariant()
            .Replace(" ", "-").Replace(".", "").Replace("/", "").Replace("&", "e");
        storeSlug = $"{storeSlug}-{Guid.NewGuid().ToString("N")[..6]}";
        var store = Store.Create(tenant.Id, storeName, storeSlug);
        _db.Stores.Add(store);

        // Create admin user
        var hash = _hasher.Hash(req.AdminPassword);
        var adminUser = DomainUser.Create(
            tenant.Id,
            req.AdminName,
            req.AdminEmail ?? req.Email,
            req.AdminLogin,
            hash,
            UserRole.Diretoria,
            phone: null,
            notes: null,
            requirePasswordChange: true);

        _db.Users.Add(adminUser);

        // Grant modules
        foreach (var moduleKey in req.Modules)
        {
            var sub = ModuleSubscription.CreateAdminGrant(
                tenantId:    tenant.Id,
                moduleKey:   moduleKey,
                grantedById: platformUserId,
                notes:       "Criado via plataforma admin");
            _db.ModuleSubscriptions.Add(sub);
        }

        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetTenant), new { tenantId = tenant.Id }, new { id = tenant.Id, slug = tenant.Slug });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UPDATE TENANT
    // ─────────────────────────────────────────────────────────────────────────

    public record UpdateTenantRequest(
        string CompanyName,
        string? TradeName,
        string TaxId,
        string Email,
        string? Phone,
        string? BusinessType);

    [HttpPut("tenants/{tenantId:guid}")]
    public async Task<IActionResult> UpdateTenant(Guid tenantId, [FromBody] UpdateTenantRequest req, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        tenant.Update(req.CompanyName, req.TradeName, req.TaxId, req.Email, req.Phone, req.BusinessType);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STATUS
    // ─────────────────────────────────────────────────────────────────────────

    public record SetTenantStatusRequest(string Status);

    [HttpPut("tenants/{tenantId:guid}/status")]
    public async Task<IActionResult> SetTenantStatus(Guid tenantId, [FromBody] SetTenantStatusRequest req, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        if (!Enum.TryParse<TenantStatus>(req.Status, true, out var newStatus))
            return BadRequest(new { error = "Status inválido." });

        tenant.SetStatus(newStatus);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MODULES (admin grant)
    // ─────────────────────────────────────────────────────────────────────────

    public record GrantModuleRequest(string ModuleKey, DateTime? ExpiresAt, string? Notes);

    [HttpPost("tenants/{tenantId:guid}/modules")]
    public async Task<IActionResult> GrantModule(Guid tenantId, [FromBody] GrantModuleRequest req, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        // Reactivate existing or create new
        var existing = await _db.ModuleSubscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.ModuleKey == req.ModuleKey.ToLowerInvariant(), ct);

        if (existing is not null)
        {
            existing.Renew(req.ExpiresAt ?? DateTime.UtcNow.AddYears(10));
        }
        else
        {
            var sub = ModuleSubscription.CreateAdminGrant(
                tenantId:    tenantId,
                moduleKey:   req.ModuleKey,
                grantedById: GetPlatformUserId(),
                expiresAt:   req.ExpiresAt,
                notes:       req.Notes ?? "Concedido via plataforma admin");
            _db.ModuleSubscriptions.Add(sub);
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("tenants/{tenantId:guid}/modules/{moduleKey}")]
    public async Task<IActionResult> RevokeModule(Guid tenantId, string moduleKey, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var sub = await _db.ModuleSubscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.ModuleKey == moduleKey.ToLowerInvariant(), ct);

        if (sub is null) return NotFound();

        sub.Cancel();
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IMPERSONATION
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a token pair for the tenant's first Diretoria user so the platform
    /// admin can enter the tenant's session.
    /// </summary>
    [HttpPost("tenants/{tenantId:guid}/impersonate")]
    public async Task<IActionResult> Impersonate(Guid tenantId, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        if (tenant is null) return NotFound();

        // Find first Diretoria user
        var adminUser = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && u.Role == UserRole.Diretoria && u.Status == UserStatus.Active)
            .OrderBy(u => u.FullName)
            .FirstOrDefaultAsync(ct);

        if (adminUser is null)
            return BadRequest(new { error = "Nenhum usuário Diretoria ativo encontrado neste tenant." });

        var stores = await _db.Stores
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.Status == StoreStatus.Active)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        if (!stores.Any())
            return BadRequest(new { error = "Tenant não possui lojas ativas." });

        var activeModules = await _db.ModuleSubscriptions
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.Status == SubscriptionStatus.Active)
            .Select(s => s.ModuleKey)
            .ToListAsync(ct);

        var primaryStore = stores.First();
        var storeIds     = stores.Select(s => s.Id).ToList();

        var tokens = _jwt.GenerateTokenPair(
            user:                adminUser,
            tenantSlug:          tenant.Slug,
            companyName:         tenant.CompanyName,
            activeModules:       activeModules,
            storeId:             primaryStore.Id,
            accessibleStoreIds:  storeIds);

        return Ok(new
        {
            accessToken  = tokens.AccessToken,
            refreshToken = tokens.RefreshToken,
            session = new
            {
                userId      = adminUser.Id,
                tenantId    = tenantId,
                companyName = tenant.CompanyName,
                name        = adminUser.FullName,
                login       = adminUser.Login,
                email       = adminUser.Email,
                role        = adminUser.Role.ToString(),
                storeId     = primaryStore.Id,
                storeIds    = storeIds,
                activeModules = activeModules,
                type        = "tenant",
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STATS
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var tenantCount  = await _db.Tenants.IgnoreQueryFilters().CountAsync(ct);
        var activeCount  = await _db.Tenants.IgnoreQueryFilters().CountAsync(t => t.Status == TenantStatus.Active, ct);
        var storeCount   = await _db.Stores.IgnoreQueryFilters().CountAsync(ct);
        var userCount    = await _db.Users.IgnoreQueryFilters().CountAsync(ct);
        var moduleCount  = await _db.ModuleSubscriptions.IgnoreQueryFilters().CountAsync(s => s.Status == SubscriptionStatus.Active, ct);

        var moduleBreakdown = await _db.ModuleSubscriptions
            .IgnoreQueryFilters()
            .Where(s => s.Status == SubscriptionStatus.Active)
            .GroupBy(s => s.ModuleKey)
            .Select(g => new { moduleKey = g.Key, count = g.Count() })
            .ToListAsync(ct);

        var recentTenants = await _db.Tenants
            .IgnoreQueryFilters()
            .OrderByDescending(t => t.CreatedAt)
            .Take(5)
            .Select(t => new { t.Id, t.CompanyName, t.TradeName, t.Status, t.CreatedAt, t.Email })
            .ToListAsync(ct);

        return Ok(new
        {
            tenantCount,
            activeCount,
            suspendedCount = tenantCount - activeCount,
            storeCount,
            userCount,
            activeSubscriptions = moduleCount,
            moduleBreakdown,
            recentTenants,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HEALTH
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("health")]
    public async Task<IActionResult> GetHealth(CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        // DB check
        var dbOk    = false;
        var dbLatMs = 0L;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            dbOk = await _db.Database.CanConnectAsync(ct);
            sw.Stop();
            dbLatMs = sw.ElapsedMilliseconds;
        }
        catch { /* dbOk stays false */ }

        return Ok(new
        {
            status    = dbOk ? "healthy" : "degraded",
            timestamp = DateTime.UtcNow,
            checks = new[]
            {
                new { name = "database", status = dbOk ? "healthy" : "unhealthy", latencyMs = dbLatMs },
                new { name = "api",      status = "healthy",                       latencyMs = 0L      },
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SYSTEM ENDPOINTS
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("system/endpoints")]
    public IActionResult GetEndpoints([FromServices] IEnumerable<ControllerActionDescriptor> descriptors)
    {
        if (!IsPlatformUser()) return Forbid();

        var endpoints = descriptors
            .Where(d => d.AttributeRouteInfo?.Template is not null)
            .Select(d =>
            {
                var methods = d.ActionConstraints?
                    .OfType<Microsoft.AspNetCore.Mvc.ActionConstraints.HttpMethodActionConstraint>()
                    .SelectMany(c => c.HttpMethods)
                    .ToArray() ?? Array.Empty<string>();

                return new
                {
                    method      = methods.FirstOrDefault() ?? "GET",
                    path        = "/" + d.AttributeRouteInfo!.Template,
                    controller  = d.ControllerName,
                    action      = d.ActionName,
                    description = d.MethodInfo
                        .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
                        .Cast<System.ComponentModel.DescriptionAttribute>()
                        .FirstOrDefault()?.Description ?? "",
                };
            })
            .OrderBy(e => e.path)
            .ThenBy(e => e.method)
            .ToList();

        return Ok(endpoints);
    }
}
