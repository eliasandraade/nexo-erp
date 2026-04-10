using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories;

public class SaleRepository : ISaleRepository
{
    private readonly NexoDbContext _context;

    public SaleRepository(NexoDbContext context) => _context = context;

    public async Task<Sale?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.Sales.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Sale?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default)
        => await _context.Sales
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Sale?> GetByNumberAsync(int number, CancellationToken ct = default)
        => await _context.Sales.FirstOrDefaultAsync(x => x.Number == number, ct);

    public async Task<IReadOnlyList<Sale>> GetAllAsync(CancellationToken ct = default)
        => await _context.Sales
            .OrderByDescending(x => x.Number)
            .ToListAsync(ct);

    public async Task<int> GetNextNumberAsync(CancellationToken ct = default)
    {
        var max = await _context.Sales.MaxAsync(x => (int?)x.Number, ct);
        return (max ?? 0) + 1;
    }

    public async Task AddAsync(Sale sale, CancellationToken ct = default)
        => await _context.Sales.AddAsync(sale, ct);

    public async Task AddPaymentAsync(SalePayment payment, CancellationToken ct = default)
        => await _context.SalePayments.AddAsync(payment, ct);

    public void TrackItem(SaleItem item)
        => _context.Entry(item).State = EntityState.Added;

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
