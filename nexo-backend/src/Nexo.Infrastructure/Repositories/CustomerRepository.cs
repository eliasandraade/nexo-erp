using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Customers;
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

    public async Task<PagedResult<CustomerDto>> GetPagedAsync(
        int page, int pageSize, string? search, bool includeInactive, CancellationToken ct = default)
    {
        var q = _context.Customers
            .AsNoTracking()
            .Where(x => includeInactive || x.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(x =>
                x.Name.ToLower().Contains(term) ||
                x.DocumentNumber.Contains(term) ||
                (x.Email  != null && x.Email.ToLower().Contains(term)) ||
                (x.Phone  != null && x.Phone.Contains(term)));
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CustomerDto(
                c.Id,
                c.PersonType.ToString(),
                c.Name,
                c.TradeName,
                c.DocumentType.ToString(),
                c.DocumentNumber,
                c.Email,
                c.Phone,
                c.WhatsApp,
                c.AddressJson,
                c.CreditLimit,
                c.Notes,
                c.IsActive,
                c.CreatedAt,
                c.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResult<CustomerDto>(items, total, page, pageSize);
    }

    public async Task<bool> DocumentExistsAsync(string documentNumber, Guid? excludeId = null, CancellationToken ct = default)
        => await _context.Customers.AnyAsync(
            x => x.DocumentNumber == documentNumber && (excludeId == null || x.Id != excludeId), ct);

    public async Task AddAsync(Customer customer, CancellationToken ct = default)
        => await _context.Customers.AddAsync(customer, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
