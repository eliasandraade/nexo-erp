namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// Abstracts access to the currently authenticated user's identity.
/// Populated from JWT claims by CurrentUserService in Infrastructure.
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }
    Guid TenantId { get; }
    /// <summary>Active store from the storeId JWT claim. Guid.Empty if not resolved.</summary>
    Guid StoreId { get; }
    /// <summary>All store IDs accessible to this user, from store[] JWT claims.</summary>
    IReadOnlyList<Guid> StoreIds { get; }
    string Name { get; }
    string Role { get; }
    bool IsAuthenticated { get; }
}
