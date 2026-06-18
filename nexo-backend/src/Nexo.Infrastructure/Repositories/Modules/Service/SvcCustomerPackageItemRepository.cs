using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcCustomerPackageItemRepository : ISvcCustomerPackageItemRepository
{
    private readonly NexoDbContext _context;
    public SvcCustomerPackageItemRepository(NexoDbContext context) => _context = context;

    public async Task<IReadOnlyList<SvcCustomerPackageItem>> GetByCustomerPackageAsync(Guid customerPackageId, CancellationToken ct = default)
        => await _context.SvcCustomerPackageItems.Where(x => x.CustomerPackageId == customerPackageId)
            .OrderBy(x => x.CreatedAt).ToListAsync(ct);

    public async Task AddAsync(SvcCustomerPackageItem entity, CancellationToken ct = default)
        => await _context.SvcCustomerPackageItems.AddAsync(entity, ct);

    public void Update(SvcCustomerPackageItem entity) => _context.SvcCustomerPackageItems.Update(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
