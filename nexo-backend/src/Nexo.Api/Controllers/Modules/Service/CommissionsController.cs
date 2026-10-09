using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Modules.Service;
using Nexo.Domain.Modules.Service;

namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>
/// Service commissions (ORKEN SERVICE) — the commission ledger and its payouts. Entries are never
/// created here: they are recognised by the payment / appointment / package flows. This controller
/// reads them, closes a professional's period into a payout, and marks a payout paid (which is
/// when the expense reaches the financeiro). Tenant/store isolation comes from the EF query
/// filters; gated by the service module; Gerente/Diretoria only, like every Service operation.
/// </summary>
[ApiController]
[Route("api/v1/service/commissions")]
[Authorize(Roles = ServiceRoles.Management)]
[RequireServiceModule]
public class CommissionsController : ControllerBase
{
    private readonly SvcCommissionService                             _service;
    private readonly IValidator<CloseSvcCommissionPayoutRequest>      _closeValidator;
    private readonly IValidator<MarkSvcCommissionPayoutPaidRequest>   _payValidator;

    public CommissionsController(
        SvcCommissionService service,
        IValidator<CloseSvcCommissionPayoutRequest> closeValidator,
        IValidator<MarkSvcCommissionPayoutPaidRequest> payValidator)
    {
        _service = service; _closeValidator = closeValidator; _payValidator = payValidator;
    }

    /// <summary>Entries, newest first. status: open (not in a payout) | settled (in a payout).</summary>
    [HttpGet("entries")]
    public async Task<ActionResult<IReadOnlyList<SvcCommissionEntryDto>>> GetEntries(
        [FromQuery] Guid? professionalId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? status, [FromQuery] Guid? payoutId, CancellationToken ct = default)
    {
        bool? settled = status?.ToLowerInvariant() switch
        {
            null or "" or "all" => null,
            "open"              => false,
            "settled"           => true,
            _                   => throw new ValidationException(
                [new ValidationFailure("status", "status must be one of: open, settled, all.")]),
        };
        return Ok(await _service.GetEntriesAsync(professionalId, from, to, settled, payoutId, ct));
    }

    /// <summary>Per-professional position: open, closed-but-unpaid and paid.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<IReadOnlyList<SvcCommissionSummaryDto>>> GetSummary(
        [FromQuery] Guid? professionalId, CancellationToken ct = default)
        => Ok(await _service.GetSummaryAsync(professionalId, ct));

    [HttpGet("payouts")]
    public async Task<ActionResult<IReadOnlyList<SvcCommissionPayoutDto>>> GetPayouts(
        [FromQuery] Guid? professionalId, [FromQuery] SvcCommissionPayoutStatus? status, CancellationToken ct = default)
        => Ok(await _service.GetPayoutsAsync(professionalId, status, ct));

    [HttpGet("payouts/{id:guid}")]
    public async Task<ActionResult<SvcCommissionPayoutDetailDto>> GetPayout(Guid id, CancellationToken ct)
        => Ok(await _service.GetPayoutByIdAsync(id, ct));

    /// <summary>Closes the professional's open entries in the period into a Pending payout.</summary>
    [HttpPost("payouts")]
    public async Task<ActionResult<SvcCommissionPayoutDetailDto>> ClosePayout(
        [FromBody] CloseSvcCommissionPayoutRequest request, CancellationToken ct)
    {
        await _closeValidator.ValidateAndThrowAsync(request, ct);
        var dto = await _service.ClosePayoutAsync(request, ct);
        return CreatedAtAction(nameof(GetPayout), new { id = dto.Payout.Id }, dto);
    }

    /// <summary>Marks the payout paid and posts the settled Payable. Idempotent.</summary>
    [HttpPost("payouts/{id:guid}/pay")]
    public async Task<ActionResult<SvcCommissionPayoutDto>> MarkPaid(
        Guid id, [FromBody] MarkSvcCommissionPayoutPaidRequest? request, CancellationToken ct)
    {
        request ??= new MarkSvcCommissionPayoutPaidRequest();
        await _payValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.MarkPayoutPaidAsync(id, request, ct));
    }
}
