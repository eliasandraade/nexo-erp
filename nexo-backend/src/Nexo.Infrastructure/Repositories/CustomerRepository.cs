using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private readonly NexoDbContext _context;

    public CustomerRepository(NexoDbContext context) => _context = context;

    public async Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Customer?> GetByDocumentAsync(string documentNumber, CancellationToken ct = default)
        => await _context.Customers.FirstOrDefaultAsync(x => x.DocumentNumber == documentNumber, ct);

    public async Task<IReadOnlyList<Customer>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
        => await _context.Customers
            .Where(x => includeInactive || x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);

    public async Task<bool> DocumentExistsAsync(string documentNumber, Guid? excludeId = null, CancellationToken ct = default)
        => await _context.Customers.AnyAsync(
            x => x.DocumentNumber == documentNumber && (excludeId == null || x.Id != excludeId), ct);

    public async Task AddAsync(Customer customer, CancellationToken ct = default)
        => await _context.Customers.AddAsync(customer, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
