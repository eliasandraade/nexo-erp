using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Modules.Service;

namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>
/// Service package templates (ORKEN SERVICE — pacotes). Store-scoped aggregate with template items.
/// All endpoints require an active service-family subscription.
/// </summary>
[ApiController]
[Route("api/v1/service/packages")]
[Authorize]
[RequireServiceModule]
public class PackagesController : ControllerBase
{
    private readonly SvcPackageService                          _service;
    private readonly IValidator<CreateSvcPackageRequest>        _createValidator;
    private readonly IValidator<UpdateSvcPackageRequest>        _updateValidator;
    private readonly IValidator<UpdateSvcPackagePriceRequest>   _priceValidator;
    private readonly IValidator<AddSvcPackageItemRequest>       _addItemValidator;
    private readonly IValidator<UpdateSvcPackageItemRequest>    _updateItemValidator;

    public PackagesController(
        SvcPackageService service,
        IValidator<CreateSvcPackageRequest> createValidator,
        IValidator<UpdateSvcPackageRequest> updateValidator,
        IValidator<UpdateSvcPackagePriceRequest> priceValidator,
        IValidator<AddSvcPackageItemRequest> addItemValidator,
        IValidator<UpdateSvcPackageItemRequest> updateItemValidator)
    {
        _service = service; _createValidator = createValidator; _updateValidator = updateValidator;
        _priceValidator = priceValidator; _addItemValidator = addItemValidator; _updateItemValidator = updateItemValidator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SvcPackageDto>>> GetAll([FromQuery] bool? active, CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(active, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SvcPackageDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SvcPackageDto>> Create([FromBody] CreateSvcPackageRequest request, CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SvcPackageDto>> Update(Guid id, [FromBody] UpdateSvcPackageRequest request, CancellationToken ct)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.UpdateAsync(id, request, ct));
    }

    [HttpPut("{id:guid}/price")]
    public async Task<ActionResult<SvcPackageDto>> UpdatePrice(Guid id, [FromBody] UpdateSvcPackagePriceRequest request, CancellationToken ct)
    {
        await _priceValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.UpdatePriceAsync(id, request, ct));
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<SvcPackageDto>> Activate(Guid id, CancellationToken ct)
        => Ok(await _service.ActivateAsync(id, ct));

    [HttpPost("{id:guid}/deactivate")]
    public async Task<ActionResult<SvcPackageDto>> Deactivate(Guid id, CancellationToken ct)
        => Ok(await _service.DeactivateAsync(id, ct));

    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<SvcPackageDto>> AddItem(Guid id, [FromBody] AddSvcPackageItemRequest request, CancellationToken ct)
    {
        await _addItemValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.AddItemAsync(id, request, ct));
    }

    [HttpPut("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<SvcPackageDto>> UpdateItem(Guid id, Guid itemId, [FromBody] UpdateSvcPackageItemRequest request, CancellationToken ct)
    {
        await _updateItemValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.UpdateItemAsync(id, itemId, request, ct));
    }

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<SvcPackageDto>> RemoveItem(Guid id, Guid itemId, CancellationToken ct)
        => Ok(await _service.RemoveItemAsync(id, itemId, ct));
}
