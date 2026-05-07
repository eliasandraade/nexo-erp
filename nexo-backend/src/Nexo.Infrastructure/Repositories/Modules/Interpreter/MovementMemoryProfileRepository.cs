using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Interpreter;

public class MovementMemoryProfileRepository : IMovementMemoryProfileRepository
{
    private readonly NexoDbContext _context;

    public MovementMemoryProfileRepository(NexoDbContext context) => _context = context;

    public async Task<MovementMemoryProfile?> GetAsync(Guid tenantId, Guid? userId, CancellationToken ct = default)
        => await _context.IntMemoryProfiles
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, ct);

    public async Task AddAsync(MovementMemoryProfile profile, CancellationToken ct = default)
        => await _context.IntMemoryProfiles.AddAsync(profile, ct);

    public void Update(MovementMemoryProfile profile)
        => _context.IntMemoryProfiles.Update(profile);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
