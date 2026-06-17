using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Modules.Service;

namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>
/// Service professionals (ORKEN SERVICE). Store-scoped; no professional login in v1.
/// All endpoints require an active service-family subscription.
/// </summary>
[ApiController]
[Route("api/v1/service/professionals")]
[Authorize]
[RequireServiceModule]
public class ProfessionalsController : ControllerBase
{
    private readonly SvcProfessionalService                    _service;
    private readonly IValidator<CreateSvcProfessionalRequest>  _createValidator;
    private readonly IValidator<UpdateSvcProfessionalRequest>  _updateValidator;

    public ProfessionalsController(
        SvcProfessionalService                   service,
        IValidator<CreateSvcProfessionalRequest> createValidator,
        IValidator<UpdateSvcProfessionalRequest> updateValidator)
    {
        _service         = service;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>Lists professionals. Pass onlyActive=true to exclude deactivated ones.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SvcProfessionalDto>>> GetAll(
        [FromQuery] bool onlyActive = false, CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(onlyActive, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SvcProfessionalDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SvcProfessionalDto>> Create(
        [FromBody] CreateSvcProfessionalRequest request, CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SvcProfessionalDto>> Update(
        Guid id, [FromBody] UpdateSvcProfessionalRequest request, CancellationToken ct)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.UpdateAsync(id, request, ct));
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<SvcProfessionalDto>> Activate(Guid id, CancellationToken ct)
        => Ok(await _service.ActivateAsync(id, ct));

    [HttpPost("{id:guid}/deactivate")]
    public async Task<ActionResult<SvcProfessionalDto>> Deactivate(Guid id, CancellationToken ct)
        => Ok(await _service.DeactivateAsync(id, ct));
}
