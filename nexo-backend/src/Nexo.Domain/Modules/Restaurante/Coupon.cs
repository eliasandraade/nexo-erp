using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

public enum CouponDiscountType { Percentage, FixedAmount, DeliveryFee }

public class Coupon : StoreEntity
{
    private Coupon() { }
    private Coupon(Guid tenantId) : base(tenantId) { }

    public string             Code           { get; private set; } = string.Empty;
    public string?            Description    { get; private set; }
    public CouponDiscountType DiscountType   { get; private set; }
    public decimal            DiscountValue  { get; private set; }
    public bool               IsActive       { get; private set; } = true;

    // Conditions (all optional)
    public decimal? MinOrderAmount          { get; private set; }
    public decimal? MinDeliveryFee          { get; private set; }
    public string?  RestrictToNeighborhoods { get; private set; } // jsonb: string[]
    public string?  RestrictToProductIds    { get; private set; } // jsonb: Guid[]
    public bool     IsFirstOrderOnly        { get; private set; }
    public string?  RestrictToCustomerPhone { get; private set; }
    public int?     MaxUses                 { get; private set; }
    public int      UsedCount               { get; private set; }
    public DateTime? ValidFrom              { get; private set; }
    public DateTime? ValidUntil             { get; private set; }

    public static Coupon Create(
        Guid               tenantId,
        string             code,
        CouponDiscountType discountType,
        decimal            discountValue,
        string?            description             = null,
        decimal?           minOrderAmount          = null,
        decimal?           minDeliveryFee          = null,
        string?            restrictToNeighborhoods = null,
        string?            restrictToProductIds    = null,
        bool               isFirstOrderOnly        = false,
        string?            restrictToCustomerPhone = null,
        int?               maxUses                 = null,
        DateTime?          validFrom               = null,
        DateTime?          validUntil              = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Código do cupom é obrigatório.");
        if (discountValue <= 0)
            throw new DomainException("O valor do desconto deve ser positivo.");
        if (discountType == CouponDiscountType.Percentage && discountValue > 100)
            throw new DomainException("Desconto em porcentagem não pode exceder 100%.");

        return new Coupon(tenantId)
        {
            Code                    = code.Trim().ToUpperInvariant(),
            Description             = description?.Trim(),
            DiscountType            = discountType,
            DiscountValue           = discountValue,
            MinOrderAmount          = minOrderAmount,
            MinDeliveryFee          = minDeliveryFee,
            RestrictToNeighborhoods = restrictToNeighborhoods,
            RestrictToProductIds    = restrictToProductIds,
            IsFirstOrderOnly        = isFirstOrderOnly,
            RestrictToCustomerPhone = restrictToCustomerPhone?.Trim(),
            MaxUses                 = maxUses,
            ValidFrom               = validFrom,
            ValidUntil              = validUntil,
        };
    }

    public void Update(
        CouponDiscountType discountType,
        decimal            discountValue,
        string?            description             = null,
        decimal?           minOrderAmount          = null,
        decimal?           minDeliveryFee          = null,
        string?            restrictToNeighborhoods = null,
        string?            restrictToProductIds    = null,
        bool               isFirstOrderOnly        = false,
        string?            restrictToCustomerPhone = null,
        int?               maxUses                 = null,
        DateTime?          validFrom               = null,
        DateTime?          validUntil              = null)
    {
        if (discountValue <= 0)
            throw new DomainException("O valor do desconto deve ser positivo.");
        if (discountType == CouponDiscountType.Percentage && discountValue > 100)
            throw new DomainException("Desconto em porcentagem não pode exceder 100%.");

        DiscountType            = discountType;
        DiscountValue           = discountValue;
        Description             = description?.Trim();
        MinOrderAmount          = minOrderAmount;
        MinDeliveryFee          = minDeliveryFee;
        RestrictToNeighborhoods = restrictToNeighborhoods;
        RestrictToProductIds    = restrictToProductIds;
        IsFirstOrderOnly        = isFirstOrderOnly;
        RestrictToCustomerPhone = restrictToCustomerPhone?.Trim();
        MaxUses                 = maxUses;
        ValidFrom               = validFrom;
        ValidUntil              = validUntil;
        SetUpdatedAt();
    }

    public void Revoke()
    {
        IsActive = false;
        SetUpdatedAt();
    }

    public void IncrementUsedCount()
    {
        UsedCount++;
        SetUpdatedAt();
    }

    /// <summary>
    /// Validates all conditions and returns the discount amount to deduct.
    /// Throws DomainException with user-facing Portuguese message on any violation.
    /// </summary>
    public decimal CalculateDiscount(
        decimal itemsSubtotal,
        decimal deliveryFee,
        string  customerPhone,
        string? neighborhood,
        bool    isFirstOrder)
    {
        var now = DateTime.UtcNow;

        if (!IsActive)
            throw new DomainException("Cupom inválido ou inativo.");
        if (ValidFrom.HasValue && now < ValidFrom.Value)
            throw new DomainException("Este cupom ainda não está válido.");
        if (ValidUntil.HasValue && now > ValidUntil.Value)
            throw new DomainException("Este cupom expirou.");
        if (MaxUses.HasValue && UsedCount >= MaxUses.Value)
            throw new DomainException("Este cupom atingiu o limite de usos.");
        if (MinOrderAmount.HasValue && itemsSubtotal < MinOrderAmount.Value)
            throw new DomainException($"Pedido mínimo de R$ {MinOrderAmount.Value:F2} para este cupom.");
        if (MinDeliveryFee.HasValue && deliveryFee < MinDeliveryFee.Value)
            throw new DomainException($"Taxa de entrega mínima de R$ {MinDeliveryFee.Value:F2} para este cupom.");
        if (IsFirstOrderOnly && !isFirstOrder)
            throw new DomainException("Este cupom é válido apenas para o primeiro pedido.");
        if (RestrictToCustomerPhone is not null &&
            NormalizePhone(RestrictToCustomerPhone) != NormalizePhone(customerPhone))
            throw new DomainException("Este cupom não é válido para este cliente.");

        if (RestrictToNeighborhoods is not null)
        {
            var allowed = System.Text.Json.JsonSerializer
                .Deserialize<string[]>(RestrictToNeighborhoods) ?? [];
            if (neighborhood is null || !allowed.Contains(neighborhood, StringComparer.OrdinalIgnoreCase))
                throw new DomainException("Este cupom não é válido para o seu bairro.");
        }

        return DiscountType switch
        {
            CouponDiscountType.Percentage  => Math.Round(itemsSubtotal * DiscountValue / 100m, 2),
            CouponDiscountType.FixedAmount => Math.Min(DiscountValue, itemsSubtotal),
            CouponDiscountType.DeliveryFee => Math.Min(DiscountValue, deliveryFee),
            _                              => 0m,
        };
    }

    private static string NormalizePhone(string phone)
        => new string(phone.Where(char.IsDigit).ToArray());
}
