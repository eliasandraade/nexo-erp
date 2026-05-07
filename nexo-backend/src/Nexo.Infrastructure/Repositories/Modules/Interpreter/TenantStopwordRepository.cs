using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Interpreter;

public class TenantStopwordRepository : ITenantStopwordRepository
{
    private readonly NexoDbContext _context;

    public TenantStopwordRepository(NexoDbContext context) => _context = context;

    public async Task<IReadOnlyList<string>> GetWordsByTenantAsync(Guid tenantId, CancellationToken ct = default)
        => await _context.IntStopwords
            .Where(x => x.TenantId == tenantId)
            .Select(x => x.Word)
            .ToListAsync(ct);

    public async Task<TenantStopword?> GetByWordAsync(Guid tenantId, string word, CancellationToken ct = default)
        => await _context.IntStopwords
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Word == word, ct);

    public async Task AddAsync(TenantStopword stopword, CancellationToken ct = default)
        => await _context.IntStopwords.AddAsync(stopword, ct);

    public Task RemoveAsync(TenantStopword stopword, CancellationToken ct = default)
    {
        _context.IntStopwords.Remove(stopword);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
