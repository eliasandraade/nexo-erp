using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Timeout;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.Options;
using Nexo.Application.Integrations.Pdf;
using Nexo.Infrastructure.Integrations.BrasilApi;
using Nexo.Infrastructure.Integrations.Common;
using Nexo.Infrastructure.Integrations.Composite;
using Nexo.Infrastructure.Integrations.OpenFoodFacts;
using Nexo.Infrastructure.Integrations.Pdf;
using Nexo.Infrastructure.Integrations.Storage;
using Nexo.Infrastructure.Integrations.ViaCep;

namespace Nexo.Infrastructure.Integrations;

public static class IntegrationsDependencyInjection
{
    public static IServiceCollection AddIntegrations(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register options
        services.Configure<IntegrationHttpOptions>(
            configuration.GetSection(IntegrationHttpOptions.SectionKey));
        services.Configure<IntegrationResilienceOptions>(
            configuration.GetSection(IntegrationResilienceOptions.SectionKey));

        // Feature flags — read once at startup
        services.AddSingleton<IIntegrationFeatureFlags>(
            new IntegrationFeatureFlags(configuration));

        // Logging/correlation handler — must be transient per ASP.NET Core DelegatingHandler rules
        services.AddTransient<IntegrationHttpClientHandler>();

        // ── CEP / CNPJ Lookup ─────────────────────────────────────────────────────────
        var brasilApiOpts   = configuration.GetSection(BrasilApiOptions.SectionKey).Get<BrasilApiOptions>() ?? new BrasilApiOptions();
        var viaCepOpts      = configuration.GetSection(ViaCepOptions.SectionKey).Get<ViaCepOptions>() ?? new ViaCepOptions();
        var resilienceOpts  = configuration.GetSection(IntegrationResilienceOptions.SectionKey).Get<IntegrationResilienceOptions>() ?? new IntegrationResilienceOptions();

        // BrasilApiCepProvider — typed client
        services.AddHttpClient<BrasilApiCepProvider>(client =>
        {
            client.BaseAddress = new Uri(brasilApiOpts.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(brasilApiOpts.TimeoutSeconds + 30);
        })
        .AddHttpMessageHandler<IntegrationHttpClientHandler>()
        .AddResilienceHandler("BrasilApi:Cep", pipeline =>
        {
            pipeline.AddTimeout(TimeSpan.FromSeconds(brasilApiOpts.TimeoutSeconds));
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = resilienceOpts.MaxRetryAttempts,
                Delay = TimeSpan.FromSeconds(resilienceOpts.RetryBaseDelaySeconds),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(r => r.StatusCode is
                        HttpStatusCode.TooManyRequests or
                        HttpStatusCode.ServiceUnavailable or
                        HttpStatusCode.GatewayTimeout)
            });
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                SamplingDuration  = TimeSpan.FromSeconds(resilienceOpts.CircuitBreakerSamplingDurationSeconds),
                BreakDuration     = TimeSpan.FromSeconds(resilienceOpts.CircuitBreakerBreakDurationSeconds),
                FailureRatio      = 0.5,
                MinimumThroughput = resilienceOpts.CircuitBreakerFailureThreshold,
            });
        });

        // ViaCepProvider — typed client (fallback)
        services.AddHttpClient<ViaCepProvider>(client =>
        {
            client.BaseAddress = new Uri(viaCepOpts.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(viaCepOpts.TimeoutSeconds + 30);
        })
        .AddHttpMessageHandler<IntegrationHttpClientHandler>()
        .AddResilienceHandler("ViaCep:Cep", pipeline =>
        {
            pipeline.AddTimeout(TimeSpan.FromSeconds(viaCepOpts.TimeoutSeconds));
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromSeconds(0.5),
                BackoffType = DelayBackoffType.Linear,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(r => r.StatusCode is
                        HttpStatusCode.ServiceUnavailable or
                        HttpStatusCode.GatewayTimeout)
            });
        });

        // BrasilApiCnpjProvider — typed client
        services.AddHttpClient<BrasilApiCnpjProvider>(client =>
        {
            client.BaseAddress = new Uri(brasilApiOpts.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(brasilApiOpts.TimeoutSeconds + 30);
        })
        .AddHttpMessageHandler<IntegrationHttpClientHandler>()
        .AddResilienceHandler("BrasilApi:Cnpj", pipeline =>
        {
            pipeline.AddTimeout(TimeSpan.FromSeconds(brasilApiOpts.TimeoutSeconds));
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = resilienceOpts.MaxRetryAttempts,
                Delay = TimeSpan.FromSeconds(resilienceOpts.RetryBaseDelaySeconds),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(r => r.StatusCode is
                        HttpStatusCode.TooManyRequests or
                        HttpStatusCode.ServiceUnavailable or
                        HttpStatusCode.GatewayTimeout)
            });
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                SamplingDuration  = TimeSpan.FromSeconds(resilienceOpts.CircuitBreakerSamplingDurationSeconds),
                BreakDuration     = TimeSpan.FromSeconds(resilienceOpts.CircuitBreakerBreakDurationSeconds),
                FailureRatio      = 0.5,
                MinimumThroughput = resilienceOpts.CircuitBreakerFailureThreshold,
            });
        });

        // Composite and interface registrations
        services.AddScoped<ICepLookupProvider,  CompositeCepLookupProvider>();
        services.AddScoped<ICnpjLookupProvider, BrasilApiCnpjProvider>();

        // ── Storage ───────────────────────────────────────────────────────────────────
        services.Configure<StorageOptions>(
            configuration.GetSection(StorageOptions.SectionKey));
        services.AddSingleton<IStorageProvider, CloudflareR2Provider>();

        // ── Open Food Facts ───────────────────────────────────────────────────
        var offOpts = configuration.GetSection(OpenFoodFactsOptions.SectionKey).Get<OpenFoodFactsOptions>() ?? new OpenFoodFactsOptions();

        services.Configure<OpenFoodFactsOptions>(configuration.GetSection(OpenFoodFactsOptions.SectionKey));

        services.AddHttpClient<OpenFoodFactsProvider>(client =>
        {
            client.BaseAddress = new Uri(offOpts.BaseUrl);
            client.Timeout     = TimeSpan.FromSeconds(offOpts.TimeoutSeconds + 30);
            client.DefaultRequestHeaders.Add("User-Agent", offOpts.UserAgent);
        })
        .AddHttpMessageHandler<IntegrationHttpClientHandler>()
        .AddResilienceHandler("OpenFoodFacts", pipeline =>
        {
            pipeline.AddTimeout(TimeSpan.FromSeconds(offOpts.TimeoutSeconds));
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay            = TimeSpan.FromSeconds(1),
                BackoffType      = DelayBackoffType.Exponential,
                ShouldHandle     = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(r => r.StatusCode is
                        HttpStatusCode.TooManyRequests or
                        HttpStatusCode.ServiceUnavailable or
                        HttpStatusCode.GatewayTimeout)
            });
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                SamplingDuration  = TimeSpan.FromSeconds(resilienceOpts.CircuitBreakerSamplingDurationSeconds),
                BreakDuration     = TimeSpan.FromSeconds(resilienceOpts.CircuitBreakerBreakDurationSeconds),
                FailureRatio      = 0.5,
                MinimumThroughput = resilienceOpts.CircuitBreakerFailureThreshold,
            });
        });

        services.AddScoped<IBarcodeProductLookupProvider, OpenFoodFactsProvider>();

        // ── PDF Rendering ─────────────────────────────────────────────────────────────
        services.AddSingleton<IPdfRenderer, QuestPdfRenderer>();

        return services;
    }
}
