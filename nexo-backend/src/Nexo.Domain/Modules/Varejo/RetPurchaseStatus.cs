namespace Nexo.Domain.Modules.Varejo;

public enum RetPurchaseStatus
{
    Draft,      // pedido em elaboração (itens editáveis)
    Confirmed,  // recebida: estoque, custo e contas a pagar lançados
    Cancelled,
    Ordered,    // pedido enviado ao fornecedor, aguardando entrega (itens congelados)
}
