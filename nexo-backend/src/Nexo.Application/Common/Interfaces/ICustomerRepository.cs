using Nexo.Application.Common;
using Nexo.Application.Features.Customers;
using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Customer?> GetByDocumentAsync(string documentNumber, CancellationToken ct = default);
    Task<IReadOnlyList<Customer>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<PagedResult<CustomerDto>> GetPagedAsync(int page, int pageSize, string? search, bool includeInactive, CancellationToken ct = default);
    Task<bool> DocumentExistsAsync(string documentNumber, Guid? excludeId = null, CancellationToken ct = default);
    Task AddAsync(Customer customer, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
