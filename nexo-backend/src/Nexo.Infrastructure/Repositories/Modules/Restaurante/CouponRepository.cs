using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Restaurante;

public class CouponRepository : ICouponRepository
{
    private readonly NexoDbContext _db;
    public CouponRepository(NexoDbContext db) => _db = db;

    public async Task<IReadOnlyList<Coupon>> GetAllAsync(CancellationToken ct = default)
        => await _db.Coupons.AsNoTracking()
            .OrderBy(x => x.Code)
            .ToListAsync(ct);

    public Task<Coupon?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Coupons.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<Coupon?> GetByCodePublicAsync(
        string code, Guid storeId, Guid tenantId, CancellationToken ct = default)
        => _db.Coupons.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x =>
                x.StoreId  == storeId  &&
                x.TenantId == tenantId &&
                x.Code     == code.Trim().ToUpperInvariant() &&
                x.IsActive, ct);

    public Task<int> CountOrdersByPhonePublicAsync(
        string normalizedPhone, Guid storeId, Guid tenantId, CancellationToken ct = default)
        => _db.RestDeliveryOrders.IgnoreQueryFilters()
            .CountAsync(x =>
                x.StoreId       == storeId  &&
                x.TenantId      == tenantId &&
                x.CustomerPhone == normalizedPhone &&
                x.Status        != DeliveryOrderStatus.Rejected &&
                x.Status        != DeliveryOrderStatus.Cancelled, ct);

    public void Add(Coupon coupon) => _db.Coupons.Add(coupon);
    public void AddUsage(CouponUsage usage) => _db.CouponUsages.Add(usage);
    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
