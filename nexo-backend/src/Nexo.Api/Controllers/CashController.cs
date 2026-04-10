using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Features.Cash;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/cash")]
[Authorize]
public class CashController : ControllerBase
{
    private readonly CashService _service;

    public CashController(CashService service) => _service = service;

    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<CashSessionDto>>> GetAllSessions(CancellationToken ct)
        => Ok(await _service.GetAllAsync(ct));

    [HttpGet("sessions/open")]
    public async Task<ActionResult<CashSessionDto?>> GetOpenSession(CancellationToken ct)
        => Ok(await _service.GetOpenSessionAsync(ct));

    [HttpGet("sessions/{id:guid}")]
    public async Task<ActionResult<CashSessionDto>> GetSessionById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost("sessions/open")]
    public async Task<ActionResult<CashSessionDto>> Open(
        [FromBody] OpenCashSessionRequest request,
        CancellationToken ct)
    {
        var dto = await _service.OpenAsync(request, ct);
        return CreatedAtAction(nameof(GetSessionById), new { id = dto.Id }, dto);
    }

    [HttpPost("sessions/{id:guid}/close")]
    public async Task<ActionResult<CashSessionDto>> Close(
        Guid id,
        [FromBody] CloseCashSessionRequest request,
        CancellationToken ct)
        => Ok(await _service.CloseAsync(id, request, ct));

    [HttpPost("sessions/{id:guid}/movements")]
    public async Task<ActionResult<CashMovementDto>> AddMovement(
        Guid id,
        [FromBody] AddCashMovementRequest request,
        CancellationToken ct)
        => Ok(await _service.AddMovementAsync(id, request, ct));
}
