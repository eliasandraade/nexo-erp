using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Common;
using Nexo.Application.Features.Suppliers;
using Nexo.Api.Authorization;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/suppliers")]
[Authorize]
public class SuppliersController : ControllerBase
{
    private readonly SupplierService _service;

    public SuppliersController(SupplierService service) => _service = service;

    [HttpGet]
    [Authorize(Roles = AppRoles.Stock)]
    public async Task<ActionResult<IReadOnlyList<SupplierDto>>> GetAll(
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(includeInactive, ct));

    [HttpGet("paged")]
    [Authorize(Roles = AppRoles.Stock)]
    public async Task<ActionResult<PagedResult<SupplierDto>>> GetPaged(
        [FromQuery] int page             = 1,
        [FromQuery] int pageSize         = 25,
        [FromQuery] string? search       = null,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        return Ok(await _service.GetPagedAsync(page, pageSize, search, includeInactive, ct));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = AppRoles.Stock)]
    public async Task<ActionResult<SupplierDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<ActionResult<SupplierDto>> Create(
        [FromBody] CreateSupplierRequest request,
        CancellationToken ct)
    {
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<ActionResult<SupplierDto>> Update(
        Guid id,
        [FromBody] UpdateSupplierRequest request,
        CancellationToken ct)
        => Ok(await _service.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/activate")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        await _service.ActivateAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _service.DeactivateAsync(id, ct);
        return NoContent();
    }
}
