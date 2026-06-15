using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;

namespace Nexo.Infrastructure.Integrations.BrasilApi;

public sealed class BrasilApiCnpjProvider : ICnpjLookupProvider
{
    private static readonly HttpRequestOptionsKey<string> ProviderNameKey = new("ProviderName");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _http;
    private readonly ILogger<BrasilApiCnpjProvider> _logger;

    public BrasilApiCnpjProvider(HttpClient http, ILogger<BrasilApiCnpjProvider> logger)
    {
        _http   = http;
        _logger = logger;
    }

    public async Task<CnpjLookupResult?> LookupAsync(string cnpj, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/cnpj/v1/{cnpj}");
        request.Options.Set(ProviderNameKey, "BrasilApi:Cnpj");

        using var response = await _http.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("[BrasilApi:Cnpj] CNPJ {Cnpj} not found", cnpj);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var dto = await JsonSerializer.DeserializeAsync<BrasilApiCnpjResponse>(
            await response.Content.ReadAsStreamAsync(ct), JsonOptions, ct);

        if (dto is null)
        {
            _logger.LogWarning("[BrasilApi:Cnpj] Null response body for CNPJ {Cnpj}", cnpj);
            return null;
        }

        CepLookupResult? address = null;
        if (!string.IsNullOrWhiteSpace(dto.Cep))
        {
            address = new CepLookupResult(
                Cep:          dto.Cep,
                Street:       BuildStreet(dto.Logradouro, dto.Numero, dto.Complemento),
                Neighborhood: dto.Bairro ?? string.Empty,
                City:         dto.Municipio ?? string.Empty,
                State:        dto.Uf ?? string.Empty,
                IbgeCode:     null,
                Provider:     "BrasilApi"
            );
        }

        return new CnpjLookupResult(
            Cnpj:                dto.Cnpj ?? cnpj,
            CompanyName:         dto.RazaoSocial ?? string.Empty,
            TradeName:           string.IsNullOrWhiteSpace(dto.NomeFantasia) ? null : dto.NomeFantasia,
            Status:              dto.DescricaoSituacaoCadastral,
            ActivityCode:        dto.CnaeFiscal?.ToString(),
            ActivityDescription: dto.CnaeFiscalDescricao,
            Address:             address,
            Provider:            "BrasilApi"
        );
    }

    private static string BuildStreet(string? logradouro, string? numero, string? complemento)
    {
        var parts = new[] { logradouro, numero, complemento }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        return string.Join(", ", parts);
    }

    // ── Internal DTO ──────────────────────────────────────────────────────────
    private sealed class BrasilApiCnpjResponse
    {
        [JsonPropertyName("cnpj")]                         public string? Cnpj                        { get; init; }
        [JsonPropertyName("razao_social")]                 public string? RazaoSocial                 { get; init; }
        [JsonPropertyName("nome_fantasia")]                public string? NomeFantasia                { get; init; }
        [JsonPropertyName("descricao_situacao_cadastral")] public string? DescricaoSituacaoCadastral  { get; init; }
        [JsonPropertyName("cnae_fiscal")]                  public int?    CnaeFiscal                  { get; init; }
        [JsonPropertyName("cnae_fiscal_descricao")]        public string? CnaeFiscalDescricao         { get; init; }
        [JsonPropertyName("cep")]                          public string? Cep                         { get; init; }
        [JsonPropertyName("logradouro")]                   public string? Logradouro                  { get; init; }
        [JsonPropertyName("numero")]                       public string? Numero                      { get; init; }
        [JsonPropertyName("complemento")]                  public string? Complemento                 { get; init; }
        [JsonPropertyName("bairro")]                       public string? Bairro                      { get; init; }
        [JsonPropertyName("municipio")]                    public string? Municipio                   { get; init; }
        [JsonPropertyName("uf")]                           public string? Uf                          { get; init; }
    }
}
