using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcCustomerPackageRepository : ISvcCustomerPackageRepository
{
    private readonly NexoDbContext _context;
    public SvcCustomerPackageRepository(NexoDbContext context) => _context = context;

    public async Task<SvcCustomerPackage?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcCustomerPackages.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<SvcCustomerPackage?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcCustomerPackages.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SvcCustomerPackage>> GetAllAsync(
        Guid? customerId, Guid? subjectId, SvcCustomerPackageStatus? status, Guid? packageId, CancellationToken ct = default)
    {
        var q = _context.SvcCustomerPackages.AsQueryable();
        if (customerId is { } c) q = q.Where(x => x.CustomerId == c);
        if (subjectId is { } s)  q = q.Where(x => x.SubjectId == s);
        if (status is { } st)    q = q.Where(x => x.Status == st);
        if (packageId is { } p)  q = q.Where(x => x.PackageId == p);
        return await q.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    }

    public async Task AddAsync(SvcCustomerPackage entity, CancellationToken ct = default)
        => await _context.SvcCustomerPackages.AddAsync(entity, ct);

    public void Update(SvcCustomerPackage entity) => _context.SvcCustomerPackages.Update(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
