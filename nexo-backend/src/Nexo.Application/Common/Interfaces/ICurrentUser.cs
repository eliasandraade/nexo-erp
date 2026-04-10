namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// Abstracts access to the currently authenticated user's identity.
/// Populated from JWT claims by CurrentUserService in Infrastructure.
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }
    Guid TenantId { get; }
    string Name { get; }
    string Role { get; }
    bool IsAuthenticated { get; }
}
