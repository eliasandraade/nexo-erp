using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;
using Npgsql;

namespace Nexo.Infrastructure.Repositories.Modules.Restaurante;

public class DeliveryOrderRepository : IDeliveryOrderRepository
{
    private readonly NexoDbContext _context;

    public DeliveryOrderRepository(NexoDbContext context) => _context = context;

    public async Task<RestDeliveryOrder?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.RestDeliveryOrders
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<RestDeliveryOrder?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default)
        => await _context.RestDeliveryOrders
            .Include(x => x.Items)
                .ThenInclude(i => i.Modifiers)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<RestDeliveryOrder?> GetByTrackingTokenAsync(string token, CancellationToken ct = default)
        => await _context.RestDeliveryOrders
            .FirstOrDefaultAsync(x => x.TrackingToken == token, ct);

    public async Task<RestDeliveryOrder?> GetByTrackingTokenPublicAsync(string token, CancellationToken ct = default)
        => await _context.RestDeliveryOrders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TrackingToken == token, ct);

    /// <summary>
    /// Bypasses query filters and applies explicit tenant+store guards.
    /// Necessary because SyncFromRestOrderAsync may be called in contexts where
    /// the global filter would silently return null (e.g. cross-store admin flows).
    /// CurrentTenantIdForFilter / CurrentStoreIdForFilter read from the scoped DbContext
    /// instance — they reflect the resolved JWT context of the current request.
    /// </summary>
    public async Task<RestDeliveryOrder?> GetByRestOrderIdAsync(Guid restOrderId, CancellationToken ct = default)
        => await _context.RestDeliveryOrders
            .IgnoreQueryFilters()
            .Where(x => x.RestOrderId == restOrderId
                     && x.TenantId   == _context.CurrentTenantIdForFilter
                     && x.StoreId    == _context.CurrentStoreIdForFilter)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<RestDeliveryOrder>> GetAllAsync(
        DeliveryOrderStatus[]? statuses = null,
        DeliveryChannel[]? channels = null,
        DateOnly? date = null,
        CancellationToken ct = default)
    {
        var query = _context.RestDeliveryOrders
            .Include(x => x.Items)
                .ThenInclude(i => i.Modifiers)
            .AsQueryable();

        if (statuses is { Length: > 0 })
            query = query.Where(x => statuses.Contains(x.Status));

        if (channels is { Length: > 0 })
            query = query.Where(x => channels.Contains(x.Channel));

        if (date.HasValue)
        {
            var start = date.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var end   = date.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(x => x.ReceivedAt >= start && x.ReceivedAt <= end);
        }

        return await query
            .OrderByDescending(x => x.ReceivedAt)
            .ToListAsync(ct);
    }

    public async Task<int> GetNextOrderNumberAsync(CancellationToken ct = default)
    {
        var max = await _context.RestDeliveryOrders.MaxAsync(x => (int?)x.OrderNumber, ct);
        return (max ?? 0) + 1;
    }

    public async Task<int> GetNextOrderNumberForStoreAsync(Guid tenantId, Guid storeId, CancellationToken ct = default)
    {
        var max = await _context.RestDeliveryOrders
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.StoreId == storeId)
            .MaxAsync(x => (int?)x.OrderNumber, ct);
        return (max ?? 0) + 1;
    }

    public async Task AddAsync(RestDeliveryOrder order, CancellationToken ct = default)
        => await _context.RestDeliveryOrders.AddAsync(order, ct);

    public void Detach(RestDeliveryOrder order)
        => _context.Entry(order).State = EntityState.Detached;

    public void TrackItem(RestDeliveryOrderItem item)
        => _context.Entry(item).State = EntityState.Added;

    public void TrackModifier(RestDeliveryOrderItemModifier modifier)
        => _context.Entry(modifier).State = EntityState.Added;

    /// <summary>
    /// Saves changes and translates a unique violation on ix_rest_delivery_orders_store_number
    /// into OrderNumberCollisionException so the service layer can retry without knowing
    /// about Npgsql types.
    /// </summary>
    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg
                  && pg.SqlState == PostgresErrorCodes.UniqueViolation
                  && pg.ConstraintName == "ix_rest_delivery_orders_store_number")
        {
            throw new OrderNumberCollisionException();
        }
    }
}
