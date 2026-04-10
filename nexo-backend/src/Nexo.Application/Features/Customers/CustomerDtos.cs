namespace Nexo.Application.Features.Customers;

// ── Requests ────────────────────────────────────────────────────────────────

public record CreateCustomerRequest(
    string PersonType,          // "Individual" | "Company"
    string Name,
    string DocumentType,        // "CPF" | "CNPJ" | "Other"
    string DocumentNumber,
    string? TradeName = null,
    string? Email = null,
    string? Phone = null,
    string? WhatsApp = null,
    string? AddressJson = null,
    decimal? CreditLimit = null,
    string? Notes = null);

public record UpdateCustomerRequest(
    string Name,
    string? TradeName,
    string? Email,
    string? Phone,
    string? WhatsApp,
    string? AddressJson,
    decimal? CreditLimit,
    string? Notes);

// ── Responses ───────────────────────────────────────────────────────────────

public record CustomerDto(
    Guid Id,
    string PersonType,
    string Name,
    string? TradeName,
    string DocumentType,
    string DocumentNumber,
    string? Email,
    string? Phone,
    string? WhatsApp,
    string? AddressJson,
    decimal? CreditLimit,
    string? Notes,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);
