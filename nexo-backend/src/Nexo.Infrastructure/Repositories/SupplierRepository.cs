using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories;

public class SupplierRepository : ISupplierRepository
{
    private readonly NexoDbContext _context;

    public SupplierRepository(NexoDbContext context) => _context = context;

    public async Task<Supplier?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.Suppliers.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Supplier?> GetByDocumentAsync(string documentNumber, CancellationToken ct = default)
        => await _context.Suppliers.FirstOrDefaultAsync(x => x.DocumentNumber == documentNumber, ct);

    public async Task<IReadOnlyList<Supplier>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
        => await _context.Suppliers
            .Where(x => includeInactive || x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);

    public async Task<bool> DocumentExistsAsync(string documentNumber, Guid? excludeId = null, CancellationToken ct = default)
        => await _context.Suppliers.AnyAsync(
            x => x.DocumentNumber == documentNumber && (excludeId == null || x.Id != excludeId), ct);

    public async Task AddAsync(Supplier supplier, CancellationToken ct = default)
        => await _context.Suppliers.AddAsync(supplier, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
