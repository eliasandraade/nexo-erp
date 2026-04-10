using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories;

public class CategoryRepository : ICategoryRepository
{
    private readonly NexoDbContext _context;

    public CategoryRepository(NexoDbContext context) => _context = context;

    public async Task<Category?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.Categories.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Category>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
        => await _context.Categories
            .Where(x => includeInactive || x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);

    public async Task<bool> HasChildrenAsync(Guid id, CancellationToken ct = default)
        => await _context.Categories.AnyAsync(x => x.ParentCategoryId == id, ct);

    public async Task AddAsync(Category category, CancellationToken ct = default)
        => await _context.Categories.AddAsync(category, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
