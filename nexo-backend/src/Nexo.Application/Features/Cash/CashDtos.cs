namespace Nexo.Application.Features.Cash;

// ── Requests ────────────────────────────────────────────────────────────────

public record OpenCashSessionRequest(
    decimal OpeningBalance,
    string? Notes = null);

/// <summary>ClosingBalance = what was counted in the drawer; Notes explain a difference.</summary>
public record CloseCashSessionRequest(decimal ClosingBalance, string? Notes = null);

public record AddCashMovementRequest(
    string MovementType,        // "Deposit" (suprimento) | "Withdrawal" (sangria) — the only manual kinds
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
    IReadOnlyList<CashMovementDto>? Movements,
    decimal? ExpectedBalance = null,    // frozen at closing
    decimal? Difference = null,         // counted − expected (closed sessions)
    string? ClosingNotes = null,
    CashSessionSummaryDto? Summary = null);

/// <summary>Drawer totals of a session and what was received in it by payment method.</summary>
public record CashSessionSummaryDto(
    decimal OpeningBalance,
    decimal CashSales,
    decimal Deposits,
    decimal Withdrawals,
    decimal Refunds,
    decimal ExpectedBalance,
    IReadOnlyList<CashPaymentTotalDto> ReceiptsByMethod);

/// <summary>Sale payments of the session grouped by method (Pix/card included — they never enter the drawer).</summary>
public record CashPaymentTotalDto(string Method, string Type, int Count, decimal Amount);
