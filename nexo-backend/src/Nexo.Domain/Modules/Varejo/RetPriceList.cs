using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Varejo;

/// <summary>
/// Lista de preços por tenant.
/// Permite preços diferenciados por cliente, tipo de cliente ou canal de venda.
///
/// Resolução de preço (servidor — a venda nunca confia no preço enviado pelo cliente):
///   candidatas = lista vinculada ao cliente + lista padrão (IsDefault), só as ativas, dentro da
///   vigência e que tenham o produto; vence a de maior Priority (empate: a do cliente);
///   sem candidata, product.SalePrice.
/// </summary>
public class RetPriceList : TenantEntity
{
    private RetPriceList() { }
    private RetPriceList(Guid tenantId) : base(tenantId) { }

    public string Name        { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>
    /// Indica que esta é a lista padrão do tenant.
    /// Só pode existir uma por tenant. Aplicada quando o cliente não tem lista vinculada.
    /// </summary>
    public bool IsDefault  { get; private set; }
    public bool IsActive   { get; private set; }
    /// <summary>Vigência (UTC). Null = sem limite naquele lado.</summary>
    public DateTime? ValidFrom  { get; private set; }
    public DateTime? ValidUntil { get; private set; }
    /// <summary>Maior vence quando o cliente tem lista e há lista padrão (ex.: promoção padrão &gt; tabela do cliente).</summary>
    public int Priority { get; private set; }

    public bool IsValidAt(DateTime utcNow)
        => IsActive && (ValidFrom is null || ValidFrom <= utcNow) && (ValidUntil is null || utcNow <= ValidUntil);

    // Navigation
    private readonly List<RetPriceListItem> _items = [];
    public IReadOnlyList<RetPriceListItem> Items => _items.AsReadOnly();

    // ── Factory ───────────────────────────────────────────────────────────────

    public static RetPriceList Create(
        Guid tenantId,
        string name,
        string? description = null,
        bool isDefault = false,
        DateTime? validFrom = null,
        DateTime? validUntil = null,
        int priority = 0)
    {
        var list = new RetPriceList(tenantId)
        {
            IsDefault = isDefault,
            IsActive  = true,
        };
        list.Update(name, description, validFrom, validUntil, priority);
        return list;
    }

    // ── Mutation ──────────────────────────────────────────────────────────────

    public void Update(string name, string? description, DateTime? validFrom = null, DateTime? validUntil = null, int priority = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Informe o nome da lista de preço.");
        if (validFrom.HasValue && validUntil.HasValue && validUntil < validFrom)
            throw new DomainException("O fim da vigência deve ser depois do início.");

        Name        = name.Trim();
        Description = description?.Trim();
        ValidFrom   = Utc(validFrom);
        ValidUntil  = Utc(validUntil);
        Priority    = priority;
        SetUpdatedAt();
    }

    private static DateTime? Utc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Unspecified } v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
        { } v => v.ToUniversalTime(),
    };

    public void SetAsDefault()
    {
        IsDefault = true;
        SetUpdatedAt();
    }

    public void UnsetDefault()
    {
        IsDefault = false;
        SetUpdatedAt();
    }

    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
    public void Deactivate() { IsActive = false; SetUpdatedAt(); }

    // ── Items ─────────────────────────────────────────────────────────────────

    public RetPriceListItem SetProductPrice(Guid tenantId, Guid productId, decimal price)
    {
        if (price < 0)
            throw new DomainException("Price cannot be negative.");
        if (decimal.Round(price, 2) != price)
            throw new DomainException("O preço tem no máximo duas casas decimais.");

        var existing = _items.FirstOrDefault(x => x.ProductId == productId);
        if (existing is not null)
        {
            existing.UpdatePrice(price);
            return existing;
        }

        var item = RetPriceListItem.Create(tenantId, Id, productId, price);
        _items.Add(item);
        return item;
    }

    public void RemoveProductPrice(Guid productId)
    {
        var item = _items.FirstOrDefault(x => x.ProductId == productId);
        if (item is not null)
            _items.Remove(item);
    }
}
