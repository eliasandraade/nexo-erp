using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Restaurante;

public class OrderRepository : IOrderRepository
{
    private readonly NexoDbContext _context;

    public OrderRepository(NexoDbContext context) => _context = context;

    public async Task<RestOrder?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.RestOrders
            .Include(x => x.Table)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<RestOrder?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default)
        => await _context.RestOrders
            .Include(x => x.Table)
            .Include(x => x.Items)
                .ThenInclude(i => i.Product)
            .Include(x => x.Items)
                .ThenInclude(i => i.Modifiers)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    /// <summary>
    /// Retorna a comanda aberta para a mesa (status ∈ Open, InPreparation, Ready, Closed).
    /// Closed está incluído porque a mesa ainda está Occupied enquanto aguarda pagamento.
    /// </summary>
    public async Task<RestOrder?> GetOpenOrderForTableAsync(Guid tableId, CancellationToken ct = default)
        => await _context.RestOrders
            .Where(x => x.TableId == tableId &&
                        x.Status != RestOrderStatus.Cancelled)
            .OrderByDescending(x => x.OpenedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<RestOrder>> GetAllAsync(CancellationToken ct = default)
        => await _context.RestOrders
            .Include(x => x.Table)
            .Include(x => x.Items)
                .ThenInclude(i => i.Product)
            .OrderByDescending(x => x.OpenedAt)
            .ToListAsync(ct);

    public async Task<int> GetNextNumberAsync(CancellationToken ct = default)
    {
        var max = await _context.RestOrders.MaxAsync(x => (int?)x.OrderNumber, ct);
        return (max ?? 0) + 1;
    }

    public async Task AddAsync(RestOrder order, CancellationToken ct = default)
        => await _context.RestOrders.AddAsync(order, ct);

    public void TrackItem(RestOrderItem item)
        => _context.Entry(item).State = EntityState.Added;

    public void TrackModifier(RestOrderItemModifier modifier)
        => _context.Entry(modifier).State = EntityState.Added;

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
