namespace Nexo.Domain.Enums;

public enum CashMovementType
{
    Opening,        // valor inicial ao abrir o caixa (o saldo de abertura fica na sessão)
    SaleReceipt,    // recebimento de venda em dinheiro (líquido do troco)
    Withdrawal,     // sangria
    Deposit,        // suprimento
    Closing,        // valor final ao fechar o caixa
    SaleRefund,     // dinheiro devolvido ao cancelar uma venda
}

public static class CashMovementTypeExtensions
{
    /// <summary>Money entering the drawer.</summary>
    public static bool IsInflow(this CashMovementType type)
        => type is CashMovementType.SaleReceipt or CashMovementType.Deposit;

    /// <summary>Money leaving the drawer.</summary>
    public static bool IsOutflow(this CashMovementType type)
        => type is CashMovementType.Withdrawal or CashMovementType.SaleRefund;

    /// <summary>Movement kinds an operator may record by hand: suprimento and sangria only.</summary>
    public static bool IsManual(this CashMovementType type)
        => type is CashMovementType.Deposit or CashMovementType.Withdrawal;
}
