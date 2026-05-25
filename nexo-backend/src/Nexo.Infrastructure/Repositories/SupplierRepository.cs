using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Suppliers;
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

    public async Task<PagedResult<SupplierDto>> GetPagedAsync(
        int page, int pageSize, string? search, bool includeInactive, CancellationToken ct = default)
    {
        var q = _context.Suppliers
            .AsNoTracking()
            .Where(x => includeInactive || x.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(x =>
                x.Name.ToLower().Contains(term) ||
                x.DocumentNumber.Contains(term) ||
                (x.Email       != null && x.Email.ToLower().Contains(term)) ||
                (x.Phone       != null && x.Phone.Contains(term)) ||
                (x.ContactName != null && x.ContactName.ToLower().Contains(term)));
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SupplierDto(
                s.Id,
                s.PersonType.ToString(),
                s.Name,
                s.TradeName,
                s.DocumentType.ToString(),
                s.DocumentNumber,
                s.Email,
                s.Phone,
                s.ContactName,
                s.AddressJson,
                s.PaymentTermsDays,
                s.BankInfoJson,
                s.Notes,
                s.IsActive,
                s.CreatedAt,
                s.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResult<SupplierDto>(items, total, page, pageSize);
    }

    public async Task<bool> DocumentExistsAsync(string documentNumber, Guid? excludeId = null, CancellationToken ct = default)
        => await _context.Suppliers.AnyAsync(
            x => x.DocumentNumber == documentNumber && (excludeId == null || x.Id != excludeId), ct);

    public async Task AddAsync(Supplier supplier, CancellationToken ct = default)
        => await _context.Suppliers.AddAsync(supplier, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
