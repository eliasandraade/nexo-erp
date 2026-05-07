using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Interpreter;

public class MovementAttachmentRepository : IMovementAttachmentRepository
{
    private readonly NexoDbContext _context;

    public MovementAttachmentRepository(NexoDbContext context) => _context = context;

    public async Task<MovementAttachment?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.IntAttachments.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<MovementAttachment>> GetByMovementIdAsync(Guid movementId, CancellationToken ct = default)
        => await _context.IntAttachments
            .Where(x => x.MovementId == movementId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(MovementAttachment attachment, CancellationToken ct = default)
        => await _context.IntAttachments.AddAsync(attachment, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
