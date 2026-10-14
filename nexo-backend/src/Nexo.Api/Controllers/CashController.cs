using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Features.Cash;
using Nexo.Application.Integrations.Pdf;
using Nexo.Api.Authorization;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/cash")]
[Authorize]
public class CashController : ControllerBase
{
    private readonly CashService _service;
    private readonly IPdfRenderer _pdfRenderer;

    public CashController(CashService service, IPdfRenderer pdfRenderer)
    {
        _service = service;
        _pdfRenderer = pdfRenderer;
    }

    [HttpGet("sessions")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<ActionResult<IReadOnlyList<CashSessionDto>>> GetAllSessions(CancellationToken ct)
        => Ok(await _service.GetAllAsync(ct));

    [HttpGet("sessions/open")]
    [Authorize(Roles = AppRoles.Sales)]
    public async Task<ActionResult<CashSessionDto?>> GetOpenSession(CancellationToken ct)
        => Ok(await _service.GetOpenSessionAsync(ct));

    [HttpGet("sessions/{id:guid}")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<ActionResult<CashSessionDto>> GetSessionById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost("sessions/open")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<ActionResult<CashSessionDto>> Open(
        [FromBody] OpenCashSessionRequest request,
        CancellationToken ct)
    {
        var dto = await _service.OpenAsync(request, ct);
        return CreatedAtAction(nameof(GetSessionById), new { id = dto.Id }, dto);
    }

    [HttpPost("sessions/{id:guid}/close")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<ActionResult<CashSessionDto>> Close(
        Guid id,
        [FromBody] CloseCashSessionRequest request,
        CancellationToken ct)
        => Ok(await _service.CloseAsync(id, request, ct));

    [HttpPost("sessions/{id:guid}/movements")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<ActionResult<CashMovementDto>> AddMovement(
        Guid id,
        [FromBody] AddCashMovementRequest request,
        CancellationToken ct)
        => Ok(await _service.AddMovementAsync(id, request, ct));

    [HttpGet("sessions/{id:guid}/close-report.pdf")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<IActionResult> GetCashCloseReport(Guid id, CancellationToken ct)
    {
        var session = await _service.GetByIdAsync(id, ct);
        var request  = new CashCloseReportRequest(session, "Orken");
        var result   = _pdfRenderer.RenderCashCloseReport(request);
        return File(result.Bytes, "application/pdf", result.FileName);
    }
}
