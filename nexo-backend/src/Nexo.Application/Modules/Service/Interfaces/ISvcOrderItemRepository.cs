using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>Repository for SvcOrderItem. Tenant + store isolation enforced by the EF global query filter.</summary>
public interface ISvcOrderItemRepository
{
    Task<SvcOrderItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SvcOrderItem>> GetByOrderAsync(Guid orderId, CancellationToken ct = default);
    Task AddAsync(SvcOrderItem entity, CancellationToken ct = default);
    void Update(SvcOrderItem entity);
    void Remove(SvcOrderItem entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
