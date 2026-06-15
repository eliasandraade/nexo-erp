namespace Nexo.Application.Integrations.DTOs;

public sealed record CnpjLookupResult(
    string Cnpj,
    string CompanyName,
    string? TradeName,
    string? Status,
    string? ActivityCode,
    string? ActivityDescription,
    CepLookupResult? Address,
    string Provider
);
