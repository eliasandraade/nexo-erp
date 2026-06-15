using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;
using Nexo.Infrastructure.Integrations.BrasilApi;
using Nexo.Infrastructure.Integrations.ViaCep;

namespace Nexo.Infrastructure.Integrations.Composite;

public sealed class CompositeCepLookupProvider : ICepLookupProvider
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(30);

    private readonly BrasilApiCepProvider _brasilApi;
    private readonly ViaCepProvider       _viaCep;
    private readonly ICacheService        _cache;
    private readonly ILogger<CompositeCepLookupProvider> _logger;

    public CompositeCepLookupProvider(
        BrasilApiCepProvider brasilApi,
        ViaCepProvider       viaCep,
        ICacheService        cache,
        ILogger<CompositeCepLookupProvider> logger)
    {
        _brasilApi = brasilApi;
        _viaCep    = viaCep;
        _cache     = cache;
        _logger    = logger;
    }

    public async Task<CepLookupResult?> LookupAsync(string cep, CancellationToken ct)
    {
        var cacheKey = $"integration:cep:{cep}";

        // 1. Cache check
        var cached = await _cache.GetAsync<CepLookupResult>(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("[CompositeCep] Cache hit for CEP {Cep}", cep);
            return cached;
        }

        // 2. Try BrasilAPI first
        CepLookupResult? result = null;
        try
        {
            result = await _brasilApi.LookupAsync(cep, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[CompositeCep] BrasilAPI failed for CEP {Cep} — falling through to ViaCEP", cep);
        }

        // 3. Fallback to ViaCEP if BrasilAPI returned null or threw
        if (result is null)
        {
            try
            {
                result = await _viaCep.LookupAsync(cep, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[CompositeCep] ViaCEP also failed for CEP {Cep} — returning null", cep);
                return null;
            }
        }

        // 4. Cache on success
        if (result is not null)
        {
            await _cache.SetAsync(cacheKey, result, CacheTtl, ct);
            _logger.LogDebug("[CompositeCep] Cached CEP {Cep} from {Provider}", cep, result.Provider);
        }

        return result;
    }
}
