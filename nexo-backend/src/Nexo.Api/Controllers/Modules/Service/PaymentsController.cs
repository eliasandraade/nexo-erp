using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Modules.Service;
using Nexo.Domain.Modules.Service;

namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>
/// Service payments (ORKEN SERVICE) — manual records of payments received against an order or a
/// customer package. Operational only: no Stripe/checkout/gateway, no global financial/cash entity,
/// and no change to the order total/status or package balance/status. Gated by the service family.
/// </summary>
[ApiController]
[Route("api/v1/service/payments")]
[Authorize]
[RequireServiceModule]
public class PaymentsController : ControllerBase
{
    private readonly SvcPaymentService                    _service;
    private readonly IValidator<CreateSvcPaymentRequest>  _createValidator;
    private readonly IValidator<VoidSvcPaymentRequest>    _voidValidator;

    public PaymentsController(
        SvcPaymentService service,
        IValidator<CreateSvcPaymentRequest> createValidator,
        IValidator<VoidSvcPaymentRequest> voidValidator)
    {
        _service = service; _createValidator = createValidator; _voidValidator = voidValidator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SvcPaymentDto>>> GetAll(
        [FromQuery] Guid? customerId, [FromQuery] Guid? orderId, [FromQuery] Guid? customerPackageId,
        [FromQuery] SvcPaymentMethod? method, [FromQuery] SvcPaymentStatus? status,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(customerId, orderId, customerPackageId, method, status, from, to, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SvcPaymentDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SvcPaymentDto>> Create([FromBody] CreateSvcPaymentRequest request, CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPost("{id:guid}/void")]
    public async Task<ActionResult<SvcPaymentDto>> Void(Guid id, [FromBody] VoidSvcPaymentRequest request, CancellationToken ct)
    {
        await _voidValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.VoidAsync(id, request, ct));
    }

    [HttpGet("order/{orderId:guid}/summary")]
    public async Task<ActionResult<SvcPaymentSummaryDto>> OrderSummary(Guid orderId, CancellationToken ct)
        => Ok(await _service.GetOrderSummaryAsync(orderId, ct));

    [HttpGet("customer-package/{customerPackageId:guid}/summary")]
    public async Task<ActionResult<SvcPaymentSummaryDto>> CustomerPackageSummary(Guid customerPackageId, CancellationToken ct)
        => Ok(await _service.GetCustomerPackageSummaryAsync(customerPackageId, ct));
}
