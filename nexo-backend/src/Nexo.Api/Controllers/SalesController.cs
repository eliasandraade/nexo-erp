using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Common;
using Nexo.Application.Features.Sales;
using Nexo.Application.Integrations.Pdf;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/sales")]
[Authorize]
[RequireModule("varejo")]
public class SalesController : ControllerBase
{
    private readonly SaleService _service;
    private readonly IPdfRenderer _pdfRenderer;

    public SalesController(SaleService service, IPdfRenderer pdfRenderer)
    {
        _service = service;
        _pdfRenderer = pdfRenderer;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SaleDto>>> GetAll(CancellationToken ct)
        => Ok(await _service.GetAllAsync(ct));

    [HttpGet("paged")]
    public async Task<ActionResult<PagedResult<SaleListItemDto>>> GetPaged(
        [FromQuery] int page            = 1,
        [FromQuery] int pageSize        = 25,
        [FromQuery] string? search      = null,
        [FromQuery] string? status      = null,
        [FromQuery] string? paymentMethod = null,
        CancellationToken ct = default)
    {
        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        return Ok(await _service.GetPagedAsync(page, pageSize, search, status, paymentMethod, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SaleDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SaleDto>> Create(
        [FromBody] CreateSaleRequest request,
        CancellationToken ct)
    {
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<SaleDto>> AddItem(
        Guid id,
        [FromBody] AddSaleItemRequest request,
        CancellationToken ct)
        => Ok(await _service.AddItemAsync(id, request, ct));

    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<SaleDto>> Confirm(
        Guid id,
        [FromBody] ConfirmSaleRequest request,
        CancellationToken ct)
        => Ok(await _service.ConfirmAsync(id, request, ct));

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await _service.CancelAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/receipt.pdf")]
    public async Task<IActionResult> GetSaleReceipt(Guid id, CancellationToken ct)
    {
        var sale    = await _service.GetByIdAsync(id, ct);
        var request = new SaleReceiptRequest(sale, "Orken");
        var result  = _pdfRenderer.RenderSaleReceipt(request);
        return File(result.Bytes, "application/pdf", result.FileName);
    }
}
