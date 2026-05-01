using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante.Interfaces;

public interface ICouponRepository
{
    Task<IReadOnlyList<Coupon>> GetAllAsync(CancellationToken ct = default);
    Task<Coupon?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Public: bypasses query filters. For portal order coupon validation.</summary>
    Task<Coupon?> GetByCodePublicAsync(
        string code, Guid storeId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Counts completed (non-rejected, non-cancelled) delivery orders for this phone on this store.
    /// Used for IsFirstOrderOnly check. Bypasses query filters.
    /// </summary>
    Task<int> CountOrdersByPhonePublicAsync(
        string normalizedPhone, Guid storeId, Guid tenantId, CancellationToken ct = default);

    void Add(Coupon coupon);
    void AddUsage(CouponUsage usage);
    Task SaveChangesAsync(CancellationToken ct = default);
}
