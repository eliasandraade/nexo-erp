using Nexo.Domain.Common;
using Nexo.Domain.Enums;

namespace Nexo.Domain.Entities;

public class Product : StoreEntity
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
    public bool IsMenuVisible { get; private set; }                 // exibe no portal público
    public bool IsIngredient { get; private set; }                  // true = insumo de estoque do restaurante
    public string? ImageUrl { get; private set; }                   // URL externa de imagem do produto

    // Dados fiscais (PR 66) — informados pelo contador; nada aqui calcula imposto
    public string? Ncm { get; private set; }                        // 8 dígitos
    public string? Cest { get; private set; }                       // 7 dígitos, quando há substituição tributária
    public int? FiscalOrigin { get; private set; }                  // origem da mercadoria, 0–8 (tabela da NF-e)
    public string? Cfop { get; private set; }                       // CFOP padrão de venda, 4 dígitos
    public string? TaxSituation { get; private set; }               // CST (2 dígitos) ou CSOSN (3 dígitos) do ICMS

    // Navigation
    public Category? Category { get; private set; }
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
        decimal? maxStockQuantity = null,
        bool isIngredient = false)
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
            IsMenuVisible    = true,
            IsIngredient     = isIngredient,
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

    public void SetMenuVisibility(bool visible) { IsMenuVisible = visible; SetUpdatedAt(); }
    public void SetIsIngredient(bool value)     { IsIngredient = value;    SetUpdatedAt(); }
    public void SetImageUrl(string? url)        { ImageUrl = url?.Trim();  SetUpdatedAt(); }

    public void Deactivate() { IsActive = false; SetUpdatedAt(); }
    public void Activate()   { IsActive = true;  SetUpdatedAt(); }

    /// <summary>Fiscal data as typed by the accountant; empty strings clear a field.</summary>
    public void UpdateFiscal(string? ncm, string? cest, int? origin, string? cfop, string? taxSituation)
    {
        Ncm          = Clean(ncm);
        Cest         = Clean(cest);
        FiscalOrigin = origin;
        Cfop         = Clean(cfop);
        TaxSituation = Clean(taxSituation);
        SetUpdatedAt();

        static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}
