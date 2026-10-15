using Nexo.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Features.Financial;
using Nexo.Domain.Enums;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/financial")]
// Contas a pagar/receber move money: management only (no other role has a financial screen).
[Authorize(Roles = AppRoles.Management)]
public class FinancialController : ControllerBase
{
    private readonly FinancialService _service;
    private readonly FinancialRecurrenceService _recurrences;

    public FinancialController(FinancialService service, FinancialRecurrenceService recurrences)
    {
        _service     = service;
        _recurrences = recurrences;
    }

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

    /// <summary>
    /// The livro in one regime: competência (fact date, open entries count) or caixa (payment date,
    /// settled only). Period [from, to) in UTC instants; default: the last 30 days.
    /// </summary>
    [HttpGet("ledger")]
    public async Task<ActionResult<LedgerDto>> GetLedger(
        [FromQuery] string regime = "accrual",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] Guid? storeId = null,
        [FromQuery] string? category = null,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<LedgerRegime>(regime, ignoreCase: true, out var parsedRegime) || !Enum.IsDefined(parsedRegime))
            return UnprocessableEntity(new { error = "Regime inválido: use accrual (competência) ou cash (caixa)." });
        FinancialCategory? parsedCategory = null;
        if (category is not null)
        {
            if (!Enum.TryParse<FinancialCategory>(category, ignoreCase: true, out var c) || !Enum.IsDefined(c))
                return UnprocessableEntity(new { error = "Categoria inválida." });
            parsedCategory = c;
        }

        var end   = (to ?? DateTime.UtcNow).ToUniversalTime();
        var start = (from ?? end.AddDays(-30)).ToUniversalTime();
        return Ok(await _service.GetLedgerAsync(new LedgerFilter(parsedRegime, start, end, storeId, parsedCategory), ct));
    }

    /// <summary>Contas a receber: open/overdue/settled títulos with the aging of what is open.</summary>
    [HttpGet("receivables")]
    public Task<ActionResult<TitlesDto>> GetReceivables(
        [FromQuery] string status = "open", [FromQuery] DateOnly? dueFrom = null, [FromQuery] DateOnly? dueTo = null,
        [FromQuery] Guid? customerId = null, CancellationToken ct = default)
        => TitlesAsync(TransactionType.Receivable, status, dueFrom, dueTo, customerId, ct);

    /// <summary>Contas a pagar: open/overdue/settled títulos with the aging of what is open.</summary>
    [HttpGet("payables")]
    public Task<ActionResult<TitlesDto>> GetPayables(
        [FromQuery] string status = "open", [FromQuery] DateOnly? dueFrom = null, [FromQuery] DateOnly? dueTo = null,
        [FromQuery] Guid? supplierId = null, CancellationToken ct = default)
        => TitlesAsync(TransactionType.Payable, status, dueFrom, dueTo, null, ct, supplierId);

    private async Task<ActionResult<TitlesDto>> TitlesAsync(
        TransactionType type, string status, DateOnly? dueFrom, DateOnly? dueTo, Guid? customerId, CancellationToken ct,
        Guid? supplierId = null)
    {
        if (!Enum.TryParse<TitleStatusFilter>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            return UnprocessableEntity(new { error = "Situação inválida: use open, overdue, settled ou all." });
        return Ok(await _service.GetTitlesAsync(new TitlesFilter(
            type, parsed,
            dueFrom is { } f ? BusinessClock.DayStartUtc(f) : null,
            dueTo is { } t ? BusinessClock.DayStartUtc(t.AddDays(1)) : null,   // dueTo is inclusive (Brazil day)
            customerId, supplierId), ct));
    }

    /// <summary>Baixa total ou parcial, com juros, multa e desconto.</summary>
    [HttpPost("transactions/{id:guid}/settle")]
    public async Task<ActionResult<SettlementResultDto>> Settle(
        Guid id, [FromBody] SettleTransactionRequest request, CancellationToken ct)
        => Ok(await _service.SettleAsync(id, request, ct));

    /// <summary>Título manual parcelado (mensal).</summary>
    [HttpPost("transactions/installments")]
    public async Task<ActionResult<IReadOnlyList<FinancialTransactionDto>>> CreateInstallments(
        [FromBody] CreateInstallmentsRequest request, CancellationToken ct)
        => Ok(await _service.CreateInstallmentsAsync(request, ct));

    // ── Recorrências ─────────────────────────────────────────────────────────

    [HttpGet("recurrences")]
    public async Task<ActionResult<IReadOnlyList<RecurrenceDto>>> GetRecurrences(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
        => Ok(await _recurrences.GetAllAsync(includeInactive, ct));

    [HttpPost("recurrences")]
    public async Task<ActionResult<RecurrenceDto>> CreateRecurrence([FromBody] CreateRecurrenceRequest request, CancellationToken ct)
        => Ok(await _recurrences.CreateAsync(request, ct));

    [HttpPut("recurrences/{id:guid}")]
    public async Task<ActionResult<RecurrenceDto>> UpdateRecurrence(Guid id, [FromBody] UpdateRecurrenceRequest request, CancellationToken ct)
        => Ok(await _recurrences.UpdateAsync(id, request, ct));

    [HttpPost("recurrences/{id:guid}/deactivate")]
    public async Task<ActionResult<RecurrenceDto>> DeactivateRecurrence(Guid id, CancellationToken ct)
        => Ok(await _recurrences.DeactivateAsync(id, ct));

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
