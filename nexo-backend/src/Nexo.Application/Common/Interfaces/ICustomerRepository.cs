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

    /// <summary>
    /// Public-path read (no auth context): finds an active customer by exact phone within a tenant,
    /// so the booking portal reuses an existing customer instead of duplicating. Bypasses the
    /// global query filter; scoped explicitly by tenantId.
    /// </summary>
    Task<Customer?> GetByPhonePublicAsync(Guid tenantId, string phone, CancellationToken ct = default);

    /// <summary>Public-path uniqueness guard for (tenant, document) — used to avoid colliding the unique index.</summary>
    Task<bool> DocumentExistsPublicAsync(Guid tenantId, string documentNumber, CancellationToken ct = default);

    Task AddAsync(Customer customer, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
