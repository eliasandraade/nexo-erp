using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
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
    private readonly ICacheService _cache;

    public PlatformController(NexoDbContext db, IJwtTokenService jwt, IPasswordHasher hasher, ICacheService cache)
    {
        _db     = db;
        _jwt    = jwt;
        _hasher = hasher;
        _cache  = cache;
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

        // Grant modules + record history events
        foreach (var moduleKey in req.Modules)
        {
            var sub = ModuleSubscription.CreateAdminGrant(
                tenantId:    tenant.Id,
                moduleKey:   moduleKey,
                grantedById: platformUserId,
                notes:       "Criado via plataforma admin");
            _db.ModuleSubscriptions.Add(sub);

            var evt = ModuleSubscriptionEvent.Create(
                tenantId:  tenant.Id,
                moduleKey: moduleKey,
                eventType: "granted",
                actorId:   platformUserId,
                notes:     "Criado via plataforma admin",
                planType:  "AdminGrant");
            _db.ModuleSubscriptionEvents.Add(evt);
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

        var eventType = existing is not null ? "renewed" : "granted";

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

        var grantEvt = ModuleSubscriptionEvent.Create(
            tenantId:  tenantId,
            moduleKey: req.ModuleKey,
            eventType: eventType,
            actorId:   GetPlatformUserId(),
            notes:     req.Notes,
            planType:  "AdminGrant",
            periodEnd: req.ExpiresAt);
        _db.ModuleSubscriptionEvents.Add(grantEvt);

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

        var revokeEvt = ModuleSubscriptionEvent.Create(
            tenantId:  tenantId,
            moduleKey: moduleKey,
            eventType: "revoked",
            actorId:   GetPlatformUserId());
        _db.ModuleSubscriptionEvents.Add(revokeEvt);

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
                new { name = "api",      status = "healthy",                       latencyMs = 1L      },
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SYSTEM ENDPOINTS
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("system/endpoints")]
    public IActionResult GetEndpoints([FromServices] IActionDescriptorCollectionProvider provider)
    {
        if (!IsPlatformUser()) return Forbid();

        var endpoints = provider.ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(d => d.AttributeRouteInfo?.Template is not null)
            .Select(d =>
            {
                var methods = d.ActionConstraints?
                    .OfType<Microsoft.AspNetCore.Mvc.ActionConstraints.HttpMethodActionConstraint>()
                    .SelectMany(c => c.HttpMethods)
                    .ToArray() ?? Array.Empty<string>();

                // Extract XML summary from [HttpGet/Post/...] or method attributes
                var xmlDoc = d.MethodInfo
                    .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
                    .Cast<System.ComponentModel.DescriptionAttribute>()
                    .FirstOrDefault()?.Description ?? "";

                return new
                {
                    method      = methods.FirstOrDefault() ?? "GET",
                    path        = "/api/" + d.AttributeRouteInfo!.Template,
                    controller  = d.ControllerName,
                    action      = d.ActionName,
                    description = xmlDoc,
                };
            })
            .OrderBy(e => e.path)
            .ThenBy(e => e.method)
            .ToList();

        return Ok(endpoints);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AUDIT LOG
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns audit records, optionally filtered by tenant, action type, severity, or free-text.
    /// Returns the 200 most recent records when no filters are applied.
    /// </summary>
    [HttpGet("audit")]
    public async Task<IActionResult> GetAuditLog(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? search,
        [FromQuery] string? severity,
        [FromQuery] string? actionType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!IsPlatformUser()) return Forbid();

        pageSize = Math.Clamp(pageSize, 1, 200);
        page     = Math.Max(page, 1);

        var query = _db.AuditRecords
            .IgnoreQueryFilters()
            .AsQueryable();

        if (tenantId.HasValue)
            query = query.Where(a => a.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(severity))
            query = query.Where(a => a.Severity == severity.ToLowerInvariant());

        if (!string.IsNullOrWhiteSpace(actionType))
            query = query.Where(a => a.ActionType == actionType.ToLowerInvariant());

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(a =>
                a.Description.Contains(search) ||
                (a.ActorName != null && a.ActorName.Contains(search)) ||
                a.ActionType.Contains(search) ||
                a.EntityType.Contains(search));

        var total = await query.CountAsync(ct);

        var records = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                id          = a.Id,
                tenantId    = a.TenantId,
                actionType  = a.ActionType,
                severity    = a.Severity,
                actorId     = a.ActorId,
                actorName   = a.ActorName,
                actorType   = a.ActorType,
                entityType  = a.EntityType,
                entityId    = a.EntityId,
                description = a.Description,
                ipAddress   = a.IpAddress,
                createdAt   = a.CreatedAt,
            })
            .ToListAsync(ct);

        return Ok(new { total, page, pageSize, records });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TENANT NOTES (internal CRM)
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("tenants/{tenantId:guid}/notes")]
    public async Task<IActionResult> GetNotes(Guid tenantId, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var notes = await _db.TenantNotes
            .Where(n => n.TenantId == tenantId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new
            {
                id           = n.Id,
                content      = n.Content,
                authorName   = n.AuthorName,
                authorId     = n.AuthorId,
                isPinned     = n.IsPinned,
                createdAt    = n.CreatedAt,
            })
            .ToListAsync(ct);

        return Ok(notes);
    }

    public record CreateNoteRequest(string Content, bool IsPinned = false);

    [HttpPost("tenants/{tenantId:guid}/notes")]
    public async Task<IActionResult> CreateNote(Guid tenantId, [FromBody] CreateNoteRequest req, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var platformUserId   = GetPlatformUserId();
        var platformUserEmail = User.FindFirstValue(ClaimTypes.Email)
                                ?? User.FindFirstValue("email") ?? "admin";

        var note = TenantNote.Create(tenantId, req.Content, platformUserId, platformUserEmail, req.IsPinned);
        _db.TenantNotes.Add(note);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetNotes), new { tenantId }, new { id = note.Id });
    }

    [HttpDelete("tenants/{tenantId:guid}/notes/{noteId:guid}")]
    public async Task<IActionResult> DeleteNote(Guid tenantId, Guid noteId, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var note = await _db.TenantNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.TenantId == tenantId, ct);
        if (note is null) return NotFound();

        _db.TenantNotes.Remove(note);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPatch("tenants/{tenantId:guid}/notes/{noteId:guid}/pin")]
    public async Task<IActionResult> TogglePin(Guid tenantId, Guid noteId, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var note = await _db.TenantNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.TenantId == tenantId, ct);
        if (note is null) return NotFound();

        note.TogglePin();
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PASSWORD RESET
    // ─────────────────────────────────────────────────────────────────────────

    public record ResetPasswordRequest(string NewPassword);

    [HttpPost("tenants/{tenantId:guid}/users/{userId:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(
        Guid tenantId, Guid userId,
        [FromBody] ResetPasswordRequest req,
        CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 6)
            return BadRequest(new { error = "Senha deve ter pelo menos 6 caracteres." });

        var user = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, ct);

        if (user is null) return NotFound();

        var hash = _hasher.Hash(req.NewPassword);
        user.ChangePasswordHash(hash, clearRequireChange: false);
        user.BumpSecurityStamp();

        // Purge all active sessions for this user from Redis + DB
        await RevokeUserSessionsAsync(userId, ct);

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FORCE LOGOUT (session revocation via SecurityStamp)
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost("tenants/{tenantId:guid}/users/{userId:guid}/force-logout")]
    public async Task<IActionResult> ForceLogout(Guid tenantId, Guid userId, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var user = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, ct);

        if (user is null) return NotFound();

        user.BumpSecurityStamp();
        await RevokeUserSessionsAsync(userId, ct);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SESSIONS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Lists active (non-expired, non-revoked) sessions for a user.</summary>
    [HttpGet("tenants/{tenantId:guid}/users/{userId:guid}/sessions")]
    public async Task<IActionResult> GetSessions(Guid tenantId, Guid userId, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var sessions = await _db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => s.UserId == userId
                     && s.TenantId == tenantId
                     && !s.IsRevoked
                     && s.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(s => s.LastUsedAt)
            .Select(s => new
            {
                id          = s.Id,
                ipAddress   = s.IpAddress,
                userAgent   = s.UserAgent,
                lastUsedAt  = s.LastUsedAt,
                createdAt   = s.CreatedAt,
                expiresAt   = s.ExpiresAt,
            })
            .ToListAsync(ct);

        return Ok(sessions);
    }

    /// <summary>
    /// Revokes ALL sessions for a user: bumps SecurityStamp, removes refresh tokens
    /// from Redis, and marks DB session rows as revoked.
    /// </summary>
    [HttpDelete("tenants/{tenantId:guid}/users/{userId:guid}/sessions")]
    public async Task<IActionResult> RevokeAllSessions(Guid tenantId, Guid userId, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var user = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, ct);

        if (user is null) return NotFound();

        user.BumpSecurityStamp();
        await RevokeUserSessionsAsync(userId, ct);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TRIAL EXPIRED
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns tenants whose trial period has expired (TrialEndsAt &lt; now)
    /// or whose active subscriptions have a past CurrentPeriodEnd.
    /// </summary>
    [HttpGet("tenants/trial-expired")]
    public async Task<IActionResult> GetTrialExpired(CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var now = DateTime.UtcNow;

        // Tenants with explicit TrialEndsAt in the past
        var trialExpired = await _db.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.TrialEndsAt != null && t.TrialEndsAt < now && t.Status == TenantStatus.Active)
            .Select(t => new
            {
                t.Id, t.CompanyName, t.TradeName, t.Email, t.Status,
                t.TrialEndsAt, t.CreatedAt,
                expiredDaysAgo = (int)Math.Floor((now - t.TrialEndsAt!.Value).TotalDays),
                expiredReason  = "trial",
            })
            .ToListAsync(ct);

        // Tenants with active subscriptions whose period has ended
        var subExpired = await _db.ModuleSubscriptions
            .IgnoreQueryFilters()
            .Where(s => s.Status == SubscriptionStatus.Active
                     && s.CurrentPeriodEnd != null
                     && s.CurrentPeriodEnd < now)
            .Join(_db.Tenants.IgnoreQueryFilters(),
                  s => s.TenantId,
                  t => t.Id,
                  (s, t) => new
                  {
                      t.Id, t.CompanyName, t.TradeName, t.Email, t.Status,
                      t.TrialEndsAt, t.CreatedAt,
                      expiredDaysAgo = (int)Math.Floor((now - s.CurrentPeriodEnd!.Value).TotalDays),
                      expiredReason  = "subscription:" + s.ModuleKey,
                  })
            .ToListAsync(ct);

        // Merge, deduplicate by tenant id (keep most-expired entry)
        var all = trialExpired
            .Cast<dynamic>()
            .Concat(subExpired.Cast<dynamic>())
            .GroupBy(x => (Guid)x.Id)
            .Select(g => g.OrderByDescending(x => (int)x.expiredDaysAgo).First())
            .OrderByDescending(x => (int)x.expiredDaysAgo)
            .ToList();

        return Ok(all);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PLAN HISTORY
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the subscription event history for a tenant, newest first.
    /// Events are recorded on: grant, renew, revoke.
    /// </summary>
    [HttpGet("tenants/{tenantId:guid}/plan-history")]
    public async Task<IActionResult> GetPlanHistory(Guid tenantId, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var events = await _db.ModuleSubscriptionEvents
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new
            {
                id        = e.Id,
                moduleKey = e.ModuleKey,
                eventType = e.EventType,
                planType  = e.PlanType,
                periodEnd = e.PeriodEnd,
                notes     = e.Notes,
                actorId   = e.ActorId,
                createdAt = e.CreatedAt,
            })
            .ToListAsync(ct);

        return Ok(events);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MRR / ARR
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calculates MRR and ARR from active subscriptions joined with ModuleDefinition pricing.
    ///
    /// Active = Status == Active AND (CurrentPeriodEnd == null OR CurrentPeriodEnd &gt; now).
    /// Monthly normalization:
    ///   Monthly     → PriceMonthly
    ///   Quarterly   → PriceQuarterly / 3
    ///   Semiannual  → PriceSemiannual / 6
    ///   Annual      → PriceAnnual / 12
    ///   Lifetime / AdminGrant / Trial → R$ 0 (counted separately as non-paying)
    /// ARR = MRR × 12.
    /// </summary>
    [HttpGet("mrr")]
    public async Task<IActionResult> GetMrr(CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var now = DateTime.UtcNow;

        var activeSubs = await _db.ModuleSubscriptions
            .IgnoreQueryFilters()
            .Where(s => s.Status == SubscriptionStatus.Active
                     && (s.CurrentPeriodEnd == null || s.CurrentPeriodEnd > now))
            .ToListAsync(ct);

        var definitions = await _db.ModuleDefinitions
            .ToDictionaryAsync(d => d.Key, ct);

        decimal totalMrr = 0m;
        var byModule = new Dictionary<string, decimal>();

        foreach (var sub in activeSubs)
        {
            if (!definitions.TryGetValue(sub.ModuleKey, out var def)) continue;

            decimal monthlyEquivalent = sub.PlanType switch
            {
                PlanType.Monthly    => def.PriceMonthly    ?? 0m,
                PlanType.Quarterly  => (def.PriceQuarterly ?? 0m) / 3m,
                PlanType.Semiannual => (def.PriceSemiannual ?? 0m) / 6m,
                PlanType.Annual     => (def.PriceAnnual    ?? 0m) / 12m,
                _                   => 0m,   // Lifetime, AdminGrant, Trial
            };

            totalMrr += monthlyEquivalent;
            byModule[sub.ModuleKey] = byModule.GetValueOrDefault(sub.ModuleKey) + monthlyEquivalent;
        }

        var nonPayingCount = activeSubs.Count(s =>
            s.PlanType is PlanType.AdminGrant or PlanType.Trial or PlanType.Lifetime);

        return Ok(new
        {
            mrr                    = Math.Round(totalMrr, 2),
            arr                    = Math.Round(totalMrr * 12m, 2),
            activeSubscriptions    = activeSubs.Count,
            payingSubscriptions    = activeSubs.Count - nonPayingCount,
            nonPayingSubscriptions = nonPayingCount,
            byModule = byModule
                .Select(kv => new { moduleKey = kv.Key, mrr = Math.Round(kv.Value, 2) })
                .OrderByDescending(x => x.mrr)
                .ToList(),
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CHURN
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns churn metrics for the given period (default 30 days).
    ///
    /// Churn rate = subscriptions canceled in period / (active now + canceled in period).
    /// Also returns previous-period canceled count for trend comparison.
    /// </summary>
    [HttpGet("churn")]
    public async Task<IActionResult> GetChurn(
        [FromQuery] int period = 30,
        CancellationToken ct = default)
    {
        if (!IsPlatformUser()) return Forbid();

        period = Math.Clamp(period, 1, 365);
        var now   = DateTime.UtcNow;
        var since = now.AddDays(-period);
        var prevSince = since.AddDays(-period);

        var canceledCount = await _db.ModuleSubscriptions
            .IgnoreQueryFilters()
            .CountAsync(s => s.Status == SubscriptionStatus.Canceled
                          && s.CanceledAt >= since, ct);

        var activeCount = await _db.ModuleSubscriptions
            .IgnoreQueryFilters()
            .CountAsync(s => s.Status == SubscriptionStatus.Active
                          && (s.CurrentPeriodEnd == null || s.CurrentPeriodEnd > now), ct);

        var totalInPeriod = activeCount + canceledCount;
        var churnRate = totalInPeriod > 0
            ? Math.Round((double)canceledCount / totalInPeriod * 100.0, 1)
            : 0.0;

        var prevCanceledCount = await _db.ModuleSubscriptions
            .IgnoreQueryFilters()
            .CountAsync(s => s.Status == SubscriptionStatus.Canceled
                          && s.CanceledAt >= prevSince
                          && s.CanceledAt < since, ct);

        return Ok(new
        {
            period,
            canceledSubscriptions  = canceledCount,
            activeSubscriptions    = activeCount,
            churnRate,
            previousPeriodCanceled = prevCanceledCount,
            trend = canceledCount - prevCanceledCount,  // positive = more churn than prev period
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PRIVATE HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes all active session Redis entries for a user and marks DB rows as revoked.
    /// Does NOT save changes — caller must call SaveChangesAsync.
    /// Also invalidates the SecurityStamp Redis cache so the middleware reloads from DB.
    /// </summary>
    private async Task RevokeUserSessionsAsync(Guid userId, CancellationToken ct)
    {
        var sessions = await _db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => s.UserId == userId && !s.IsRevoked)
            .ToListAsync(ct);

        foreach (var s in sessions)
        {
            s.Revoke();
            await _cache.RemoveAsync($"refresh:valid:{s.RefreshJti}", ct);
        }

        // Clear stamp cache so middleware reloads the new stamp from DB on next request
        await _cache.RemoveAsync($"user:stamp:{userId}", ct);
    }
}
