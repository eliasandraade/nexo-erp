namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// Provides the current tenant context for the active HTTP request.
/// Populated by TenantResolutionMiddleware from JWT claims.
/// Injected as IScoped — one instance per request.
///
/// Used by:
///   - NexoDbContext Global Query Filters (tenant isolation)
///   - TenantSaveChangesInterceptor (prevents cross-tenant writes)
///   - ModuleAccessMiddleware (subscription verification)
///   - Application services (audit, business logic)
/// </summary>
public interface ICurrentTenant
{
    /// <summary>Current tenant's primary key. Guid.Empty if not yet resolved.</summary>
    Guid Id { get; }

    /// <summary>Human-readable slug for logging/debugging.</summary>
    string Slug { get; }

    /// <summary>
    /// List of module keys with an active subscription.
    /// Example: ["varejo", "restaurante"]
    /// Populated from Redis cache (5min TTL) or DB fallback.
    /// </summary>
    IReadOnlyList<string> ActiveModules { get; }

    /// <summary>
    /// Module keys the tenant had and has no access to now (ModuleSubscription.ExpiredKeys): they read and take no writes.
    /// Comes from the same cache entry as <see cref="ActiveModules"/>.
    /// </summary>
    IReadOnlyList<string> ExpiredModules { get; }

    /// <summary>True if the tenant context has been resolved for this request.</summary>
    bool IsResolved { get; }

    /// <summary>Resolved, with no module with access now: the whole company is read-only.</summary>
    bool IsReadOnly { get; }

    /// <summary>Called by TenantResolutionMiddleware after JWT validation.</summary>
    void Set(Guid id, string slug, IReadOnlyList<string> activeModules, IReadOnlyList<string>? expiredModules = null);
}
