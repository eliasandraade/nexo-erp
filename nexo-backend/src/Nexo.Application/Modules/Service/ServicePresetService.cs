using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Resolves the active Service preset (labels + capability flags) for the current tenant from
/// its active module keys (decisions D1/D2; Q5 deterministic fallback via the registry).
///
/// PR0 is read-only: a per-store SvcSettings override is out of scope and arrives with the
/// engine — see docs/superpowers/specs/2026-06-17-orken-service-v1.md.
/// </summary>
public sealed class ServicePresetService
{
    private readonly ICurrentTenant _currentTenant;
    private readonly ITenantRepository _tenants;

    public ServicePresetService(ICurrentTenant currentTenant, ITenantRepository tenants)
    {
        _currentTenant = currentTenant;
        _tenants = tenants;
    }

    /// <summary>
    /// Returns the resolved preset, or null when the tenant has no active service-family
    /// module (the family gate normally prevents this from being reached).
    /// </summary>
    public async Task<ServicePresetDto?> GetActivePresetAsync(CancellationToken ct = default)
    {
        if (!_currentTenant.IsResolved) return null;

        var activeKeys = await _tenants.GetActiveModuleKeysAsync(_currentTenant.Id, ct);
        var preset = ServicePresetRegistry.Resolve(activeKeys);
        return preset is null ? null : Map(preset);
    }

    private static ServicePresetDto Map(ServicePreset p) => new(
        p.Key,
        p.DisplayName,
        new ServiceLabelsDto(
            p.Labels.Customer, p.Labels.Professional, p.Labels.CatalogItem,
            p.Labels.Appointment, p.Labels.Order, p.Labels.Subject),
        new ServiceCapabilitiesDto(
            p.Capabilities.Appointments, p.Capabilities.Orders, p.Capabilities.Quotes,
            p.Capabilities.Parts, p.Capabilities.Packages, p.Capabilities.SimpleRecord,
            p.Capabilities.Commissions, p.Capabilities.Recurrence,
            p.Capabilities.SubjectKind?.ToString()));
}
