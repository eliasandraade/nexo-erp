using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Filters;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Api.Controllers.Modules.Restaurante;

[ApiController]
[Route("api/restaurante/expenses")]
[Authorize]
[RequireModule("restaurante")]
public class ExpensesController : ControllerBase
{
    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;

    public ExpensesController(NexoDbContext db, ICurrentTenant tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    // GET /api/restaurante/expenses?from=yyyy-MM-dd&to=yyyy-MM-dd
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExpenseDto>>> List(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct = default)
    {
        var q = _db.RestExpenses.AsQueryable();

        if (TryParseDate(from) is { } fromDate)
            q = q.Where(e => e.CompetenceDate >= fromDate);

        if (TryParseDate(to) is { } toDate)
            q = q.Where(e => e.CompetenceDate <= toDate);

        var list = await q
            .OrderByDescending(e => e.CompetenceDate)
            .ThenBy(e => e.Category)
            .Select(e => new ExpenseDto(
                e.Id, e.Description, e.Category, e.Amount,
                e.CompetenceDate, e.PaymentDate, e.IsRecurring, e.CreatedAt))
            .ToListAsync(ct);

        return Ok(list);
    }

    // GET /api/restaurante/expenses/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExpenseDto>> Get(Guid id, CancellationToken ct)
    {
        var e = await _db.RestExpenses.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound();
        return Ok(new ExpenseDto(e.Id, e.Description, e.Category, e.Amount,
            e.CompetenceDate, e.PaymentDate, e.IsRecurring, e.CreatedAt));
    }

    // POST /api/restaurante/expenses
    [HttpPost]
    public async Task<ActionResult<ExpenseDto>> Create(
        [FromBody] CreateExpenseRequest req, CancellationToken ct)
    {
        var expense = RestExpense.Create(
            _tenant.Id, req.Description, req.Category, req.Amount,
            req.CompetenceDate, req.PaymentDate, req.IsRecurring);

        _db.RestExpenses.Add(expense);
        await _db.SaveChangesAsync(ct);

        var dto = new ExpenseDto(expense.Id, expense.Description, expense.Category,
            expense.Amount, expense.CompetenceDate, expense.PaymentDate,
            expense.IsRecurring, expense.CreatedAt);

        return CreatedAtAction(nameof(Get), new { id = expense.Id }, dto);
    }

    // PUT /api/restaurante/expenses/{id}
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ExpenseDto>> Update(
        Guid id, [FromBody] CreateExpenseRequest req, CancellationToken ct)
    {
        var expense = await _db.RestExpenses.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (expense is null) return NotFound();

        expense.Update(req.Description, req.Category, req.Amount,
            req.CompetenceDate, req.PaymentDate, req.IsRecurring);
        await _db.SaveChangesAsync(ct);

        return Ok(new ExpenseDto(expense.Id, expense.Description, expense.Category,
            expense.Amount, expense.CompetenceDate, expense.PaymentDate,
            expense.IsRecurring, expense.CreatedAt));
    }

    // DELETE /api/restaurante/expenses/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var expense = await _db.RestExpenses.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (expense is null) return NotFound();

        _db.RestExpenses.Remove(expense);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DateOnly? TryParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateOnly.TryParseExact(value, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : null;
    }
}

// ── Request/Response records ──────────────────────────────────────────────────

public record CreateExpenseRequest(
    string    Description,
    string    Category,
    decimal   Amount,
    DateOnly  CompetenceDate,
    DateOnly? PaymentDate,
    bool      IsRecurring);

public record ExpenseDto(
    Guid      Id,
    string    Description,
    string    Category,
    decimal   Amount,
    DateOnly  CompetenceDate,
    DateOnly? PaymentDate,
    bool      IsRecurring,
    DateTime  CreatedAt);
