using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;

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

    public PlatformController(NexoDbContext db) => _db = db;

    private bool IsPlatformUser() =>
        User.FindFirstValue("type") == "platform";

    /// <summary>
    /// Returns all tenants with their stores, active modules, and user counts.
    /// </summary>
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
            modules     = subscriptions
                            .Where(s => s.TenantId == t.Id)
                            .Select(s => s.ModuleKey)
                            .ToList(),
            stores      = stores
                            .Where(s => s.TenantId == t.Id)
                            .Select(s => new
                            {
                                id     = s.Id,
                                name   = s.Name,
                                slug   = s.Slug,
                                status = s.Status.ToString(),
                            })
                            .ToList(),
            userCount   = userCounts.FirstOrDefault(u => u.TenantId == t.Id)?.Count ?? 0,
        });

        return Ok(result);
    }

    /// <summary>
    /// Returns a single tenant with full detail: stores, subscriptions, users.
    /// </summary>
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
            subscriptions = subscriptions.Select(s => new
            {
                id        = s.Id,
                moduleKey = s.ModuleKey,
                status    = s.Status.ToString(),
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
                id    = u.Id,
                name  = u.FullName,
                login = u.Login,
                email = u.Email,
                role  = u.Role.ToString(),
                status = u.Status.ToString(),
            }),
        });
    }
}
