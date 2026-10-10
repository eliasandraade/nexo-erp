using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Features.Audit;
using Nexo.Infrastructure.Audit;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/audit")]
// The audit trail holds IPs, e-mails and impersonation records — same audience as the screen.
[Authorize(Roles = "Diretoria")]
public class AuditController : ControllerBase
{
    private readonly AuditQueryService _service;

    public AuditController(AuditQueryService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AuditRecordDto>>> GetAll(
        [FromQuery] string? actionType,
        [FromQuery] string? severity,
        [FromQuery] string? actor,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var records = await _service.GetAsync(actionType, severity, actor, from, to, ct);
        return Ok(records);
    }

    [HttpGet("stats")]
    public async Task<ActionResult<AuditStatsDto>> GetStats(CancellationToken ct)
    {
        return Ok(await _service.GetStatsAsync(ct));
    }
}
