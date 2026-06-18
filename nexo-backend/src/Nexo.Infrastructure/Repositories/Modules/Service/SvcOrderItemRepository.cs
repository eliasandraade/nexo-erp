using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcOrderItemRepository : ISvcOrderItemRepository
{
    private readonly NexoDbContext _context;
    public SvcOrderItemRepository(NexoDbContext context) => _context = context;

    public async Task<SvcOrderItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcOrderItems.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SvcOrderItem>> GetByOrderAsync(Guid orderId, CancellationToken ct = default)
        => await _context.SvcOrderItems.Where(x => x.OrderId == orderId)
            .OrderBy(x => x.CreatedAt).ToListAsync(ct);

    public async Task AddAsync(SvcOrderItem entity, CancellationToken ct = default)
        => await _context.SvcOrderItems.AddAsync(entity, ct);

    public void Update(SvcOrderItem entity) => _context.SvcOrderItems.Update(entity);
    public void Remove(SvcOrderItem entity) => _context.SvcOrderItems.Remove(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
