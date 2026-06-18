using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Modules.Service;
using Nexo.Domain.Modules.Service;

namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>
/// Customer packages (ORKEN SERVICE) — a package assigned to a customer with consumable balances.
/// Consumption grants the operational right of use only; it never records payment nor changes any
/// order total/status. All endpoints require an active service-family subscription.
/// </summary>
[ApiController]
[Route("api/v1/service/customer-packages")]
[Authorize]
[RequireServiceModule]
public class CustomerPackagesController : ControllerBase
{
    private readonly SvcCustomerPackageService                    _service;
    private readonly IValidator<AssignSvcCustomerPackageRequest>  _assignValidator;
    private readonly IValidator<ConsumeSvcPackageRequest>         _consumeValidator;

    public CustomerPackagesController(
        SvcCustomerPackageService service,
        IValidator<AssignSvcCustomerPackageRequest> assignValidator,
        IValidator<ConsumeSvcPackageRequest> consumeValidator)
    {
        _service = service; _assignValidator = assignValidator; _consumeValidator = consumeValidator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SvcCustomerPackageDto>>> GetAll(
        [FromQuery] Guid? customerId, [FromQuery] Guid? subjectId,
        [FromQuery] SvcCustomerPackageStatus? status, [FromQuery] Guid? packageId, CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(customerId, subjectId, status, packageId, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SvcCustomerPackageDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SvcCustomerPackageDto>> Assign([FromBody] AssignSvcCustomerPackageRequest request, CancellationToken ct)
    {
        await _assignValidator.ValidateAndThrowAsync(request, ct);
        var dto = await _service.AssignAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<SvcCustomerPackageDto>> Cancel(Guid id, CancellationToken ct)
        => Ok(await _service.CancelAsync(id, ct));

    [HttpPost("{id:guid}/consume")]
    public async Task<ActionResult<SvcCustomerPackageDto>> Consume(Guid id, [FromBody] ConsumeSvcPackageRequest request, CancellationToken ct)
    {
        await _consumeValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.ConsumeAsync(id, request, ct));
    }

    [HttpGet("{id:guid}/usages")]
    public async Task<ActionResult<IReadOnlyList<SvcPackageUsageDto>>> GetUsages(Guid id, CancellationToken ct)
        => Ok(await _service.GetUsagesAsync(id, ct));
}
