using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Entities;

/// <summary>
/// Transferência de estoque between two stores of the same tenant, applied atomically: the
/// origin's stock goes down (TransferOut) and the destination's goes up (TransferIn) in one
/// transaction, both movements referencing this record.
/// </summary>
public class StockTransfer : TenantEntity
{
    private StockTransfer() { }
    private StockTransfer(Guid tenantId) : base(tenantId) { }

    private readonly List<StockTransferItem> _items = [];

    public int Number { get; private set; }
    public Guid FromStoreId { get; private set; }
    public Guid ToStoreId { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    public IReadOnlyCollection<StockTransferItem> Items => _items;

    public static StockTransfer Create(
        Guid tenantId, int number, Guid fromStoreId, Guid toStoreId, Guid createdByUserId, string? notes)
    {
        if (fromStoreId == Guid.Empty || toStoreId == Guid.Empty)
            throw new DomainException("Informe a loja de origem e a de destino.");
        if (fromStoreId == toStoreId)
            throw new DomainException("A loja de destino deve ser diferente da origem.");
        if (notes is { Length: > 500 })
            throw new DomainException("As observações da transferência têm no máximo 500 caracteres.");

        return new StockTransfer(tenantId)
        {
            Number          = number,
            FromStoreId     = fromStoreId,
            ToStoreId       = toStoreId,
            CreatedByUserId = createdByUserId,
            Notes           = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
    }

    public StockTransferItem AddItem(Guid productId, decimal quantity)
    {
        if (quantity <= 0 || decimal.Round(quantity, 4) != quantity)
            throw new DomainException("A quantidade transferida deve ser positiva, com até 4 casas decimais.");
        if (_items.Any(i => i.ProductId == productId))
            throw new DomainException("O produto aparece mais de uma vez na transferência.");
        var item = StockTransferItem.Create(TenantId, Id, productId, quantity);
        _items.Add(item);
        return item;
    }
}

public class StockTransferItem : TenantEntity
{
    private StockTransferItem() { }
    private StockTransferItem(Guid tenantId) : base(tenantId) { }

    public Guid StockTransferId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }

    internal static StockTransferItem Create(Guid tenantId, Guid transferId, Guid productId, decimal quantity)
        => new(tenantId) { StockTransferId = transferId, ProductId = productId, Quantity = quantity };
}
