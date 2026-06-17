using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Modules.Service;
using Nexo.Domain.Modules.Service;

namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>
/// Service subjects (ORKEN SERVICE) — pet/veículo/aluno/dependente. Tenant-scoped (shared
/// across the tenant's stores). All endpoints require an active service-family subscription.
/// </summary>
[ApiController]
[Route("api/v1/service/subjects")]
[Authorize]
[RequireServiceModule]
public class SubjectsController : ControllerBase
{
    private readonly SvcSubjectService                   _service;
    private readonly IValidator<CreateSvcSubjectRequest> _createValidator;
    private readonly IValidator<UpdateSvcSubjectRequest> _updateValidator;

    public SubjectsController(
        SvcSubjectService                   service,
        IValidator<CreateSvcSubjectRequest> createValidator,
        IValidator<UpdateSvcSubjectRequest> updateValidator)
    {
        _service         = service;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>Lists subjects, optionally filtered by customer, kind, and active state.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SvcSubjectDto>>> GetAll(
        [FromQuery] Guid? customerId,
        [FromQuery] SvcSubjectKind? kind,
        [FromQuery] bool? active,
        CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(customerId, kind, active, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SvcSubjectDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SvcSubjectDto>> Create(
        [FromBody] CreateSvcSubjectRequest request, CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SvcSubjectDto>> Update(
        Guid id, [FromBody] UpdateSvcSubjectRequest request, CancellationToken ct)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.UpdateAsync(id, request, ct));
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<SvcSubjectDto>> Activate(Guid id, CancellationToken ct)
        => Ok(await _service.ActivateAsync(id, ct));

    [HttpPost("{id:guid}/deactivate")]
    public async Task<ActionResult<SvcSubjectDto>> Deactivate(Guid id, CancellationToken ct)
        => Ok(await _service.DeactivateAsync(id, ct));
}
