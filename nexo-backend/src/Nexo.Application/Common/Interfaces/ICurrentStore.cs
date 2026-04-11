namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// Provides the current store context for the active HTTP request.
/// Reads the storeId JWT claim via IHttpContextAccessor.
/// Injected as IScoped — one instance per request.
///
/// Used by:
///   - NexoDbContext Global Query Filters (store isolation)
///   - TenantSaveChangesInterceptor (auto-inject StoreId on INSERT)
///   - Application services (business logic scoping)
/// </summary>
public interface ICurrentStore
{
    /// <summary>Current store's primary key. Guid.Empty if not yet resolved.</summary>
    Guid Id { get; }

    /// <summary>True if a valid storeId claim was found in the JWT.</summary>
    bool IsResolved { get; }
}
