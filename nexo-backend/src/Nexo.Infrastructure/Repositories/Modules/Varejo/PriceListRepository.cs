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
        var query = _context.RetPriceLists.Include(x => x.Items).AsQueryable();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        return await query.OrderBy(x => x.Name).ToListAsync(ct);
    }

    public void TrackItem(RetPriceListItem item) => _context.Entry(item).State = EntityState.Added;

    public async Task<IReadOnlyList<RetPriceList>> GetAllDefaultsAsync(CancellationToken ct = default)
        => await _context.RetPriceLists.Where(x => x.IsDefault).ToListAsync(ct);   // active or not

    public async Task<RetCustomerPriceList?> GetCustomerLinkAsync(Guid customerId, CancellationToken ct = default)
        => await _context.RetCustomerPriceLists.FirstOrDefaultAsync(x => x.CustomerId == customerId, ct);

    public async Task<IReadOnlyList<(Guid CustomerId, string Name)>> GetLinkedCustomersAsync(Guid priceListId, CancellationToken ct = default)
    {
        var rows = await _context.RetCustomerPriceLists
            .Where(x => x.PriceListId == priceListId)
            .Join(_context.Customers, l => l.CustomerId, c => c.Id, (l, c) => new { c.Id, c.Name })
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        return rows.Select(r => (r.Id, r.Name)).ToList();
    }

    public async Task AddCustomerLinkAsync(RetCustomerPriceList link, CancellationToken ct = default)
        => await _context.RetCustomerPriceLists.AddAsync(link, ct);

    public void RemoveCustomerLink(RetCustomerPriceList link) => _context.RetCustomerPriceLists.Remove(link);

    public async Task<IReadOnlyDictionary<Guid, (string Name, string Code)>> GetProductLabelsAsync(
        IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        // Lists are tenant-wide, products are per store: read labels across the tenant's stores.
        var tenantId = _context.CurrentTenantIdForFilter;
        var rows = await _context.Products.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && list.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Code }).ToListAsync(ct);
        return rows.ToDictionary(x => x.Id, x => (x.Name, x.Code));
    }

    public async Task AddAsync(RetPriceList priceList, CancellationToken ct = default)
        => await _context.RetPriceLists.AddAsync(priceList, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
