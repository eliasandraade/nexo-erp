using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Features.Financial;

/// <summary>
/// Brings the verticals' own money records into the livro, so contas a pagar, fluxo de caixa and the
/// ledger see everything:
///   - despesas do restaurante (<see cref="RestExpense"/>): one título per expense, open until the
///     expense has a payment date, then paid on it;
///   - movimentações confirmadas do Build/Interpreter (<see cref="FinancialMovement"/>): already
///     happened, so posted settled on their date (transfers between accounts are not income or expense
///     and stay out).
///
/// The record of origin stays the source of truth: its lançamento is corrected only there (edit, delete,
/// void, reprocess) — an open mirror is cancelled, a settled one is reversed, and a new one is posted
/// when the facts changed. It never calls SaveChanges: the caller's unit of work saves both together.
/// </summary>
public class VerticalLedgerPostingService
{
    public const string RestExpenseReference         = "RestExpense";
    public const string RestExpenseReversalReference = "RestExpenseReversal";
    public const string MovementReference            = "FinancialMovement";
    public const string MovementReversalReference    = "FinancialMovementReversal";

    /// <summary>ReferenceTypes whose lançamentos are paid at the origin, never in the financeiro.</summary>
    public static bool IsSettledAtOrigin(string? referenceType)
        => referenceType is RestExpenseReference or MovementReference;

    private readonly IFinancialRepository _financial;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentStore _currentStore;
    private readonly ICurrentUser _currentUser;

    public VerticalLedgerPostingService(
        IFinancialRepository financial, ICurrentTenant currentTenant, ICurrentStore currentStore, ICurrentUser currentUser)
    {
        _financial     = financial;
        _currentTenant = currentTenant;
        _currentStore  = currentStore;
        _currentUser   = currentUser;
    }

    // ── Despesas do restaurante ──────────────────────────────────────────────

    /// <summary>Creates, keeps or replaces the expense's lançamento so it matches the expense.</summary>
    public async Task SyncRestExpenseAsync(RestExpense expense, CancellationToken ct = default)
    {
        var active = await ActiveMirrorAsync(RestExpenseReference, RestExpenseReversalReference, expense.Id, ct);
        var desired = Desired(expense);

        if (active is not null && desired is not null && Matches(active, desired))
        {
            if (active.IsOpen && active.Description != desired.Description)
                active.Update(active.Amount, desired.Description, active.DueDate);
            return;
        }

        if (active is not null)
            await RetireAsync(active, RestExpenseReversalReference, expense.Id, $"Estorno — {active.Description}", ct);
        if (desired is null) return;   // R$ 0: nothing to owe

        var tx = FinancialTransaction.Create(
            _currentTenant.Id, await PayableAccountAsync(ct), TransactionType.Payable, desired.Category, desired.Amount,
            desired.Description, desired.DueDate, _currentUser.UserId,
            referenceType: RestExpenseReference, referenceId: expense.Id,
            storeId: StoreOf(expense.StoreId), competenceDate: desired.CompetenceDate);
        if (desired.PaidAt is { } paidAt) tx.MarkPaid(paidAt);
        await _financial.AddTransactionAsync(tx, ct);
    }

    /// <summary>The expense is being deleted: its lançamento is cancelled (open) or reversed (paid).</summary>
    public async Task RetireRestExpenseAsync(RestExpense expense, CancellationToken ct = default)
    {
        var active = await ActiveMirrorAsync(RestExpenseReference, RestExpenseReversalReference, expense.Id, ct);
        if (active is not null)
            await RetireAsync(active, RestExpenseReversalReference, expense.Id, $"Estorno — {active.Description}", ct);
    }

    // ── Movimentações (Build / Interpreter) ─────────────────────────────────

    /// <summary>Posts a confirmed movement as a settled lançamento on its date (transfers stay out).</summary>
    public async Task PostMovementAsync(FinancialMovement movement, CancellationToken ct = default)
    {
        if (movement.Status != MovementStatus.Confirmed || movement.Nature == MovementNature.Transfer || movement.Amount <= 0)
            return;
        if (await ActiveMirrorAsync(MovementReference, MovementReversalReference, movement.Id, ct) is not null)
            return;   // already posted

        var isIn = movement.Direction == MovementDirection.In;
        var category = isIn
            ? FinancialCategory.OtherIncome
            : movement.Nature == MovementNature.Expense ? FinancialCategory.OperatingExpense : FinancialCategory.OtherExpense;
        var when = BusinessClock.NoonUtc(movement.Date);
        var account = isIn ? await ReceivableAccountAsync(ct) : await PayableAccountAsync(ct);

        var tx = FinancialTransaction.Create(
            _currentTenant.Id, account, isIn ? TransactionType.Receivable : TransactionType.Payable, category,
            movement.Amount, movement.Description, when, _currentUser.UserId,
            referenceType: MovementReference, referenceId: movement.Id,
            storeId: movement.ContextType == FinancialContextType.Loja ? movement.ContextId : null,
            competenceDate: when,
            supplierId: isIn ? null : movement.SupplierId);
        tx.MarkPaid(when);
        await _financial.AddTransactionAsync(tx, ct);
    }

    /// <summary>The movement was voided or sent back to draft: its lançamento is reversed.</summary>
    public async Task RetireMovementAsync(FinancialMovement movement, CancellationToken ct = default)
    {
        var active = await ActiveMirrorAsync(MovementReference, MovementReversalReference, movement.Id, ct);
        if (active is not null)
            await RetireAsync(active, MovementReversalReference, movement.Id, $"Estorno — {active.Description}", ct);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed record DesiredMirror(
        FinancialCategory Category, decimal Amount, string Description, DateTime CompetenceDate, DateTime DueDate, DateTime? PaidAt);

    private static DesiredMirror? Desired(RestExpense e)
    {
        if (e.Amount <= 0) return null;
        var due = BusinessClock.NoonUtc(e.PaymentDate ?? e.CompetenceDate);
        return new DesiredMirror(
            CategoryOf(e.Category), Math.Round(e.Amount, 2, MidpointRounding.AwayFromZero),
            Truncate($"{e.Category} — {e.Description}", 500),
            BusinessClock.NoonUtc(e.CompetenceDate), due,
            e.PaymentDate is { } paid ? BusinessClock.NoonUtc(paid) : null);
    }

    private static bool Matches(FinancialTransaction t, DesiredMirror d)
        => t.Amount == d.Amount && t.Category == d.Category && t.CompetenceDate == d.CompetenceDate
           && (d.PaidAt is { } paidAt
               ? t.Status == TransactionStatus.Paid && t.PaidAt == paidAt
               : t.IsOpen && t.DueDate == d.DueDate);

    /// <summary>The restaurant's free-text categories mapped to the livro's lines.</summary>
    public static FinancialCategory CategoryOf(string category)
    {
        var c = category.Trim().ToLowerInvariant();
        if (c.Contains("imposto") || c.Contains("taxa")) return FinancialCategory.Tax;
        if (c.Contains("folha") || c.Contains("salário") || c.Contains("salario")) return FinancialCategory.Payroll;
        if (c is "outros" or "outro") return FinancialCategory.OtherExpense;
        return FinancialCategory.OperatingExpense;
    }

    /// <summary>The newest lançamento of the record that is neither cancelled nor reversed.</summary>
    private async Task<FinancialTransaction?> ActiveMirrorAsync(
        string referenceType, string reversalReference, Guid referenceId, CancellationToken ct)
    {
        var reversed = (await _financial.GetTransactionsByReferenceAsync(reversalReference, referenceId, ct))
            .Where(r => r.ReversesTransactionId.HasValue).Select(r => r.ReversesTransactionId!.Value).ToHashSet();
        return (await _financial.GetTransactionsByReferenceAsync(referenceType, referenceId, ct))
            .Where(t => t.Status != TransactionStatus.Cancelled && !reversed.Contains(t.Id))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefault();
    }

    private async Task RetireAsync(
        FinancialTransaction mirror, string reversalReference, Guid referenceId, string description, CancellationToken ct)
    {
        if (mirror.IsOpen)
        {
            mirror.Cancel();
            return;
        }
        if (mirror.Status != TransactionStatus.Paid) return;
        var opposite = mirror.TransactionType == TransactionType.Receivable ? await PayableAccountAsync(ct) : await ReceivableAccountAsync(ct);
        await _financial.AddTransactionAsync(FinancialTransaction.CreateReversal(
            mirror, opposite, Truncate(description, 500), DateTime.UtcNow, _currentUser.UserId, reversalReference, referenceId), ct);
    }

    private async Task<Guid> PayableAccountAsync(CancellationToken ct)
        => (await _financial.GetDefaultByTypeAsync(FinancialAccountType.Payable, ct)
            ?? throw new DomainException("Nenhuma conta 'Contas a Pagar' ativa.")).Id;

    private async Task<Guid> ReceivableAccountAsync(CancellationToken ct)
        => (await _financial.GetDefaultByTypeAsync(FinancialAccountType.Receivable, ct)
            ?? throw new DomainException("Nenhuma conta 'Contas a Receber' ativa.")).Id;

    /// <summary>A record not saved yet has no StoreId — it is being created in the request's store.</summary>
    private Guid? StoreOf(Guid storeId)
        => storeId != Guid.Empty ? storeId : _currentStore.IsResolved ? _currentStore.Id : null;

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
