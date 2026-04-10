namespace Nexo.Application.Features.Cash;

// ── Requests ────────────────────────────────────────────────────────────────

public record OpenCashSessionRequest(
    decimal OpeningBalance,
    string? Notes = null);

public record CloseCashSessionRequest(decimal ClosingBalance);

public record AddCashMovementRequest(
    string MovementType,        // "SaleIncome" | "ManualEntry" | "ManualWithdrawal" | "Expense" | "OpeningBalance" | "ClosingBalance"
    decimal Amount,
    string Description,
    string? ReferenceType = null,
    Guid? ReferenceId = null);

// ── Responses ───────────────────────────────────────────────────────────────

public record CashMovementDto(
    Guid Id,
    string MovementType,
    decimal Amount,
    string Description,
    string? ReferenceType,
    Guid? ReferenceId,
    Guid CreatedByUserId,
    DateTime CreatedAt);

public record CashSessionDto(
    Guid Id,
    string Status,
    Guid OpenedByUserId,
    string OpenedByName,
    Guid? ClosedByUserId,
    string? ClosedByName,
    decimal OpeningBalance,
    decimal? ClosingBalance,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    string? Notes,
    IReadOnlyList<CashMovementDto>? Movements);
