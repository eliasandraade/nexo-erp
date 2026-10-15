using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Common;
using Nexo.Application.Modules.Service;

namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>Financeiro do Service: recebido, por profissional, comissões, pacotes e agenda no período.</summary>
[ApiController]
[Route("api/v1/service/finance")]
[Authorize(Roles = ServiceRoles.Management)]
[RequireServiceModule]
public class FinanceController : ControllerBase
{
    private readonly SvcFinanceService _service;
    public FinanceController(SvcFinanceService service) => _service = service;

    /// <summary>Period in Brazil days, both inclusive; default = this month up to today.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<SvcFinanceSummaryDto>> GetSummary(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(BusinessClock.ToBrazil(DateTime.UtcNow));
        var end = to ?? today;
        var start = from ?? new DateOnly(end.Year, end.Month, 1);
        return Ok(await _service.GetSummaryAsync(start, end, ct));
    }
}
