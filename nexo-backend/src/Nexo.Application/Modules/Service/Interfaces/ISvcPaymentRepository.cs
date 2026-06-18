using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>Repository for SvcPayment. Tenant + store isolation via the EF global query filter.</summary>
public interface ISvcPaymentRepository
{
    Task<SvcPayment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SvcPayment>> GetAllAsync(
        Guid? customerId, Guid? orderId, Guid? customerPackageId, SvcPaymentMethod? method,
        SvcPaymentStatus? status, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<IReadOnlyList<SvcPayment>> GetByOrderAsync(Guid orderId, CancellationToken ct = default);
    Task<IReadOnlyList<SvcPayment>> GetByCustomerPackageAsync(Guid customerPackageId, CancellationToken ct = default);
    Task AddAsync(SvcPayment entity, CancellationToken ct = default);
    void Update(SvcPayment entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
