using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Stores;

namespace Nexo.Api.Controllers;

/// <summary>
/// Exposes the stores accessible to the authenticated user.
/// The list is derived from the store[] JWT claims set at login / switch-store.
/// </summary>
[ApiController]
[Route("api/stores")]
[Authorize]
public class StoreController : ControllerBase
{
    private readonly IStoreRepository _stores;
    private readonly ICurrentUser     _currentUser;

    public StoreController(IStoreRepository stores, ICurrentUser currentUser)
    {
        _stores      = stores;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Returns the stores accessible to the current user.
    /// Only active stores that appear in the JWT store[] claims are returned.
    /// The active storeId (from JWT storeId claim) is indicated by matching it against StoreId in the session.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StoreDto>>> GetMyStores(CancellationToken ct)
    {
        var storeIds = _currentUser.StoreIds;

        if (storeIds.Count == 0)
            return Ok(Array.Empty<StoreDto>());

        var stores = await _stores.GetByIdsAsync(_currentUser.TenantId, storeIds, ct);

        var result = stores.Select(s => new StoreDto(
            Id:        s.Id.ToString(),
            Name:      s.Name,
            Slug:      s.Slug,
            ModuleKey: s.ModuleSubscription?.ModuleKey,
            Status:    s.Status.ToString())).ToList();

        return Ok(result);
    }
}
