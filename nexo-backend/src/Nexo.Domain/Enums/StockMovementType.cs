namespace Nexo.Domain.Enums;

public enum StockMovementType
{
    ManualEntry,    // entrada manual
    ManualExit,     // saída manual
    Adjustment,     // ajuste de inventário
    SaleOutput,     // saída por venda
    ReturnEntry,    // entrada por devolução
    PurchaseEntry,  // entrada por compra
    Transfer,       // transferência entre locais (futuro)
    Loss,           // perda / vencimento
    RecipeOutput,   // consumo de ingrediente por ficha técnica (módulo Restaurante)
    TransferOut,    // saída por transferência para outra loja
    TransferIn,     // entrada por transferência de outra loja
    PurchaseReversal, // saída por cancelamento de compra recebida
}
