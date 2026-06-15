namespace Nexo.Application.Integrations.Contracts;

using Nexo.Application.Integrations.DTOs;

public interface ICnpjLookupProvider
{
    Task<CnpjLookupResult?> LookupAsync(string cnpj, CancellationToken ct = default);
}
