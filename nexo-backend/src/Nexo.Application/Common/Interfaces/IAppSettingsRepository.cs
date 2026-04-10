using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// App settings repository — scoped to current tenant via Global Query Filters.
/// </summary>
public interface IAppSettingsRepository
{
    /// <summary>
    /// Returns settings for the current tenant, or creates default settings if none exist.
    /// </summary>
    Task<AppSettings> GetOrCreateAsync(CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
