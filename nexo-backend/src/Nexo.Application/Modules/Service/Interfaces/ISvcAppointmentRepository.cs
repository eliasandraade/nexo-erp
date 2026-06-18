using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>Repository for SvcAppointment. Tenant + store isolation enforced by the EF global query filter.</summary>
public interface ISvcAppointmentRepository
{
    Task<SvcAppointment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SvcAppointment>> GetAllAsync(
        DateTime? from, DateTime? to, Guid? professionalId, SvcAppointmentStatus? status,
        Guid? customerId, Guid? subjectId, CancellationToken ct = default);

    /// <summary>True if the professional has a blocking appointment (Scheduled/Confirmed/InProgress)
    /// overlapping [startsAt, endsAt). <paramref name="excludeId"/> skips the row being rescheduled.</summary>
    Task<bool> HasOverlapAsync(
        Guid professionalId, DateTime startsAt, DateTime endsAt, Guid? excludeId, CancellationToken ct = default);

    /// <summary>
    /// Public-path read (no auth context): blocking appointments for a professional within a UTC
    /// window, used to subtract busy time from generated availability. Scoped explicitly by
    /// tenant + store; bypasses the global query filter.
    /// </summary>
    Task<IReadOnlyList<SvcAppointment>> GetBlockingForProfessionalPublicAsync(
        Guid professionalId, Guid tenantId, Guid storeId, DateTime fromUtc, DateTime toUtc,
        CancellationToken ct = default);

    /// <summary>Public-path overlap guard, scoped explicitly by tenant + store.</summary>
    Task<bool> HasOverlapPublicAsync(
        Guid professionalId, Guid tenantId, Guid storeId, DateTime startsAt, DateTime endsAt,
        CancellationToken ct = default);

    Task AddAsync(SvcAppointment entity, CancellationToken ct = default);
    void Update(SvcAppointment entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
