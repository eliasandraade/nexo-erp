using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Infrastructure.Auth;

/// <summary>
/// Reads the authenticated user's identity from the current HTTP context JWT claims.
/// Registered as scoped — safe to inject into application services.
/// </summary>
public class CurrentUserService : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    private ClaimsPrincipal? Principal
        => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated
        => Principal?.Identity?.IsAuthenticated == true;

    public Guid UserId
    {
        get
        {
            var value = Principal?.FindFirstValue("userId")
                     ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }
    }

    public Guid TenantId
    {
        get
        {
            var value = Principal?.FindFirstValue("tenantId");
            return Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }
    }

    public string Name
        => Principal?.FindFirstValue("name") ?? "Unknown";

    public string Role
        => Principal?.FindFirstValue("role")
        ?? Principal?.FindFirstValue(ClaimTypes.Role)
        ?? string.Empty;
}
