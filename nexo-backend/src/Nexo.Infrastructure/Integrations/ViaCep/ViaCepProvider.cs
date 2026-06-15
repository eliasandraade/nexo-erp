using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;

namespace Nexo.Infrastructure.Integrations.ViaCep;

public sealed class ViaCepProvider : ICepLookupProvider
{
    private static readonly HttpRequestOptionsKey<string> ProviderNameKey = new("ProviderName");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _http;
    private readonly ILogger<ViaCepProvider> _logger;

    public ViaCepProvider(HttpClient http, ILogger<ViaCepProvider> logger)
    {
        _http   = http;
        _logger = logger;
    }

    public async Task<CepLookupResult?> LookupAsync(string cep, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/{cep}/json/");
        request.Options.Set(ProviderNameKey, "ViaCep:Cep");

        using var response = await _http.SendAsync(request, ct);

        // ViaCEP returns 404 for invalid format
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("[ViaCep:Cep] CEP {Cep} not found (404)", cep);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var dto = await JsonSerializer.DeserializeAsync<ViaCepResponse>(
            await response.Content.ReadAsStreamAsync(ct), JsonOptions, ct);

        // ViaCEP returns 200 with {"erro": true} when CEP doesn't exist
        if (dto is null || dto.Erro == true)
        {
            _logger.LogDebug("[ViaCep:Cep] CEP {Cep} not found (erro=true)", cep);
            return null;
        }

        return new CepLookupResult(
            Cep:          dto.Cep ?? cep,
            Street:       dto.Logradouro ?? string.Empty,
            Neighborhood: dto.Bairro ?? string.Empty,
            City:         dto.Localidade ?? string.Empty,
            State:        dto.Uf ?? string.Empty,
            IbgeCode:     string.IsNullOrWhiteSpace(dto.Ibge) ? null : dto.Ibge,
            Provider:     "ViaCep"
        );
    }

    // ── Internal DTO ──────────────────────────────────────────────────────────
    private sealed class ViaCepResponse
    {
        [JsonPropertyName("cep")]        public string? Cep        { get; init; }
        [JsonPropertyName("logradouro")] public string? Logradouro { get; init; }
        [JsonPropertyName("bairro")]     public string? Bairro     { get; init; }
        [JsonPropertyName("localidade")] public string? Localidade { get; init; }
        [JsonPropertyName("uf")]         public string? Uf         { get; init; }
        [JsonPropertyName("ibge")]       public string? Ibge       { get; init; }
        [JsonPropertyName("erro")]       public bool?   Erro       { get; init; }
    }
}
