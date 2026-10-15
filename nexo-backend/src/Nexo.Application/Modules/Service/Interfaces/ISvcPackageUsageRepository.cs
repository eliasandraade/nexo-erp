using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>Repository for SvcPackageUsage (append-only consumption history).</summary>
public interface ISvcPackageUsageRepository
{
    Task<IReadOnlyList<SvcPackageUsage>> GetByCustomerPackageAsync(Guid customerPackageId, CancellationToken ct = default);
    Task<SvcPackageUsage?> GetByIdAsync(Guid id, CancellationToken ct = default);
    void Update(SvcPackageUsage entity);
    /// <summary>True when a consumption (not reversed) already paid for this appointment.</summary>
    Task<bool> ExistsForAppointmentAsync(Guid appointmentId, CancellationToken ct = default);
    Task AddAsync(SvcPackageUsage entity, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
