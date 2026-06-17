using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Modules.Service;

namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>
/// Service catalog — serviços/procedimentos/aulas (ORKEN SERVICE). Store-scoped.
/// All endpoints require an active service-family subscription.
/// </summary>
[ApiController]
[Route("api/v1/service/catalog")]
[Authorize]
[RequireServiceModule]
public class CatalogController : ControllerBase
{
    private readonly SvcCatalogItemService                    _service;
    private readonly IValidator<CreateSvcCatalogItemRequest>  _createValidator;
    private readonly IValidator<UpdateSvcCatalogItemRequest>  _updateValidator;

    public CatalogController(
        SvcCatalogItemService                   service,
        IValidator<CreateSvcCatalogItemRequest> createValidator,
        IValidator<UpdateSvcCatalogItemRequest> updateValidator)
    {
        _service         = service;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>Lists catalog items. Pass onlyActive=true to exclude deactivated ones.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SvcCatalogItemDto>>> GetAll(
        [FromQuery] bool onlyActive = false, CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(onlyActive, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SvcCatalogItemDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SvcCatalogItemDto>> Create(
        [FromBody] CreateSvcCatalogItemRequest request, CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SvcCatalogItemDto>> Update(
        Guid id, [FromBody] UpdateSvcCatalogItemRequest request, CancellationToken ct)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.UpdateAsync(id, request, ct));
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<SvcCatalogItemDto>> Activate(Guid id, CancellationToken ct)
        => Ok(await _service.ActivateAsync(id, ct));

    [HttpPost("{id:guid}/deactivate")]
    public async Task<ActionResult<SvcCatalogItemDto>> Deactivate(Guid id, CancellationToken ct)
        => Ok(await _service.DeactivateAsync(id, ct));
}
