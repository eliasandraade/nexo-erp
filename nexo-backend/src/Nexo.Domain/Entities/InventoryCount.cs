using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Entities;

public enum InventoryCountStatus { Open, Applied, Cancelled }

/// <summary>
/// Inventário (contagem física) of one store. Opening it snapshots what the system expects per
/// product; the team types what was counted; applying it adjusts each product to the counted
/// quantity — the difference is computed against the stock AT APPLY TIME (sales made while counting
/// are not undone) and every change is an Adjustment movement referencing the count.
/// </summary>
public class InventoryCount : StoreEntity
{
    private InventoryCount() { }
    private InventoryCount(Guid tenantId) : base(tenantId) { }

    private readonly List<InventoryCountItem> _items = [];

    public int Number { get; private set; }
    public InventoryCountStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? ClosedByUserId { get; private set; }
    public DateTime? ClosedAt { get; private set; }

    public IReadOnlyCollection<InventoryCountItem> Items => _items;

    public static InventoryCount Open(Guid tenantId, int number, Guid createdByUserId, string? notes)
    {
        if (notes is { Length: > 500 })
            throw new DomainException("As observações do inventário têm no máximo 500 caracteres.");

        return new InventoryCount(tenantId)
        {
            Number          = number,
            Status          = InventoryCountStatus.Open,
            Notes           = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedByUserId = createdByUserId,
        };
    }

    public InventoryCountItem AddProduct(Guid productId, decimal expectedQuantity)
    {
        EnsureOpen();
        if (_items.Any(i => i.ProductId == productId))
            throw new DomainException("O produto já está neste inventário.");
        var item = InventoryCountItem.Create(TenantId, Id, productId, expectedQuantity);
        _items.Add(item);
        return item;
    }

    public void Count(Guid productId, decimal? counted)
    {
        EnsureOpen();
        var item = _items.FirstOrDefault(i => i.ProductId == productId)
            ?? throw new DomainException("Produto fora deste inventário.");
        item.SetCounted(counted);
        SetUpdatedAt();
    }

    /// <summary>Freezes the system quantity at apply time and the difference of every counted item.</summary>
    public void MarkApplied(Guid userId, IReadOnlyDictionary<Guid, decimal> systemQuantities)
    {
        EnsureOpen();
        if (!_items.Any(i => i.CountedQuantity.HasValue))
            throw new DomainException("Informe a contagem de ao menos um produto antes de aplicar.");

        foreach (var item in _items.Where(i => i.CountedQuantity.HasValue))
            item.Freeze(systemQuantities.GetValueOrDefault(item.ProductId));

        Status         = InventoryCountStatus.Applied;
        ClosedByUserId = userId;
        ClosedAt       = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void Cancel(Guid userId)
    {
        EnsureOpen();
        Status         = InventoryCountStatus.Cancelled;
        ClosedByUserId = userId;
        ClosedAt       = DateTime.UtcNow;
        SetUpdatedAt();
    }

    private void EnsureOpen()
    {
        if (Status != InventoryCountStatus.Open)
            throw new DomainException("Este inventário já foi encerrado.");
    }
}

public class InventoryCountItem : TenantEntity
{
    private InventoryCountItem() { }
    private InventoryCountItem(Guid tenantId) : base(tenantId) { }

    public Guid InventoryCountId { get; private set; }
    public Guid ProductId { get; private set; }
    /// <summary>System quantity when the count was opened (reference for the team).</summary>
    public decimal ExpectedQuantity { get; private set; }
    public decimal? CountedQuantity { get; private set; }
    /// <summary>System quantity when the count was applied — the base of the adjustment.</summary>
    public decimal? SystemQuantityAtApply { get; private set; }
    /// <summary>Counted − system at apply (positive = sobra, negative = falta).</summary>
    public decimal? Difference { get; private set; }

    internal static InventoryCountItem Create(Guid tenantId, Guid countId, Guid productId, decimal expected)
        => new(tenantId)
        {
            InventoryCountId = countId,
            ProductId        = productId,
            ExpectedQuantity = expected,
        };

    internal void SetCounted(decimal? counted)
    {
        if (counted is < 0)
            throw new DomainException("A quantidade contada não pode ser negativa.");
        if (counted is { } c && decimal.Round(c, 4) != c)
            throw new DomainException("A quantidade contada tem no máximo 4 casas decimais.");
        CountedQuantity = counted;
        SetUpdatedAt();
    }

    internal void Freeze(decimal systemQuantity)
    {
        SystemQuantityAtApply = systemQuantity;
        Difference            = CountedQuantity!.Value - systemQuantity;
        SetUpdatedAt();
    }
}
