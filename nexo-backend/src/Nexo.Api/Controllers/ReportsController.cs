using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Features.Reports;
using Nexo.Infrastructure.Reports;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly ReportsService _service;

    public ReportsController(ReportsService service) => _service = service;

    /// <summary>Sales summary. Query: from=yyyy-MM-dd&amp;to=yyyy-MM-dd (defaults to current month).</summary>
    [HttpGet("sales")]
    public async Task<ActionResult<SalesReportDto>> GetSales(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var today    = DateOnly.FromDateTime(DateTime.UtcNow);
        var fromDate = from is not null ? DateOnly.Parse(from) : new DateOnly(today.Year, today.Month, 1);
        var toDate   = to   is not null ? DateOnly.Parse(to)   : today;
        return Ok(await _service.GetSalesReportAsync(fromDate, toDate, ct));
    }

    /// <summary>Inventory health snapshot.</summary>
    [HttpGet("inventory")]
    public async Task<ActionResult<InventoryReportDto>> GetInventory(CancellationToken ct) =>
        Ok(await _service.GetInventoryReportAsync(ct));

    /// <summary>Customer activity summary.</summary>
    [HttpGet("customers")]
    public async Task<ActionResult<CustomerReportDto>> GetCustomers(CancellationToken ct) =>
        Ok(await _service.GetCustomerReportAsync(ct));
}
