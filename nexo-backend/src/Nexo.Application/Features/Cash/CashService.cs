using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Cash;

/// <summary>
/// Caixa (cash drawer sessions). The session owns the money rules — expected balance and closing
/// difference live in <see cref="CashSession"/>; this service adds locking, validation of manual
/// movements and the per-session summary (drawer totals and receipts by payment method).
/// </summary>
public class CashService
{
    private readonly ICashRepository _cash;
    private readonly IUnitOfWork _uow;
    private readonly IAuditWriter _audit;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;

    public CashService(
        ICashRepository cash, IUnitOfWork uow, IAuditWriter audit,
        ICurrentTenant currentTenant, ICurrentUser currentUser)
    {
        _cash          = cash;
        _uow           = uow;
        _audit         = audit;
        _currentTenant = currentTenant;
        _currentUser   = currentUser;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<CashSessionDto?> GetOpenSessionAsync(CancellationToken ct = default)
    {
        var session = await _cash.GetOpenSessionAsync(ct);
        return session is null ? null : await MapWithSummaryAsync(session, includeMovements: false, ct);
    }

    /// <summary>History: every session of the store, newest first, with expected/counted/difference.</summary>
    public async Task<IReadOnlyList<CashSessionDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _cash.GetAllAsync(ct);
        return list.Select(s => MapToDto(s, null, null)).ToList();
    }

    public async Task<CashSessionDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var session = await _cash.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("CashSession", id);
        return await MapWithSummaryAsync(session, includeMovements: true, ct);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public async Task<CashSessionDto> OpenAsync(OpenCashSessionRequest request, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);

        // Serialise two opens by the same user (double click) — the one-open-per-user rule is
        // checked under this lock.
        await _cash.LockUserOpeningAsync(_currentUser.UserId, ct);
        var existing = await _cash.GetOpenSessionByUserAsync(_currentUser.UserId, ct);
        if (existing is not null)
            throw new ConflictException("You already have an open cash session.");

        var session = CashSession.Open(
            _currentTenant.Id,
            _currentUser.UserId,
            request.OpeningBalance,
            request.Notes);

        await _cash.AddSessionAsync(session, ct);
        _audit.Stage(AuditActions.CashOpen, AuditSeverity.Info, "CashSession", session.Id.ToString(),
            $"Caixa aberto com {session.OpeningBalance:F2}",
            tenantId: _currentTenant.Id, actorId: _currentUser.UserId, actorName: _currentUser.Name,
            metadata: new { session.OpeningBalance });
        await _cash.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return MapToDto(session, [], Summary(session, [], []));
    }

    public async Task<CashSessionDto> CloseAsync(Guid sessionId, CloseCashSessionRequest request, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);

        await _cash.LockSessionAsync(sessionId, ct);
        var session = await _cash.GetByIdAsync(sessionId, ct)
            ?? throw new NotFoundException("CashSession", sessionId);

        var movements = await _cash.GetMovementsBySessionAsync(sessionId, ct);
        session.Close(_currentUser.UserId, request.ClosingBalance, movements, request.Notes);

        _audit.Stage(AuditActions.CashClose,
            session.Difference == 0 ? AuditSeverity.Info : AuditSeverity.Warning,
            "CashSession", session.Id.ToString(),
            session.Difference == 0
                ? $"Caixa fechado sem divergência ({session.ClosingBalance:F2})"
                : $"Caixa fechado com divergência de {session.Difference:F2} (esperado {session.ExpectedBalance:F2}, contado {session.ClosingBalance:F2})",
            tenantId: _currentTenant.Id, actorId: _currentUser.UserId, actorName: _currentUser.Name,
            metadata: new { session.ExpectedBalance, Counted = session.ClosingBalance, session.Difference, session.ClosingNotes });

        await _cash.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await MapWithSummaryAsync(session, includeMovements: true, ct);
    }

    /// <summary>
    /// Manual movement: only suprimento (Deposit) and sangria (Withdrawal), with a reason. Sales,
    /// refunds, opening and closing are written by the system, never by hand. A sangria cannot take
    /// out more than the drawer holds.
    /// </summary>
    public async Task<CashMovementDto> AddMovementAsync(Guid sessionId, AddCashMovementRequest request, CancellationToken ct = default)
    {
        if (!Enum.TryParse<CashMovementType>(request.MovementType, ignoreCase: true, out var movementType)
            || !Enum.IsDefined(movementType) || !movementType.IsManual())
            throw new DomainException("Movimento manual aceita apenas suprimento (Deposit) ou sangria (Withdrawal).");
        if (request.Amount <= 0 || decimal.Round(request.Amount, 2) != request.Amount)
            throw new DomainException("O valor deve ser positivo e ter até duas casas decimais.");
        if (string.IsNullOrWhiteSpace(request.Description))
            throw new DomainException("Informe o motivo da movimentação.");

        await using var tx = await _uow.BeginTransactionAsync(ct);

        await _cash.LockSessionAsync(sessionId, ct);
        var session = await _cash.GetByIdAsync(sessionId, ct)
            ?? throw new NotFoundException("CashSession", sessionId);
        session.EnsureOpen();

        if (movementType == CashMovementType.Withdrawal)
        {
            var available = CashSession.ExpectedBalanceFrom(
                session.OpeningBalance, await _cash.GetMovementsBySessionAsync(sessionId, ct));
            if (request.Amount > available)
                throw new DomainException(
                    $"Sangria maior que o saldo do caixa: disponível {available:F2}, solicitado {request.Amount:F2}.");
        }

        var movement = CashMovement.Create(
            _currentTenant.Id,
            sessionId,
            movementType,
            request.Amount,
            request.Description,
            _currentUser.UserId,
            request.ReferenceType,
            request.ReferenceId);

        await _cash.AddMovementAsync(movement, ct);
        _audit.Stage(AuditActions.CashMovement, AuditSeverity.Info, "CashSession", sessionId.ToString(),
            $"{(movementType == CashMovementType.Deposit ? "Suprimento" : "Sangria")} de {movement.Amount:F2}: {movement.Description}",
            tenantId: _currentTenant.Id, actorId: _currentUser.UserId, actorName: _currentUser.Name,
            metadata: new { MovementType = movementType.ToString(), movement.Amount });
        await _cash.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return MapMovementToDto(movement);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private async Task<CashSessionDto> MapWithSummaryAsync(CashSession s, bool includeMovements, CancellationToken ct)
    {
        var movements = await _cash.GetMovementsBySessionAsync(s.Id, ct);
        var payments  = await _cash.GetPaymentTotalsBySessionAsync(s.Id, ct);
        return MapToDto(s, includeMovements ? movements : null, Summary(s, movements, payments));
    }

    private static CashSessionSummaryDto Summary(
        CashSession s, IReadOnlyList<CashMovement> movements, IReadOnlyList<CashPaymentTotalDto> payments)
    {
        decimal Sum(CashMovementType type) => movements.Where(m => m.MovementType == type).Sum(m => m.Amount);
        return new CashSessionSummaryDto(
            OpeningBalance:  s.OpeningBalance,
            CashSales:       Sum(CashMovementType.SaleReceipt),
            Deposits:        Sum(CashMovementType.Deposit),
            Withdrawals:     Sum(CashMovementType.Withdrawal),
            Refunds:         Sum(CashMovementType.SaleRefund),
            ExpectedBalance: s.IsOpen ? CashSession.ExpectedBalanceFrom(s.OpeningBalance, movements) : s.ExpectedBalance ?? 0,
            ReceiptsByMethod: payments);
    }

    private static CashSessionDto MapToDto(CashSession s, IReadOnlyList<CashMovement>? movements, CashSessionSummaryDto? summary) => new(
        s.Id,
        s.Status.ToString(),
        s.OpenedByUserId,
        s.OpenedBy?.FullName ?? string.Empty,
        s.ClosedByUserId,
        s.ClosedBy?.FullName,
        s.OpeningBalance,
        s.ClosingBalance,
        s.OpenedAt,
        s.ClosedAt,
        s.Notes,
        movements?.Select(MapMovementToDto).ToList(),
        s.ExpectedBalance,
        s.Difference,
        s.ClosingNotes,
        summary);

    private static CashMovementDto MapMovementToDto(CashMovement m) => new(
        m.Id, m.MovementType.ToString(), m.Amount, m.Description,
        m.ReferenceType, m.ReferenceId, m.CreatedByUserId, m.CreatedAt);
}
