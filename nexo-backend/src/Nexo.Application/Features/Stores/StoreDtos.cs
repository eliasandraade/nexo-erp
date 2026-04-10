namespace Nexo.Application.Features.Stores;

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
