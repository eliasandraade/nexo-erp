using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Restaurante;
using Nexo.Api.Authorization;

namespace Nexo.Api.Controllers.Modules.Restaurante;

[ApiController]
[Route("api/restaurante/modifier-groups")]
[Authorize]
[RequireModule("restaurante")]
public class ModifierGroupsController : ControllerBase
{
    private readonly ModifierGroupService _service;

    public ModifierGroupsController(ModifierGroupService service) => _service = service;

    [HttpGet]
    [Authorize(Roles = AppRoles.RestaurantOps)]
    public async Task<ActionResult<IReadOnlyList<ModifierGroupDto>>> GetByProduct(
        [FromQuery] Guid productId, CancellationToken ct = default)
        => Ok(await _service.GetByProductAsync(productId, ct));

    [HttpPost]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<ActionResult<ModifierGroupDto>> Create(
        [FromBody] CreateModifierGroupRequest request, CancellationToken ct)
        => Ok(await _service.CreateGroupAsync(request, ct));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<ActionResult<ModifierGroupDto>> Update(
        Guid id, [FromBody] UpdateModifierGroupRequest request, CancellationToken ct)
        => Ok(await _service.UpdateGroupAsync(id, request, ct));

    [HttpPost("{id:guid}/modifiers")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<ActionResult<ModifierGroupDto>> AddModifier(
        Guid id, [FromBody] CreateModifierRequest request, CancellationToken ct)
        => Ok(await _service.AddModifierAsync(id, request, ct));

    [HttpPut("{id:guid}/modifiers/{modId:guid}")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<ActionResult<ModifierDto>> UpdateModifier(
        Guid id, Guid modId, [FromBody] UpdateModifierRequest request, CancellationToken ct)
        => Ok(await _service.UpdateModifierAsync(modId, request, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<IActionResult> DeactivateGroup(Guid id, CancellationToken ct)
    {
        await _service.DeactivateGroupAsync(id, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/modifiers/{modId:guid}")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<IActionResult> DeleteModifier(Guid id, Guid modId, CancellationToken ct)
    {
        await _service.DeleteModifierAsync(modId, ct);
        return NoContent();
    }
}
