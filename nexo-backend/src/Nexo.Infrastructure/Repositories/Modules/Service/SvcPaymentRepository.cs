using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcPaymentRepository : ISvcPaymentRepository
{
    private readonly NexoDbContext _context;
    public SvcPaymentRepository(NexoDbContext context) => _context = context;

    public async Task<SvcPayment?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcPayments.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SvcPayment>> GetAllAsync(
        Guid? customerId, Guid? orderId, Guid? customerPackageId, SvcPaymentMethod? method,
        SvcPaymentStatus? status, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var q = _context.SvcPayments.AsQueryable();
        if (customerId is { } c)        q = q.Where(x => x.CustomerId == c);
        if (orderId is { } o)           q = q.Where(x => x.OrderId == o);
        if (customerPackageId is { } p) q = q.Where(x => x.CustomerPackageId == p);
        if (method is { } m)            q = q.Where(x => x.Method == m);
        if (status is { } s)            q = q.Where(x => x.Status == s);
        if (from is { } f)              q = q.Where(x => x.PaidAt >= f);
        if (to is { } t)                q = q.Where(x => x.PaidAt <= t);
        return await q.OrderByDescending(x => x.PaidAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SvcPayment>> GetByOrderAsync(Guid orderId, CancellationToken ct = default)
        => await _context.SvcPayments.Where(x => x.OrderId == orderId).OrderByDescending(x => x.PaidAt).ToListAsync(ct);

    public async Task<IReadOnlyList<SvcPayment>> GetByCustomerPackageAsync(Guid customerPackageId, CancellationToken ct = default)
        => await _context.SvcPayments.Where(x => x.CustomerPackageId == customerPackageId).OrderByDescending(x => x.PaidAt).ToListAsync(ct);

    public async Task AddAsync(SvcPayment entity, CancellationToken ct = default)
        => await _context.SvcPayments.AddAsync(entity, ct);

    public void Update(SvcPayment entity) => _context.SvcPayments.Update(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
