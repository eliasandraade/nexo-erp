namespace Nexo.Application.Integrations.DTOs;

public sealed record CepLookupResult(
    string Cep,
    string Street,
    string Neighborhood,
    string City,
    string State,
    string? IbgeCode,
    string Provider
);
