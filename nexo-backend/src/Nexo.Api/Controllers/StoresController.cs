using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Stores;

namespace Nexo.Api.Controllers;

/// <summary>
/// Exposes read-only tenant info to authenticated users.
/// Users may only query their own tenant — cross-tenant queries return 403.
/// Platform-level tenant management (create, suspend, billing) is handled
/// by internal tooling, not this controller.
/// </summary>
[ApiController]
[Route("api/tenants")]
[Authorize]
public class TenantsController : ControllerBase
{
    private readonly TenantService _tenantService;
    private readonly ICurrentTenant _currentTenant;

    public TenantsController(TenantService tenantService, ICurrentTenant currentTenant)
    {
        _tenantService  = tenantService;
        _currentTenant  = currentTenant;
    }

    /// <summary>Returns the authenticated user's own tenant info.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TenantDto>> GetById(Guid id, CancellationToken ct)
    {
        // Enforce: a user may only query their own tenant.
        if (id != _currentTenant.Id)
            return Forbid();

        var tenant = await _tenantService.GetByIdAsync(id, ct);
        return Ok(tenant);
    }

    /// <summary>Returns tenant info by slug. Restricted to own tenant slug.</summary>
    [HttpGet("by-slug/{slug}")]
    public async Task<ActionResult<TenantDto>> GetBySlug(string slug, CancellationToken ct)
    {
        if (!string.Equals(slug, _currentTenant.Slug, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        var tenant = await _tenantService.GetBySlugAsync(slug, ct);
        return Ok(tenant);
    }
}
