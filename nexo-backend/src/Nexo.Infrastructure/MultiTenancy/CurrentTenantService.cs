using Nexo.Application.Common.Interfaces;

namespace Nexo.Infrastructure.MultiTenancy;

/// <summary>
/// Scoped implementation of ICurrentTenant.
/// Populated once per request by TenantResolutionMiddleware.
/// Consumed by NexoDbContext Global Query Filters and TenantSaveChangesInterceptor.
/// </summary>
public class CurrentTenantService : ICurrentTenant
{
    private Guid _id;
    private string _slug = string.Empty;
    private IReadOnlyList<string> _activeModules = Array.Empty<string>();
    private bool _isResolved;

    public Guid Id => _id;
    public string Slug => _slug;
    public IReadOnlyList<string> ActiveModules => _activeModules;
    public bool IsResolved => _isResolved;

    public void Set(Guid id, string slug, IReadOnlyList<string> activeModules)
    {
        if (_isResolved)
            throw new InvalidOperationException(
                "ICurrentTenant.Set() was called more than once for the same request. " +
                "This indicates a misconfigured middleware pipeline.");

        _id = id;
        _slug = slug;
        _activeModules = activeModules;
        _isResolved = true;
    }
}
