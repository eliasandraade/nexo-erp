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
            .Include(x => x.Items)
            .OrderByDescending(x => x.PurchaseNumber)
            .Take(200)
            .ToListAsync(ct);

    public async Task<int> GetNextNumberAsync(CancellationToken ct = default)
    {
        var max = await _context.RetPurchases.MaxAsync(x => (int?)x.PurchaseNumber, ct);
        return (max ?? 0) + 1;
    }

    public async Task LockAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM nexo.ret_purchases WHERE id = {id} AND tenant_id = {tenantId} FOR UPDATE", ct);
        var tracked = _context.ChangeTracker.Entries<RetPurchase>().FirstOrDefault(e => e.Entity.Id == id);
        if (tracked is not null) await tracked.ReloadAsync(ct);
    }

    public async Task LockNumberingAsync(Guid storeId, CancellationToken ct = default)
    {
        const long salt = 0x1C0_0000_0000_0003;   // stable across processes
        var key = BitConverter.ToInt64(storeId.ToByteArray(), 0) ^ salt;
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    }

    public async Task<IReadOnlyDictionary<Guid, (string Name, string Code)>> GetProductLabelsAsync(
        IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        var rows = await _context.Products.Where(p => list.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Code }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => (r.Name, r.Code));
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetSupplierNamesAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        var rows = await _context.Suppliers.Where(s => list.Contains(s.Id))
            .Select(s => new { s.Id, Name = s.TradeName ?? s.Name }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => r.Name);
    }

    public void TrackItem(RetPurchaseItem item) => _context.Entry(item).State = EntityState.Added;

    public async Task AddAsync(RetPurchase purchase, CancellationToken ct = default)
        => await _context.RetPurchases.AddAsync(purchase, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
