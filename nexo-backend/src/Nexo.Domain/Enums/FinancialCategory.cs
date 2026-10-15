namespace Nexo.Domain.Enums;

/// <summary>
/// Linha gerencial de um lançamento — o "o quê" do dinheiro; a direção (entrada/saída) vem do
/// <see cref="TransactionType"/>. É o que separa receita de estorno, compra de despesa.
///
///   Revenue           — vendas e serviços (entrada)
///   RevenueDeduction  — devoluções, cancelamentos e estornos de receita (saída)
///   Purchase          — compras de mercadoria; vão para o estoque, então no regime de competência
///                       não são despesa (o custo entra pelo CMV da venda); no caixa são saída
///   OperatingExpense  — despesas operacionais (aluguel, energia, manutenção…)
///   Payroll           — folha e encargos
///   Commission        — comissões e repasses
///   Tax               — impostos e taxas públicas
///   FinancialExpense  — juros, multas, tarifas bancárias e de cartão
///   OtherIncome       — outras entradas
///   OtherExpense      — outras saídas
/// </summary>
public enum FinancialCategory
{
    Revenue,
    RevenueDeduction,
    Purchase,
    OperatingExpense,
    Payroll,
    Commission,
    Tax,
    FinancialExpense,
    OtherIncome,
    OtherExpense,
}

public static class FinancialCategoryRules
{
    /// <summary>Categories a person may choose for a manual lançamento of the given direction.</summary>
    public static bool IsAllowedForManualEntry(this FinancialCategory category, TransactionType type)
        => type == TransactionType.Receivable
            ? category is FinancialCategory.Revenue or FinancialCategory.OtherIncome
            : category is FinancialCategory.OperatingExpense or FinancialCategory.Payroll
                or FinancialCategory.Commission or FinancialCategory.Tax
                or FinancialCategory.FinancialExpense or FinancialCategory.OtherExpense
                or FinancialCategory.Purchase;

    public static FinancialCategory DefaultFor(TransactionType type)
        => type == TransactionType.Receivable ? FinancialCategory.OtherIncome : FinancialCategory.OperatingExpense;
}
