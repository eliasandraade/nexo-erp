using Nexo.Application.Common;
using Nexo.Application.Features.Suppliers;
using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

public interface ISupplierRepository
{
    Task<Supplier?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Supplier?> GetByDocumentAsync(string documentNumber, CancellationToken ct = default);
    Task<IReadOnlyList<Supplier>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<PagedResult<SupplierDto>> GetPagedAsync(int page, int pageSize, string? search, bool includeInactive, CancellationToken ct = default);
    Task<bool> DocumentExistsAsync(string documentNumber, Guid? excludeId = null, CancellationToken ct = default);
    Task AddAsync(Supplier supplier, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
