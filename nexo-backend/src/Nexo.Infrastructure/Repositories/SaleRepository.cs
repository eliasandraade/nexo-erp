using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Sales;
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
            .Include(x => x.Customer)
            .Include(x => x.SoldBy)
            .AsNoTracking()
            .OrderByDescending(x => x.Number)
            .ToListAsync(ct);

    public async Task<PagedResult<SaleListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search, string? status, string? paymentMethod, CancellationToken ct = default)
    {
        var q = _context.Sales
            .Include(x => x.Customer)
            .Include(x => x.SoldBy)
            .Include(x => x.Items).ThenInclude(i => i.Product)
            .Include(x => x.Payments)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(s =>
                s.Number.ToString().Contains(term) ||
                (s.Customer != null && s.Customer.Name.ToLower().Contains(term)) ||
                (s.SoldBy  != null && s.SoldBy.FullName.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(s => s.Status.ToString().ToLower() == status.ToLower());

        if (!string.IsNullOrWhiteSpace(paymentMethod))
        {
            // "Card" is a frontend alias covering both Debit and Credit
            if (string.Equals(paymentMethod, "Card", StringComparison.OrdinalIgnoreCase))
                q = q.Where(s => s.Payments.Any(p =>
                    p.Method.ToString() == "Debit" || p.Method.ToString() == "Credit"));
            else
                q = q.Where(s => s.Payments.Any(p =>
                    p.Method.ToString().ToLower() == paymentMethod.ToLower()));
        }

        var total = await q.CountAsync(ct);

        var sales = await q
            .OrderByDescending(x => x.Number)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = sales.Select(s =>
        {
            var firstItem  = s.Items.OrderBy(i => i.CreatedAt).FirstOrDefault();
            var methods    = s.Payments.Select(p => p.Method.ToString()).Distinct().ToList();
            var timestamp  = s.ConfirmedAt ?? s.CreatedAt;

            return new SaleListItemDto(
                s.Id,
                s.Number,
                s.Status.ToString(),
                s.CustomerId,
                s.Customer?.Name,
                s.SoldBy?.FullName ?? string.Empty,
                s.Total,
                timestamp,
                s.Items.Count,
                s.Items.Sum(i => i.Quantity),
                firstItem?.Product?.Name,
                methods.AsReadOnly());
        }).ToList();

        return new PagedResult<SaleListItemDto>(items, total, page, pageSize);
    }

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
