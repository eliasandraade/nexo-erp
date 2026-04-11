namespace Nexo.Application.Features.Stores;

/// <summary>
/// Store (galpão) descriptor returned to authenticated users.
/// Only stores accessible to the current user (from JWT store[] claims) are returned.
/// </summary>
public record StoreDto(
    string Id,
    string Name,
    string Slug,
    string? ModuleKey,
    string Status);

// Kept for compatibility — TenantDto is the canonical type since Store→Tenant rename.
public record TenantDto(
    string Id,
    string Slug,
    string CompanyName,
    string? TradeName,
    string TaxId,
    string Email,
    string? Phone,
    string BusinessType,
    string Status,
    DateTime? TrialEndsAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);
