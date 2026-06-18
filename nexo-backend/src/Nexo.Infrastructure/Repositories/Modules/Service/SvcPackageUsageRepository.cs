using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcPackageUsageRepository : ISvcPackageUsageRepository
{
    private readonly NexoDbContext _context;
    public SvcPackageUsageRepository(NexoDbContext context) => _context = context;

    public async Task<IReadOnlyList<SvcPackageUsage>> GetByCustomerPackageAsync(Guid customerPackageId, CancellationToken ct = default)
        => await _context.SvcPackageUsages.Where(x => x.CustomerPackageId == customerPackageId)
            .OrderByDescending(x => x.CreatedAt).ToListAsync(ct);

    public async Task AddAsync(SvcPackageUsage entity, CancellationToken ct = default)
        => await _context.SvcPackageUsages.AddAsync(entity, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
