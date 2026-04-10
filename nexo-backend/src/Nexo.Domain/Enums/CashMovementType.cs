namespace Nexo.Domain.Enums;

public enum CashMovementType
{
    Opening,        // valor inicial ao abrir o caixa
    SaleReceipt,    // recebimento de venda
    Withdrawal,     // sangria
    Deposit,        // suprimento
    Closing,        // valor final ao fechar o caixa
}
