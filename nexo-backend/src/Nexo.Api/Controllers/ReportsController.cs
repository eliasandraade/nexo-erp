using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Nexo.Application.Features.Reports;
using Nexo.Infrastructure.Reports;
using Nexo.Api.Authorization;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = AppRoles.Management)]
public class ReportsController : ControllerBase
{
    private readonly ReportsService _service;

    public ReportsController(ReportsService service) => _service = service;

    /// <summary>
    /// Sales of a period: totals, day by day, best products, sellers and payment methods.
    /// Query: from=yyyy-MM-dd&amp;to=yyyy-MM-dd (Brazil days; defaults to the current month to date).
    /// </summary>
    [HttpGet("sales")]
    public async Task<ActionResult<SalesReportDto>> GetSales(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var window = SalesWindow.TryParse(from, to, out var error);
        if (window is null) return BadRequest(new { error });
        return Ok(await _service.GetSalesReportAsync(window, ct));
    }

    /// <summary>The sales of a period as a spreadsheet (CSV that Excel opens in Portuguese).</summary>
    [HttpGet("sales/export")]
    public async Task<IActionResult> ExportSales(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var window = SalesWindow.TryParse(from, to, out var error);
        if (window is null) return BadRequest(new { error });

        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileName = $"vendas_{window.From:yyyy-MM-dd}_a_{window.To:yyyy-MM-dd}.csv",
        }.ToString();
        await _service.ExportSalesAsync(window, Response.Body, ct);
        return new EmptyResult();
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
