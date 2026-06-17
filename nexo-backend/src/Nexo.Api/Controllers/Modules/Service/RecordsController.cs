using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Modules.Service;
using Nexo.Domain.Modules.Service;

namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>
/// Service record entries (ORKEN SERVICE) — internal notes / history with durable attachment
/// references. Store-scoped. v1 contexts: Customer and Subject only. Append-only; DELETE is a
/// hard delete. All endpoints require an active service-family subscription.
/// </summary>
[ApiController]
[Route("api/v1/service/records")]
[Authorize]
[RequireServiceModule]
public class RecordsController : ControllerBase
{
    private readonly SvcRecordEntryService                   _service;
    private readonly IValidator<CreateSvcRecordEntryRequest> _createValidator;

    public RecordsController(
        SvcRecordEntryService service, IValidator<CreateSvcRecordEntryRequest> createValidator)
    {
        _service         = service;
        _createValidator = createValidator;
    }

    /// <summary>Lists records for one context (both contextType and contextId are required).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SvcRecordEntryDto>>> GetByContext(
        [FromQuery] SvcRecordContextType? contextType,
        [FromQuery] Guid? contextId,
        CancellationToken ct = default)
    {
        if (contextType is null || contextId is null || contextId == Guid.Empty)
            return BadRequest(new { error = "contextType and contextId are required." });
        if (contextType is not (SvcRecordContextType.Customer or SvcRecordContextType.Subject))
            return BadRequest(new { error = "ContextType is not supported yet. Use Customer or Subject." });

        return Ok(await _service.GetByContextAsync(contextType.Value, contextId.Value, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SvcRecordEntryDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SvcRecordEntryDto>> Create(
        [FromBody] CreateSvcRecordEntryRequest request, CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    /// <summary>Hard delete (records have no soft-delete state). Returns 204.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }
}
