using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Restaurante;

namespace Nexo.Api.Controllers.Modules.Restaurante;

[ApiController]
[Route("api/restaurante/areas")]
[Authorize]
[RequireModule("restaurante")]
public class AreasController : ControllerBase
{
    private readonly AreaService _service;

    public AreasController(AreaService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AreaDto>>> GetAll(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(includeInactive, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AreaDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<AreaDto>> Create(
        [FromBody] CreateAreaRequest request, CancellationToken ct)
    {
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AreaDto>> Update(
        Guid id, [FromBody] UpdateAreaRequest request, CancellationToken ct)
        => Ok(await _service.UpdateAsync(id, request, ct));
}
