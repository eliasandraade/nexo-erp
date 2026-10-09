using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>Repository for the SvcOrder aggregate. Tenant + store isolation enforced by the EF global query filter.</summary>
public interface ISvcOrderRepository
{
    Task<SvcOrder?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SvcOrder?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SvcOrder>> GetAllAsync(
        SvcOrderStatus? status, Guid? customerId, Guid? subjectId, Guid? professionalId,
        Guid? appointmentId, CancellationToken ct = default);
    Task<bool> ExistsForAppointmentAsync(Guid appointmentId, CancellationToken ct = default);

    /// <summary>
    /// SELECT … FOR UPDATE on the order row (scoped to the tenant). Must run inside a transaction:
    /// serialises everything that changes what an order owes or has paid (payments, voids, item
    /// edits), so a concurrent pair can neither both miss the "fully paid" moment nor edit an item
    /// while its commission is being recognised. Works across API replicas (database lock).
    /// </summary>
    Task LockAsync(Guid orderId, Guid tenantId, CancellationToken ct = default);
    Task AddAsync(SvcOrder entity, CancellationToken ct = default);
    void Update(SvcOrder entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
