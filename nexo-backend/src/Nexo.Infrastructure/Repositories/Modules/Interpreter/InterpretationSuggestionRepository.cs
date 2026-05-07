using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Interpreter;

public class InterpretationSuggestionRepository : IInterpretationSuggestionRepository
{
    private readonly NexoDbContext _context;

    public InterpretationSuggestionRepository(NexoDbContext context) => _context = context;

    public async Task<InterpretationSuggestion?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.IntSuggestions.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<InterpretationSuggestion?> GetLatestByMovementIdAsync(Guid movementId, CancellationToken ct = default)
        => await _context.IntSuggestions
            .Where(x => x.MovementId == movementId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(InterpretationSuggestion suggestion, CancellationToken ct = default)
        => await _context.IntSuggestions.AddAsync(suggestion, ct);

    public void Update(InterpretationSuggestion suggestion)
        => _context.IntSuggestions.Update(suggestion);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
