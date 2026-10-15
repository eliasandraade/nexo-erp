using Nexo.Domain.Enums;

namespace Nexo.Application.Features.Financial;

// ── Baixa (settlement) ───────────────────────────────────────────────────────

/// <summary>
/// Baixa de um título. <see cref="Amount"/> is the money that actually moved; juros and multa come on
/// top of it as their own lançamentos; <see cref="Discount"/> extinguishes part of the título without
/// money. The título is settled by Amount + Discount — less than its balance splits it.
/// </summary>
public record SettleTransactionRequest(
    decimal Amount,
    DateTime? PaidAt = null,
    decimal Interest = 0,
    decimal Fine = 0,
    decimal Discount = 0);

public record SettlementResultDto(
    FinancialTransactionDto Settled,
    FinancialTransactionDto? Remainder,
    IReadOnlyList<FinancialTransactionDto> Adjustments);

// ── Títulos (contas a receber / a pagar) ─────────────────────────────────────

public enum TitleStatusFilter { Open, Overdue, Settled, All }

public record TitlesFilter(
    TransactionType Type,
    TitleStatusFilter Status,
    DateTime? DueFrom = null,
    DateTime? DueTo = null,
    Guid? CustomerId = null,
    Guid? SupplierId = null);

public record TitleDto(
    Guid Id,
    string Description,
    Guid? CustomerId,
    string? CustomerName,
    Guid? SupplierId,
    string? SupplierName,
    decimal Amount,
    DateTime DueDate,
    DateTime CompetenceDate,
    DateTime? PaidAt,
    string Status,              // Pending | Overdue | Paid | Cancelled (Overdue also when the job hasn't run yet)
    int DaysOverdue,
    string Category,
    Guid? StoreId,
    string? ReferenceType,
    Guid? ReferenceId,
    Guid? ParentTransactionId);

/// <summary>Open títulos by due-date bucket (overdue = due before today, Brazil time).</summary>
public record AgingDto(
    decimal NotDue,
    decimal Overdue1To30,
    decimal Overdue31To60,
    decimal OverdueOver60,
    decimal TotalOpen,
    int OpenCount,
    int OverdueCount);

public record TitlesDto(IReadOnlyList<TitleDto> Items, AgingDto Aging, bool Truncated);

// ── Parcelamento ─────────────────────────────────────────────────────────────

public record CreateInstallmentsRequest(
    string TransactionType,         // "Receivable" | "Payable"
    decimal TotalAmount,
    string Description,
    DateTime FirstDueDate,
    int Installments = 1,           // monthly, 1..60
    string? Category = null,
    Guid? CustomerId = null,
    Guid? StoreId = null,
    DateTime? CompetenceDate = null,    // default: each installment's due date
    Guid? SupplierId = null,
    Guid? FinancialAccountId = null);   // default: the active Contas a Receber / a Pagar

/// <summary>Row used to compute the aging of open títulos.</summary>
public record OpenTitleRow(DateTime DueDate, decimal Amount);

// ── Recorrências ─────────────────────────────────────────────────────────────

public record CreateRecurrenceRequest(
    string TransactionType,         // "Payable" | "Receivable"
    string Description,
    decimal Amount,
    int DayOfMonth,                 // 1..31 (month's last day when shorter)
    DateOnly StartMonth,
    DateOnly? EndMonth = null,
    string? Category = null,
    Guid? SupplierId = null,
    Guid? CustomerId = null,
    Guid? StoreId = null);

public record UpdateRecurrenceRequest(string Description, decimal Amount, int DayOfMonth, DateOnly? EndMonth = null);

public record RecurrenceDto(
    Guid Id,
    string TransactionType,
    string Category,
    string Description,
    decimal Amount,
    int DayOfMonth,
    DateOnly StartMonth,
    DateOnly? EndMonth,
    DateOnly? GeneratedThrough,
    bool IsActive,
    Guid? SupplierId,
    string? SupplierName,
    Guid? CustomerId,
    Guid? StoreId);
