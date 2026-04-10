using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Varejo.Interfaces;
using Nexo.Domain.Modules.Varejo;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Varejo;

public class PriceListRepository : IPriceListRepository
{
    private readonly NexoDbContext _context;

    public PriceListRepository(NexoDbContext context) => _context = context;

    public async Task<RetPriceList?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.RetPriceLists.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<RetPriceList?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default)
        => await _context.RetPriceLists
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<RetPriceList?> GetDefaultAsync(CancellationToken ct = default)
        => await _context.RetPriceLists
            .Where(x => x.IsDefault && x.IsActive)
            .FirstOrDefaultAsync(ct);

    public async Task<RetPriceList?> GetByCustomerIdAsync(Guid customerId, CancellationToken ct = default)
    {
        var link = await _context.RetCustomerPriceLists
            .Include(x => x.PriceList)
            .FirstOrDefaultAsync(x => x.CustomerId == customerId, ct);

        return link?.PriceList?.IsActive == true ? link.PriceList : null;
    }

    public async Task<IReadOnlyList<RetPriceList>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var query = _context.RetPriceLists.AsQueryable();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        return await query.OrderBy(x => x.Name).ToListAsync(ct);
    }

    public async Task AddAsync(RetPriceList priceList, CancellationToken ct = default)
        => await _context.RetPriceLists.AddAsync(priceList, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
