using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Modules.Service;
using Nexo.Domain.Modules.Service;

namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>
/// Service orders (ORKEN SERVICE — ordem de serviço). Store-scoped aggregate with line items.
/// All endpoints require an active service-family subscription.
/// </summary>
[ApiController]
[Route("api/v1/service/orders")]
[Authorize]
[RequireServiceModule]
public class OrdersController : ControllerBase
{
    private readonly SvcOrderService                          _service;
    private readonly IValidator<CreateSvcOrderRequest>        _createValidator;
    private readonly IValidator<UpdateSvcOrderRequest>        _updateValidator;
    private readonly IValidator<ChangeSvcOrderStatusRequest>  _statusValidator;
    private readonly IValidator<AddSvcOrderItemRequest>       _addItemValidator;
    private readonly IValidator<UpdateSvcOrderItemRequest>    _updateItemValidator;

    public OrdersController(
        SvcOrderService service,
        IValidator<CreateSvcOrderRequest> createValidator,
        IValidator<UpdateSvcOrderRequest> updateValidator,
        IValidator<ChangeSvcOrderStatusRequest> statusValidator,
        IValidator<AddSvcOrderItemRequest> addItemValidator,
        IValidator<UpdateSvcOrderItemRequest> updateItemValidator)
    {
        _service = service; _createValidator = createValidator; _updateValidator = updateValidator;
        _statusValidator = statusValidator; _addItemValidator = addItemValidator; _updateItemValidator = updateItemValidator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SvcOrderDto>>> GetAll(
        [FromQuery] SvcOrderStatus? status, [FromQuery] Guid? customerId, [FromQuery] Guid? subjectId,
        [FromQuery] Guid? professionalId, [FromQuery] Guid? appointmentId, CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(status, customerId, subjectId, professionalId, appointmentId, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SvcOrderDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SvcOrderDto>> Create([FromBody] CreateSvcOrderRequest request, CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPost("from-appointment/{appointmentId:guid}")]
    public async Task<ActionResult<SvcOrderDto>> CreateFromAppointment(Guid appointmentId, CancellationToken ct)
    {
        var dto = await _service.CreateFromAppointmentAsync(appointmentId, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SvcOrderDto>> Update(Guid id, [FromBody] UpdateSvcOrderRequest request, CancellationToken ct)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.UpdateAsync(id, request, ct));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<SvcOrderDto>> ChangeStatus(Guid id, [FromBody] ChangeSvcOrderStatusRequest request, CancellationToken ct)
    {
        await _statusValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.ChangeStatusAsync(id, request, ct));
    }

    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<SvcOrderDto>> AddItem(Guid id, [FromBody] AddSvcOrderItemRequest request, CancellationToken ct)
    {
        await _addItemValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.AddItemAsync(id, request, ct));
    }

    [HttpPut("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<SvcOrderDto>> UpdateItem(Guid id, Guid itemId, [FromBody] UpdateSvcOrderItemRequest request, CancellationToken ct)
    {
        await _updateItemValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.UpdateItemAsync(id, itemId, request, ct));
    }

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<SvcOrderDto>> RemoveItem(Guid id, Guid itemId, CancellationToken ct)
        => Ok(await _service.RemoveItemAsync(id, itemId, ct));
}
