using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Varejo.Interfaces;
using Nexo.Domain.Modules.Varejo;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Varejo;

public class PurchaseRepository : IPurchaseRepository
{
    private readonly NexoDbContext _context;

    public PurchaseRepository(NexoDbContext context) => _context = context;

    public async Task<RetPurchase?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.RetPurchases.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<RetPurchase?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default)
        => await _context.RetPurchases
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<RetPurchase>> GetAllAsync(CancellationToken ct = default)
        => await _context.RetPurchases
            .OrderByDescending(x => x.PurchaseNumber)
            .ToListAsync(ct);

    public async Task<int> GetNextNumberAsync(CancellationToken ct = default)
    {
        var max = await _context.RetPurchases.MaxAsync(x => (int?)x.PurchaseNumber, ct);
        return (max ?? 0) + 1;
    }

    public async Task AddAsync(RetPurchase purchase, CancellationToken ct = default)
        => await _context.RetPurchases.AddAsync(purchase, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
