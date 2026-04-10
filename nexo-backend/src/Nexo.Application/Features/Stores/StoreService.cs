using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Stores;

/// <summary>
/// Tenant service (formerly StoreService). Exposes read-only tenant info.
/// Mutating tenant data (billing, modules) will live in a separate admin service.
/// </summary>
public class TenantService
{
    private readonly ITenantRepository _tenants;

    public TenantService(ITenantRepository tenants) => _tenants = tenants;

    public async Task<IReadOnlyList<TenantDto>> GetAllAsync(CancellationToken ct = default)
    {
        var tenants = await _tenants.GetAllAsync(ct);
        return tenants.Select(MapToDto).ToList();
    }

    public async Task<TenantDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var tenant = await _tenants.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Tenant", id);
        return MapToDto(tenant);
    }

    public async Task<TenantDto> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var tenant = await _tenants.GetBySlugAsync(slug, ct)
            ?? throw new NotFoundException("Tenant", slug);
        return MapToDto(tenant);
    }

    private static TenantDto MapToDto(Domain.Entities.Tenant t) => new(
        Id:           t.Id.ToString(),
        Slug:         t.Slug,
        CompanyName:  t.CompanyName,
        TradeName:    t.TradeName,
        TaxId:        t.TaxId,
        Email:        t.Email,
        Phone:        t.Phone,
        BusinessType: t.BusinessType,
        Status:       t.Status.ToString().ToLowerInvariant(),
        TrialEndsAt:  t.TrialEndsAt,
        CreatedAt:    t.CreatedAt,
        UpdatedAt:    t.UpdatedAt);
}
