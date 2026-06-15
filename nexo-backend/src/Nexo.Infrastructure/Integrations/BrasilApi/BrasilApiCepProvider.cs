using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;

namespace Nexo.Infrastructure.Integrations.BrasilApi;

public sealed class BrasilApiCepProvider : ICepLookupProvider
{
    private static readonly HttpRequestOptionsKey<string> ProviderNameKey = new("ProviderName");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _http;
    private readonly ILogger<BrasilApiCepProvider> _logger;

    public BrasilApiCepProvider(HttpClient http, ILogger<BrasilApiCepProvider> logger)
    {
        _http   = http;
        _logger = logger;
    }

    public async Task<CepLookupResult?> LookupAsync(string cep, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/cep/v2/{cep}");
        request.Options.Set(ProviderNameKey, "BrasilApi:Cep");

        using var response = await _http.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("[BrasilApi:Cep] CEP {Cep} not found", cep);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var dto = await JsonSerializer.DeserializeAsync<BrasilApiCepResponse>(
            await response.Content.ReadAsStreamAsync(ct), JsonOptions, ct);

        if (dto is null)
        {
            _logger.LogWarning("[BrasilApi:Cep] Null response body for CEP {Cep}", cep);
            return null;
        }

        return new CepLookupResult(
            Cep:          dto.Cep ?? cep,
            Street:       dto.Street ?? string.Empty,
            Neighborhood: dto.Neighborhood ?? string.Empty,
            City:         dto.City ?? string.Empty,
            State:        dto.State ?? string.Empty,
            IbgeCode:     null,
            Provider:     "BrasilApi"
        );
    }

    // ── Internal DTO ──────────────────────────────────────────────────────────
    private sealed class BrasilApiCepResponse
    {
        [JsonPropertyName("cep")]          public string? Cep          { get; init; }
        [JsonPropertyName("state")]        public string? State        { get; init; }
        [JsonPropertyName("city")]         public string? City         { get; init; }
        [JsonPropertyName("neighborhood")] public string? Neighborhood { get; init; }
        [JsonPropertyName("street")]       public string? Street       { get; init; }
    }
}
