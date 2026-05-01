using System.Text.Json;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante;

public class CouponService
{
    private readonly ICouponRepository _repo;
    private readonly ICurrentTenant    _currentTenant;
    private readonly IStoreRepository  _stores;

    public CouponService(
        ICouponRepository repo,
        ICurrentTenant    currentTenant,
        IStoreRepository  stores)
    {
        _repo          = repo;
        _currentTenant = currentTenant;
        _stores        = stores;
    }

    public async Task<IReadOnlyList<CouponDto>> GetAllAsync(CancellationToken ct = default)
    {
        var coupons = await _repo.GetAllAsync(ct);
        return coupons.Select(Map).ToList();
    }

    public async Task<CouponDto> CreateAsync(
        CreateCouponRequest req, CancellationToken ct = default)
    {
        var discountType = Enum.Parse<CouponDiscountType>(req.DiscountType, ignoreCase: true);
        var coupon = Coupon.Create(
            _currentTenant.Id,
            req.Code,
            discountType,
            req.DiscountValue,
            req.Description,
            req.MinOrderAmount,
            req.MinDeliveryFee,
            req.RestrictToNeighborhoods is not null
                ? JsonSerializer.Serialize(req.RestrictToNeighborhoods) : null,
            req.RestrictToProductIds is not null
                ? JsonSerializer.Serialize(req.RestrictToProductIds) : null,
            req.IsFirstOrderOnly,
            req.RestrictToCustomerPhone,
            req.MaxUses,
            req.ValidFrom,
            req.ValidUntil);
        _repo.Add(coupon);
        await _repo.SaveChangesAsync(ct);
        return Map(coupon);
    }

    public async Task<CouponDto> UpdateAsync(
        Guid id, UpdateCouponRequest req, CancellationToken ct = default)
    {
        var coupon = await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Coupon", id);
        var discountType = Enum.Parse<CouponDiscountType>(req.DiscountType, ignoreCase: true);
        coupon.Update(
            discountType, req.DiscountValue, req.Description,
            req.MinOrderAmount, req.MinDeliveryFee,
            req.RestrictToNeighborhoods is not null
                ? JsonSerializer.Serialize(req.RestrictToNeighborhoods) : null,
            req.RestrictToProductIds is not null
                ? JsonSerializer.Serialize(req.RestrictToProductIds) : null,
            req.IsFirstOrderOnly, req.RestrictToCustomerPhone,
            req.MaxUses, req.ValidFrom, req.ValidUntil);
        await _repo.SaveChangesAsync(ct);
        return Map(coupon);
    }

    public async Task RevokeAsync(Guid id, CancellationToken ct = default)
    {
        var coupon = await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Coupon", id);
        coupon.Revoke();
        await _repo.SaveChangesAsync(ct);
    }

    public async Task<ValidateCouponResponse> ValidatePublicAsync(
        ValidateCouponRequest req, CancellationToken ct = default)
    {
        var store = await _stores.GetByPublicSlugAsync(req.PublicSlug, ct)
            ?? throw new NotFoundException("Store", req.PublicSlug);

        var coupon = await _repo.GetByCodePublicAsync(
            req.CouponCode, store.Id, store.TenantId, ct);

        if (coupon is null)
            return new ValidateCouponResponse(false, "Cupom não encontrado.", 0, "", 0);

        var normalizedPhone = new string(req.CustomerPhone.Where(char.IsDigit).ToArray());
        var orderCount = await _repo.CountOrdersByPhonePublicAsync(
            normalizedPhone, store.Id, store.TenantId, ct);
        var isFirstOrder = orderCount == 0;

        try
        {
            var discount = coupon.CalculateDiscount(
                req.ItemsSubtotal, req.DeliveryFee,
                req.CustomerPhone, req.Neighborhood, isFirstOrder);

            return new ValidateCouponResponse(
                true, null, discount,
                coupon.DiscountType.ToString(), coupon.DiscountValue);
        }
        catch (DomainException ex)
        {
            return new ValidateCouponResponse(false, ex.Message, 0, "", 0);
        }
    }

    private static CouponDto Map(Coupon c) => new(
        c.Id, c.Code, c.Description, c.DiscountType.ToString(), c.DiscountValue,
        c.IsActive, c.MinOrderAmount, c.MinDeliveryFee,
        c.RestrictToNeighborhoods is not null
            ? JsonSerializer.Deserialize<string[]>(c.RestrictToNeighborhoods) : null,
        c.RestrictToProductIds is not null
            ? JsonSerializer.Deserialize<Guid[]>(c.RestrictToProductIds) : null,
        c.IsFirstOrderOnly, c.RestrictToCustomerPhone,
        c.MaxUses, c.UsedCount, c.ValidFrom, c.ValidUntil);
}
