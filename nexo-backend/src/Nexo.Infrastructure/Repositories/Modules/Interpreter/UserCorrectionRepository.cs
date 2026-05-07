using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Interpreter;

public class UserCorrectionRepository : IUserCorrectionRepository
{
    private readonly NexoDbContext _context;

    public UserCorrectionRepository(NexoDbContext context) => _context = context;

    public async Task<IReadOnlyList<UserCorrection>> GetByMovementIdAsync(Guid movementId, CancellationToken ct = default)
        => await _context.IntUserCorrections
            .Where(x => x.MovementId == movementId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UserCorrection>> GetByTenantAsync(
        Guid              tenantId,
        DateOnly?         from     = null,
        DateOnly?         to       = null,
        CorrectionType?   type     = null,
        CancellationToken ct       = default)
    {
        var query = _context.IntUserCorrections.Where(x => x.TenantId == tenantId);

        if (from.HasValue) query = query.Where(x => DateOnly.FromDateTime(x.CreatedAt) >= from.Value);
        if (to.HasValue)   query = query.Where(x => DateOnly.FromDateTime(x.CreatedAt) <= to.Value);
        if (type.HasValue) query = query.Where(x => x.CorrectionType == type.Value);

        return await query.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    }

    public async Task AddRangeAsync(IEnumerable<UserCorrection> corrections, CancellationToken ct = default)
        => await _context.IntUserCorrections.AddRangeAsync(corrections, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
