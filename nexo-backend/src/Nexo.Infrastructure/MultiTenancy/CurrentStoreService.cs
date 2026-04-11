using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Infrastructure.MultiTenancy;

/// <summary>
/// Reads the active store from the current HTTP context JWT claims.
/// Registered as scoped — one instance per request.
///
/// Populated automatically from the storeId JWT claim — no middleware required.
/// The interceptor reads this service to auto-inject StoreId on every INSERT.
/// </summary>
public class CurrentStoreService : ICurrentStore
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentStoreService(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    private ClaimsPrincipal? Principal
        => _httpContextAccessor.HttpContext?.User;

    public Guid Id
    {
        get
        {
            var value = Principal?.FindFirstValue("storeId");
            return Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }
    }

    public bool IsResolved => Id != Guid.Empty;
}
