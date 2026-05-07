using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Interpreter;

public class ExtractionResultRepository : IExtractionResultRepository
{
    private readonly NexoDbContext _context;

    public ExtractionResultRepository(NexoDbContext context) => _context = context;

    public async Task<ExtractionResult?> GetLatestByMovementIdAsync(Guid movementId, CancellationToken ct = default)
        => await _context.IntExtractionResults
            .Where(x => x.MovementId == movementId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ExtractionResult>> GetAllByMovementIdAsync(Guid movementId, CancellationToken ct = default)
        => await _context.IntExtractionResults
            .Where(x => x.MovementId == movementId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(ExtractionResult result, CancellationToken ct = default)
        => await _context.IntExtractionResults.AddAsync(result, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
