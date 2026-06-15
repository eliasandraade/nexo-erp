namespace Nexo.Application.Integrations.Contracts;

using Nexo.Application.Integrations.DTOs;

public interface ICepLookupProvider
{
    Task<CepLookupResult?> LookupAsync(string cep, CancellationToken ct = default);
}
