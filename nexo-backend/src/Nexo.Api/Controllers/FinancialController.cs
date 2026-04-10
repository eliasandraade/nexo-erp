using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Features.Financial;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/financial")]
[Authorize]
public class FinancialController : ControllerBase
{
    private readonly FinancialService _service;

    public FinancialController(FinancialService service) => _service = service;

    // ── Accounts ─────────────────────────────────────────────────────────────

    [HttpGet("accounts")]
    public async Task<ActionResult<IReadOnlyList<FinancialAccountDto>>> GetAccounts(
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
        => Ok(await _service.GetAllAccountsAsync(includeInactive, ct));

    [HttpGet("accounts/{id:guid}")]
    public async Task<ActionResult<FinancialAccountDto>> GetAccountById(Guid id, CancellationToken ct)
        => Ok(await _service.GetAccountByIdAsync(id, ct));

    [HttpPost("accounts")]
    public async Task<ActionResult<FinancialAccountDto>> CreateAccount(
        [FromBody] CreateFinancialAccountRequest request,
        CancellationToken ct)
    {
        var dto = await _service.CreateAccountAsync(request, ct);
        return CreatedAtAction(nameof(GetAccountById), new { id = dto.Id }, dto);
    }

    [HttpPut("accounts/{id:guid}")]
    public async Task<ActionResult<FinancialAccountDto>> UpdateAccount(
        Guid id,
        [FromBody] UpdateFinancialAccountRequest request,
        CancellationToken ct)
        => Ok(await _service.UpdateAccountAsync(id, request, ct));

    [HttpPost("accounts/{id:guid}/activate")]
    public async Task<IActionResult> ActivateAccount(Guid id, CancellationToken ct)
    {
        await _service.ActivateAccountAsync(id, ct);
        return NoContent();
    }

    [HttpPost("accounts/{id:guid}/deactivate")]
    public async Task<IActionResult> DeactivateAccount(Guid id, CancellationToken ct)
    {
        await _service.DeactivateAccountAsync(id, ct);
        return NoContent();
    }

    // ── Transactions ─────────────────────────────────────────────────────────

    [HttpGet("transactions/pending")]
    public async Task<ActionResult<IReadOnlyList<FinancialTransactionDto>>> GetPending(CancellationToken ct)
        => Ok(await _service.GetPendingTransactionsAsync(ct));

    [HttpGet("transactions/{id:guid}")]
    public async Task<ActionResult<FinancialTransactionDto>> GetTransactionById(Guid id, CancellationToken ct)
        => Ok(await _service.GetTransactionByIdAsync(id, ct));

    [HttpGet("accounts/{accountId:guid}/transactions")]
    public async Task<ActionResult<IReadOnlyList<FinancialTransactionDto>>> GetByAccount(Guid accountId, CancellationToken ct)
        => Ok(await _service.GetTransactionsByAccountAsync(accountId, ct));

    [HttpPost("transactions")]
    public async Task<ActionResult<FinancialTransactionDto>> CreateTransaction(
        [FromBody] CreateTransactionRequest request,
        CancellationToken ct)
    {
        var dto = await _service.CreateTransactionAsync(request, ct);
        return CreatedAtAction(nameof(GetTransactionById), new { id = dto.Id }, dto);
    }

    [HttpPut("transactions/{id:guid}")]
    public async Task<ActionResult<FinancialTransactionDto>> UpdateTransaction(
        Guid id,
        [FromBody] UpdateTransactionRequest request,
        CancellationToken ct)
        => Ok(await _service.UpdateTransactionAsync(id, request, ct));

    [HttpPost("transactions/{id:guid}/pay")]
    public async Task<ActionResult<FinancialTransactionDto>> MarkPaid(
        Guid id,
        [FromBody] MarkTransactionPaidRequest request,
        CancellationToken ct)
        => Ok(await _service.MarkPaidAsync(id, request, ct));

    [HttpPost("transactions/{id:guid}/cancel")]
    public async Task<ActionResult<FinancialTransactionDto>> CancelTransaction(Guid id, CancellationToken ct)
        => Ok(await _service.CancelTransactionAsync(id, ct));
}
