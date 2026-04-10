namespace Nexo.Application.Features.Suppliers;

// ── Requests ────────────────────────────────────────────────────────────────

public record CreateSupplierRequest(
    string PersonType,          // "Individual" | "Company"
    string Name,
    string DocumentType,        // "CPF" | "CNPJ" | "Other"
    string DocumentNumber,
    string? TradeName = null,
    string? Email = null,
    string? Phone = null,
    string? ContactName = null,
    string? AddressJson = null,
    int? PaymentTermsDays = null,
    string? BankInfoJson = null,
    string? Notes = null);

public record UpdateSupplierRequest(
    string Name,
    string? TradeName,
    string? Email,
    string? Phone,
    string? ContactName,
    string? AddressJson,
    int? PaymentTermsDays,
    string? BankInfoJson,
    string? Notes);

// ── Responses ───────────────────────────────────────────────────────────────

public record SupplierDto(
    Guid Id,
    string PersonType,
    string Name,
    string? TradeName,
    string DocumentType,
    string DocumentNumber,
    string? Email,
    string? Phone,
    string? ContactName,
    string? AddressJson,
    int? PaymentTermsDays,
    string? BankInfoJson,
    string? Notes,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);
