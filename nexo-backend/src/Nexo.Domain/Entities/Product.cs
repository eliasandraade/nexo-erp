using Nexo.Domain.Common;
using Nexo.Domain.Enums;

namespace Nexo.Domain.Entities;

public class Product : TenantEntity
{
    private Product() { }
    private Product(Guid tenantId) : base(tenantId) { }

    public string Code { get; private set; } = string.Empty;       // SKU interno, único por tenant
    public string? Barcode { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? CategoryId { get; private set; }
    public ProductUnit Unit { get; private set; }
    public decimal CostPrice { get; private set; }
    public decimal SalePrice { get; private set; }
    public bool TrackStock { get; private set; }                    // false para serviços
    public decimal? MinStockQuantity { get; private set; }
    public decimal? MaxStockQuantity { get; private set; }
    public bool IsActive { get; private set; }

    // Navigation
    public Category? Category { get; private set; }
    public StockItem? StockItem { get; private set; }
    public ICollection<SaleItem> SaleItems { get; private set; } = [];
    public ICollection<StockMovement> StockMovements { get; private set; } = [];

    public static Product Create(
        Guid tenantId,
        string code,
        string name,
        ProductUnit unit,
        decimal salePrice,
        decimal costPrice = 0,
        string? barcode = null,
        string? description = null,
        Guid? categoryId = null,
        bool trackStock = true,
        decimal? minStockQuantity = null,
        decimal? maxStockQuantity = null)
    {
        return new Product(tenantId)
        {
            Code             = code.Trim().ToUpperInvariant(),
            Barcode          = barcode?.Trim(),
            Name             = name.Trim(),
            Description      = description?.Trim(),
            CategoryId       = categoryId,
            Unit             = unit,
            CostPrice        = costPrice,
            SalePrice        = salePrice,
            TrackStock       = trackStock,
            MinStockQuantity = minStockQuantity,
            MaxStockQuantity = maxStockQuantity,
            IsActive         = true,
        };
    }

    public void Update(
        string name,
        string? barcode,
        string? description,
        Guid? categoryId,
        ProductUnit unit,
        decimal costPrice,
        decimal salePrice,
        bool trackStock,
        decimal? minStockQuantity,
        decimal? maxStockQuantity)
    {
        Name             = name.Trim();
        Barcode          = barcode?.Trim();
        Description      = description?.Trim();
        CategoryId       = categoryId;
        Unit             = unit;
        CostPrice        = costPrice;
        SalePrice        = salePrice;
        TrackStock       = trackStock;
        MinStockQuantity = minStockQuantity;
        MaxStockQuantity = maxStockQuantity;
        SetUpdatedAt();
    }

    public void UpdatePrices(decimal costPrice, decimal salePrice)
    {
        CostPrice = costPrice;
        SalePrice = salePrice;
        SetUpdatedAt();
    }

    public void Deactivate() { IsActive = false; SetUpdatedAt(); }
    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
}
