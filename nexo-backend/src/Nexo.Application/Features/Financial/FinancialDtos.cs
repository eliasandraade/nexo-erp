namespace Nexo.Application.Features.Financial;

// ── Requests ────────────────────────────────────────────────────────────────

public record CreateFinancialAccountRequest(
    string Code,
    string Name,
    string AccountType,         // "Revenue" | "Expense" | "Asset" | "Liability" | "Equity"
    Guid? ParentAccountId = null);

public record UpdateFinancialAccountRequest(
    string Code,
    string Name,
    Guid? ParentAccountId);

public record CreateTransactionRequest(
    Guid FinancialAccountId,
    string TransactionType,     // "Receivable" | "Payable"
    decimal Amount,
    string Description,
    DateTime DueDate,
    string? ReferenceType = null,
    Guid? ReferenceId = null);

public record UpdateTransactionRequest(
    decimal Amount,
    string Description,
    DateTime DueDate);

public record MarkTransactionPaidRequest(DateTime? PaidAt = null);

// ── Responses ───────────────────────────────────────────────────────────────

public record FinancialAccountDto(
    Guid Id,
    string Code,
    string Name,
    string AccountType,
    Guid? ParentAccountId,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record FinancialTransactionDto(
    Guid Id,
    Guid FinancialAccountId,
    string AccountName,
    string TransactionType,
    decimal Amount,
    string Description,
    DateTime DueDate,
    DateTime? PaidAt,
    string Status,
    string? ReferenceType,
    Guid? ReferenceId,
    Guid CreatedByUserId,
    DateTime CreatedAt,
    DateTime UpdatedAt);
