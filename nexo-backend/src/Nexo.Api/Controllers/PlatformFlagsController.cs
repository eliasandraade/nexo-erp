using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Api.Controllers;

/// <summary>
/// Platform admin endpoints for managing feature flags.
/// Global flag management + per-tenant overrides.
/// </summary>
[ApiController]
[Route("api/platform")]
[Authorize]
public class PlatformFlagsController : ControllerBase
{
    private readonly NexoDbContext _db;
    private readonly ICacheService _cache;

    public PlatformFlagsController(NexoDbContext db, ICacheService cache)
    {
        _db    = db;
        _cache = cache;
    }

    private bool IsPlatformUser() =>
        User.FindFirstValue("type") == "platform";

    // ─────────────────────────────────────────────────────────────────────────
    // GLOBAL FLAGS — list, create, update default
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lists all feature flags with their global defaults and the count of tenant overrides.
    /// Optionally resolves the flag value for a specific tenant via ?tenantId=.
    /// </summary>
    [HttpGet("flags")]
    public async Task<IActionResult> GetFlags(
        [FromQuery] Guid? tenantId,
        CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var flags = await _db.FeatureFlags
            .OrderBy(f => f.Category)
            .ThenBy(f => f.Key)
            .ToListAsync(ct);

        // Count overrides per flag
        var overrideCounts = await _db.TenantFeatureOverrides
            .IgnoreQueryFilters()
            .GroupBy(o => o.FlagKey)
            .Select(g => new { FlagKey = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // If tenantId provided, load that tenant's overrides
        Dictionary<string, bool>? tenantOverrides = null;
        if (tenantId.HasValue)
        {
            tenantOverrides = await _db.TenantFeatureOverrides
                .IgnoreQueryFilters()
                .Where(o => o.TenantId == tenantId.Value)
                .ToDictionaryAsync(o => o.FlagKey, o => o.IsEnabled, ct);
        }

        var result = flags.Select(f =>
        {
            var overrideCount = overrideCounts.FirstOrDefault(x => x.FlagKey == f.Key)?.Count ?? 0;
            bool? tenantValue = tenantOverrides is not null
                ? tenantOverrides.TryGetValue(f.Key, out var ov) ? ov : null
                : null;

            return new
            {
                key            = f.Key,
                name           = f.Name,
                description    = f.Description,
                defaultEnabled = f.DefaultEnabled,
                category       = f.Category,
                overrideCount,
                // Resolved value for the requested tenant (null if no tenantId requested)
                tenantValue,
                // Whether the tenant has an explicit override
                hasOverride    = tenantOverrides is not null && tenantOverrides.ContainsKey(f.Key),
                updatedAt      = f.UpdatedAt,
            };
        });

        return Ok(result);
    }

    public record CreateFlagRequest(
        string Key,
        string Name,
        string? Description,
        bool DefaultEnabled,
        string Category = "geral");

    [HttpPost("flags")]
    public async Task<IActionResult> CreateFlag([FromBody] CreateFlagRequest req, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var exists = await _db.FeatureFlags.AnyAsync(f => f.Key == req.Key.ToLowerInvariant(), ct);
        if (exists) return Conflict(new { error = "Já existe uma flag com essa chave." });

        var flag = FeatureFlag.Create(req.Key, req.Name, req.Description, req.DefaultEnabled, req.Category);
        _db.FeatureFlags.Add(flag);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetFlags), new { }, new { key = flag.Key });
    }

    public record UpdateFlagRequest(
        string Name,
        string? Description,
        bool DefaultEnabled,
        string Category);

    /// <summary>Updates global flag settings. Changing DefaultEnabled affects all tenants without an override.</summary>
    [HttpPut("flags/{key}")]
    public async Task<IActionResult> UpdateFlag(string key, [FromBody] UpdateFlagRequest req, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var flag = await _db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == key.ToLowerInvariant(), ct);
        if (flag is null) return NotFound();

        flag.Update(req.Name, req.Description, req.DefaultEnabled, req.Category);
        await _db.SaveChangesAsync(ct);

        // Invalidate flag cache for all tenants that DON'T have an override
        // (tenants with overrides are unaffected by global default change)
        // For simplicity: broad invalidation — caches will rebuild on next request
        await InvalidateAllFlagCachesAsync(ct);

        return NoContent();
    }

    /// <summary>Toggles only the DefaultEnabled value. Shortcut for the common toggle action.</summary>
    [HttpPatch("flags/{key}/toggle")]
    public async Task<IActionResult> ToggleFlag(string key, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var flag = await _db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == key.ToLowerInvariant(), ct);
        if (flag is null) return NotFound();

        flag.SetDefault(!flag.DefaultEnabled);
        await _db.SaveChangesAsync(ct);

        await InvalidateAllFlagCachesAsync(ct);

        return Ok(new { key = flag.Key, defaultEnabled = flag.DefaultEnabled });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TENANT OVERRIDES
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Lists all tenant overrides for a specific flag.</summary>
    [HttpGet("flags/{key}/overrides")]
    public async Task<IActionResult> GetFlagOverrides(string key, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var flag = await _db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == key.ToLowerInvariant(), ct);
        if (flag is null) return NotFound();

        var overrides = await _db.TenantFeatureOverrides
            .IgnoreQueryFilters()
            .Where(o => o.FlagKey == key.ToLowerInvariant())
            .Join(_db.Tenants.IgnoreQueryFilters(),
                  o => o.TenantId,
                  t => t.Id,
                  (o, t) => new
                  {
                      tenantId    = t.Id,
                      tenantName  = t.TradeName ?? t.CompanyName,
                      isEnabled   = o.IsEnabled,
                      notes       = o.Notes,
                      updatedAt   = o.UpdatedAt,
                  })
            .OrderBy(x => x.tenantName)
            .ToListAsync(ct);

        return Ok(overrides);
    }

    /// <summary>Returns all flag values resolved for a specific tenant (global default + overrides).</summary>
    [HttpGet("tenants/{tenantId:guid}/flags")]
    public async Task<IActionResult> GetTenantFlags(Guid tenantId, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        var flags = await _db.FeatureFlags
            .OrderBy(f => f.Category).ThenBy(f => f.Key)
            .ToListAsync(ct);

        var overrides = await _db.TenantFeatureOverrides
            .IgnoreQueryFilters()
            .Where(o => o.TenantId == tenantId)
            .ToDictionaryAsync(o => o.FlagKey, ct);

        var result = flags.Select(f =>
        {
            var hasOverride = overrides.TryGetValue(f.Key, out var ov);
            return new
            {
                key            = f.Key,
                name           = f.Name,
                category       = f.Category,
                defaultEnabled = f.DefaultEnabled,
                resolved       = hasOverride ? ov!.IsEnabled : f.DefaultEnabled,
                hasOverride,
                overrideValue  = hasOverride ? (bool?)ov!.IsEnabled : null,
                notes          = hasOverride ? ov!.Notes : null,
            };
        });

        return Ok(result);
    }

    public record SetOverrideRequest(bool IsEnabled, string? Notes);

    /// <summary>
    /// Sets a tenant-specific override for a flag.
    /// Creates or updates the override row. To remove and fall back to global, use DELETE.
    /// </summary>
    [HttpPost("tenants/{tenantId:guid}/flags/{key}")]
    public async Task<IActionResult> SetOverride(
        Guid tenantId, string key,
        [FromBody] SetOverrideRequest req,
        CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var flagKey = key.ToLowerInvariant();

        var flag = await _db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == flagKey, ct);
        if (flag is null) return NotFound(new { error = "Flag não encontrada." });

        var existing = await _db.TenantFeatureOverrides
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.FlagKey == flagKey, ct);

        if (existing is not null)
        {
            existing.SetEnabled(req.IsEnabled, req.Notes);
        }
        else
        {
            var newOverride = TenantFeatureOverride.Create(tenantId, flagKey, req.IsEnabled, req.Notes);
            _db.TenantFeatureOverrides.Add(newOverride);
        }

        await _db.SaveChangesAsync(ct);
        await _cache.RemoveAsync($"features:{tenantId}", ct);

        return NoContent();
    }

    /// <summary>Removes the tenant override, reverting the tenant to the global default.</summary>
    [HttpDelete("tenants/{tenantId:guid}/flags/{key}")]
    public async Task<IActionResult> DeleteOverride(Guid tenantId, string key, CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var flagKey = key.ToLowerInvariant();

        var existing = await _db.TenantFeatureOverrides
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.FlagKey == flagKey, ct);

        if (existing is null) return NotFound();

        _db.TenantFeatureOverrides.Remove(existing);
        await _db.SaveChangesAsync(ct);
        await _cache.RemoveAsync($"features:{tenantId}", ct);

        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PRIVATE HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Broad cache invalidation for global flag changes.
    /// Fetches all tenants that have NO override for any flag (affected by global default changes)
    /// and removes their cache entry. Safe to call broadly — cache rebuilds in &lt;200ms.
    /// </summary>
    private async Task InvalidateAllFlagCachesAsync(CancellationToken ct)
    {
        var tenantIds = await _db.Tenants
            .IgnoreQueryFilters()
            .Select(t => t.Id)
            .ToListAsync(ct);

        foreach (var id in tenantIds)
            await _cache.RemoveAsync($"features:{id}", ct);
    }
}
