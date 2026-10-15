using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Modules.Service;

namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>Bloqueios de agenda (folga, almoço, feriado) — no new booking lands in them.</summary>
[ApiController]
[Route("api/v1/service/time-blocks")]
[Authorize(Roles = ServiceRoles.Management)]
[RequireServiceModule]
public class TimeBlocksController : ControllerBase
{
    private readonly SvcTimeBlockService _service;
    public TimeBlocksController(SvcTimeBlockService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SvcTimeBlockDto>>> GetRange(
        [FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] Guid? professionalId, CancellationToken ct)
        => Ok(await _service.GetRangeAsync(from, to, professionalId, ct));

    [HttpPost]
    public async Task<ActionResult<SvcTimeBlockDto>> Create([FromBody] CreateSvcTimeBlockRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _service.CreateAsync(request, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }
}
