using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcSettingsRepository : ISvcSettingsRepository
{
    private readonly NexoDbContext _context;

    public SvcSettingsRepository(NexoDbContext context) => _context = context;

    // The global query filter scopes to the current tenant + store, so this returns the active
    // store's settings (or null when it hasn't been configured yet).
    public async Task<SvcSettings?> GetForCurrentStoreAsync(CancellationToken ct = default)
        => await _context.SvcSettings.FirstOrDefaultAsync(ct);

    public async Task AddAsync(SvcSettings entity, CancellationToken ct = default)
        => await _context.SvcSettings.AddAsync(entity, ct);

    public void Update(SvcSettings entity)
        => _context.SvcSettings.Update(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
