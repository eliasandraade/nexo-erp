using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Integrations.Options;
using Nexo.Infrastructure.Integrations.Common;

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

        return services;
    }
}
