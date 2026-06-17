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

    Task AddAsync(SvcAppointment entity, CancellationToken ct = default);
    void Update(SvcAppointment entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
