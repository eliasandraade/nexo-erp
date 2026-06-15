using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Nexo.Infrastructure.Integrations.Common;

public class IntegrationHttpClientHandler : DelegatingHandler
{
    private static readonly HttpRequestOptionsKey<string> ProviderNameKey   = new("ProviderName");
    private static readonly HttpRequestOptionsKey<string> CorrelationIdKey  = new("X-Correlation-Id");

    private readonly ILogger<IntegrationHttpClientHandler> _logger;

    public IntegrationHttpClientHandler(ILogger<IntegrationHttpClientHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Resolve provider name
        request.Options.TryGetValue(ProviderNameKey, out var providerName);
        providerName ??= "Unknown";

        // Resolve or generate correlation ID
        string correlationId;
        if (request.Headers.TryGetValues("X-Correlation-Id", out var existingValues))
        {
            correlationId = existingValues.FirstOrDefault()
                            ?? Guid.NewGuid().ToString("N")[..12];
        }
        else
        {
            correlationId = Guid.NewGuid().ToString("N")[..12];
        }

        // Propagate correlation ID downstream
        request.Headers.Remove("X-Correlation-Id");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);

        _logger.LogInformation(
            "[Integration] {ProviderName} request started — CorrelationId: {CorrelationId}",
            providerName, correlationId);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            sw.Stop();

            _logger.LogInformation(
                "[Integration] {ProviderName} responded {StatusCode} in {ElapsedMs}ms — CorrelationId: {CorrelationId}",
                providerName, (int)response.StatusCode, sw.ElapsedMilliseconds, correlationId);

            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();

            _logger.LogError(
                "[Integration] {ProviderName} failed in {ElapsedMs}ms — CorrelationId: {CorrelationId}, Error: {Message}",
                providerName, sw.ElapsedMilliseconds, correlationId, ex.Message);

            throw;
        }
    }
}
