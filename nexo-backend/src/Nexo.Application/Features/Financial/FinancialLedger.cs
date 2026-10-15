using Nexo.Domain.Enums;

namespace Nexo.Application.Features.Financial;

/// <summary>
/// Os dois regimes do livro, sobre os mesmos lançamentos:
///   Accrual (competência) — pela data do fato, todo lançamento não cancelado (em aberto conta);
///   Cash (caixa)          — pela data do pagamento, só o que foi quitado.
/// </summary>
public enum LedgerRegime { Accrual, Cash }

public record LedgerFilter(
    LedgerRegime Regime,
    DateTime From,              // inclusive, UTC
    DateTime To,                // exclusive, UTC
    Guid? StoreId = null,
    FinancialCategory? Category = null);

public record LedgerTotalRow(FinancialCategory Category, TransactionType Type, decimal Amount, int Count);

public record LedgerEntryDto(
    Guid Id,
    string Direction,           // "In" | "Out"
    string Category,
    decimal Amount,
    string Description,
    DateTime CompetenceDate,
    DateTime DueDate,
    DateTime? PaidAt,
    string Status,
    Guid? StoreId,
    string? ReferenceType,
    Guid? ReferenceId,
    Guid? ReversesTransactionId);

public record LedgerCategoryTotalDto(string Category, decimal In, decimal Out, decimal Net, int Count);

public record LedgerDto(
    string Regime,
    DateTime From,
    DateTime To,
    decimal TotalIn,
    decimal TotalOut,
    decimal Net,                // entradas − saídas
    decimal Result,             // competência: Net without compras de mercadoria; caixa: = Net
    IReadOnlyList<LedgerCategoryTotalDto> ByCategory,
    IReadOnlyList<LedgerEntryDto> Entries,
    bool Truncated);
