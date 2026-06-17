using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Modules.Service;
using Nexo.Domain.Modules.Service;

namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>
/// Service agenda (ORKEN SERVICE) — appointments. Store-scoped. All endpoints require an active
/// service-family subscription. Per-professional overlap is rejected with 409; invalid status
/// transitions with 422.
/// </summary>
[ApiController]
[Route("api/v1/service/appointments")]
[Authorize]
[RequireServiceModule]
public class AppointmentsController : ControllerBase
{
    private readonly SvcAppointmentService                         _service;
    private readonly IValidator<CreateSvcAppointmentRequest>       _createValidator;
    private readonly IValidator<UpdateSvcAppointmentRequest>       _updateValidator;
    private readonly IValidator<ChangeSvcAppointmentStatusRequest> _statusValidator;

    public AppointmentsController(
        SvcAppointmentService                         service,
        IValidator<CreateSvcAppointmentRequest>       createValidator,
        IValidator<UpdateSvcAppointmentRequest>       updateValidator,
        IValidator<ChangeSvcAppointmentStatusRequest> statusValidator)
    {
        _service         = service;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _statusValidator = statusValidator;
    }

    /// <summary>Lists appointments, filterable by date range, professional, status, customer, subject.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SvcAppointmentDto>>> GetAll(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] Guid? professionalId,
        [FromQuery] SvcAppointmentStatus? status,
        [FromQuery] Guid? customerId,
        [FromQuery] Guid? subjectId,
        CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(from, to, professionalId, status, customerId, subjectId, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SvcAppointmentDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SvcAppointmentDto>> Create(
        [FromBody] CreateSvcAppointmentRequest request, CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SvcAppointmentDto>> Update(
        Guid id, [FromBody] UpdateSvcAppointmentRequest request, CancellationToken ct)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.UpdateAsync(id, request, ct));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<SvcAppointmentDto>> ChangeStatus(
        Guid id, [FromBody] ChangeSvcAppointmentStatusRequest request, CancellationToken ct)
    {
        await _statusValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.ChangeStatusAsync(id, request, ct));
    }
}
