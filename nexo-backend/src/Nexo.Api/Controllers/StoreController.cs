using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Stores;
using Nexo.Domain.Entities;
using Nexo.Domain.Exceptions;

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
            Id:         s.Id.ToString(),
            Name:       s.Name,
            Slug:       s.Slug,
            PublicSlug: s.PublicSlug,
            ModuleKey:  s.ModuleSubscription?.ModuleKey,
            Status:     s.Status.ToString())).ToList();

        return Ok(result);
    }

    /// <summary>
    /// Verifica se um slug está disponível antes de salvar.
    /// Retorna { available, normalized } — pode ser chamado sem autenticação para slugs públicos.
    /// Passe excludeStoreId para ignorar a própria store (útil ao editar um slug já salvo).
    /// </summary>
    [HttpGet("check-slug")]
    [AllowAnonymous]
    public async Task<IActionResult> CheckSlugAvailability(
        [FromQuery] string slug,
        [FromQuery] Guid? excludeStoreId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return BadRequest(new { error = "Slug é obrigatório." });

        var normalized = Store.NormalizeSlug(slug);

        if (normalized.Length < 3)
            return Ok(new { available = false, normalized, reason = "Muito curto (mínimo 3 caracteres)." });

        if (normalized.Length > 100)
            return Ok(new { available = false, normalized, reason = "Muito longo (máximo 100 caracteres)." });

        var exists = await _stores.PublicSlugExistsAsync(normalized, excludeStoreId: excludeStoreId, ct);
        return Ok(new { available = !exists, normalized });
    }

    /// <summary>
    /// Define o slug público do portal. null desativa o portal para esta store.
    /// O slug é normalizado: lowercase, sem acentos, só letras/números/hífen.
    /// Retorna 409 se o slug já estiver em uso por outra store.
    /// </summary>
    [HttpPatch("{id:guid}/public-slug")]
    public async Task<IActionResult> SetPublicSlug(
        Guid id, [FromBody] SetPublicSlugRequest request, CancellationToken ct)
    {
        var store = await _stores.GetByIdTrackedAsync(id, ct);
        if (store is null) return NotFound();

        string? normalized = request.PublicSlug is null
            ? null
            : Store.NormalizeSlug(request.PublicSlug);

        if (normalized is not null)
        {
            if (normalized.Length < 3)
                return BadRequest(new { error = "Slug muito curto. Mínimo 3 caracteres após normalização." });

            if (normalized.Length > 100)
                return BadRequest(new { error = "Slug muito longo. Máximo 100 caracteres após normalização." });

            if (await _stores.PublicSlugExistsAsync(normalized, excludeStoreId: id, ct))
                return Conflict(new { error = $"O slug '{normalized}' já está em uso por outra loja." });
        }

        store.SetPublicSlug(normalized);
        await _stores.SaveChangesAsync(ct);

        return Ok(new { publicSlug = store.PublicSlug });
    }
}
