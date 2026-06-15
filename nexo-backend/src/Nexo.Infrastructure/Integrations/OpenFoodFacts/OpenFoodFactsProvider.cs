using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;

namespace Nexo.Infrastructure.Integrations.OpenFoodFacts;

public sealed class OpenFoodFactsProvider : IBarcodeProductLookupProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private const string SourceProvider = "OpenFoodFacts";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(30);

    private readonly HttpClient       _http;
    private readonly ICacheService    _cache;
    private readonly ILogger<OpenFoodFactsProvider> _logger;

    public OpenFoodFactsProvider(
        HttpClient http,
        ICacheService cache,
        ILogger<OpenFoodFactsProvider> logger)
    {
        _http   = http;
        _cache  = cache;
        _logger = logger;
    }

    public async Task<ProductLookupResult?> LookupAsync(string barcode, CancellationToken ct = default)
    {
        var normalizedBarcode = barcode.Trim();
        var cacheKey = $"barcode:{normalizedBarcode}";

        // Cache hit — return immediately
        var cached = await _cache.GetAsync<ProductLookupResult>(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("[OpenFoodFacts] Cache hit for barcode {Barcode}", normalizedBarcode);
            return cached;
        }

        // HTTP call
        using var response = await _http.GetAsync($"/api/v0/product/{normalizedBarcode}.json", ct);
        response.EnsureSuccessStatusCode();

        var offResponse = await JsonSerializer.DeserializeAsync<OffResponse>(
            await response.Content.ReadAsStreamAsync(ct), JsonOptions, ct);

        if (offResponse is null || offResponse.Status == 0)
        {
            _logger.LogDebug("[OpenFoodFacts] Product not found for barcode {Barcode}", normalizedBarcode);
            return null;
        }

        var product = offResponse.Product;
        if (product is null)
            return null;

        var result = MapToResult(normalizedBarcode, product);

        await _cache.SetAsync(cacheKey, result, CacheTtl, ct);

        return result;
    }

    private static ProductLookupResult MapToResult(string barcode, OffProduct product)
    {
        var name     = product.ProductName ?? product.ProductNameEn;
        var brand    = ParseFirstBrand(product.Brands);
        var imageUrl = product.ImageUrl ?? product.ImageFrontUrl;
        var category = ParseFirstCategory(product.CategoriesTags);
        var (quantity, unit) = ParseQuantity(product.Quantity);

        var filledFields = new[] { name, brand, category, quantity }
            .Count(f => f is not null);
        var confidence = filledFields / 4.0;

        return new ProductLookupResult(
            Barcode:        barcode,
            Name:           name ?? string.Empty,
            Brand:          brand,
            ImageUrl:       imageUrl,
            Category:       category,
            Quantity:       quantity,
            Unit:           unit,
            SourceProvider: SourceProvider,
            Confidence:     confidence
        );
    }

    private static string? ParseFirstBrand(string? brands)
    {
        if (string.IsNullOrWhiteSpace(brands))
            return null;

        var idx = brands.IndexOf(',');
        return idx >= 0
            ? brands[..idx].Trim()
            : brands.Trim();
    }

    private static string? ParseFirstCategory(string[]? tags)
    {
        if (tags is null || tags.Length == 0)
            return null;

        var tag = tags[0];
        const string prefix = "en:";
        return tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? tag[prefix.Length..]
            : tag;
    }

    private static (string? Quantity, string? Unit) ParseQuantity(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, null);

        var spaceIdx = raw.IndexOf(' ');
        if (spaceIdx > 0)
            return (raw[..spaceIdx].Trim(), raw[(spaceIdx + 1)..].Trim());

        return (raw.Trim(), null);
    }

    // ── Internal DTOs ─────────────────────────────────────────────────────────
    private sealed class OffResponse
    {
        [JsonPropertyName("status")]  public int         Status  { get; init; }
        [JsonPropertyName("product")] public OffProduct? Product { get; init; }
    }

    private sealed class OffProduct
    {
        [JsonPropertyName("product_name")]    public string?   ProductName    { get; init; }
        [JsonPropertyName("product_name_en")] public string?   ProductNameEn  { get; init; }
        [JsonPropertyName("brands")]          public string?   Brands         { get; init; }
        [JsonPropertyName("image_url")]       public string?   ImageUrl       { get; init; }
        [JsonPropertyName("image_front_url")] public string?   ImageFrontUrl  { get; init; }
        [JsonPropertyName("categories_tags")] public string[]? CategoriesTags { get; init; }
        [JsonPropertyName("quantity")]        public string?   Quantity       { get; init; }
    }
}
