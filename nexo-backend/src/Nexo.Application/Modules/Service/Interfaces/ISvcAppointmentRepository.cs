using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>Repository for SvcAppointment. Tenant + store isolation enforced by the EF global query filter.</summary>
public interface ISvcAppointmentRepository
{
    Task<SvcAppointment?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Transaction-scoped lock per professional: two bookings of the same professional serialise.</summary>
    Task LockProfessionalAsync(Guid professionalId, CancellationToken ct = default);
    /// <summary>True if a bloqueio (the professional's or the whole store's) covers part of [start, end).</summary>
    Task<bool> IsBlockedAsync(Guid professionalId, DateTime startsAt, DateTime endsAt, CancellationToken ct = default);
    /// <summary>Public path: the bloqueios of a professional (and the store's) within a window.</summary>
    Task<IReadOnlyList<SvcTimeBlock>> GetBlocksPublicAsync(
        Guid professionalId, Guid tenantId, Guid storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    /// <summary>The booking behind a customer's own link, inside the slug's store (tracked).</summary>
    Task<SvcAppointment?> GetByPublicTokenAsync(string token, Guid tenantId, Guid storeId, CancellationToken ct = default);
    /// <summary>Row lock (FOR UPDATE) on the booking behind a customer's link, before loading it.</summary>
    Task LockByPublicTokenAsync(string token, Guid tenantId, Guid storeId, CancellationToken ct = default);
    /// <summary>
    /// Row lock (FOR UPDATE) on one booking, before loading it: a status change and the customer's
    /// cancellation never overwrite each other (each saves the whole row).
    /// </summary>
    Task LockRowAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    /// <summary>Public path: the booking already has a comanda (not cancelled) or a package paid for it.</summary>
    Task<bool> HasChargesPublicAsync(Guid appointmentId, Guid tenantId, CancellationToken ct = default);
    /// <summary>Public path: same as <see cref="IsBlockedAsync"/>, scoped explicitly.</summary>
    Task<bool> IsBlockedPublicAsync(
        Guid professionalId, Guid tenantId, Guid storeId, DateTime startsAt, DateTime endsAt, CancellationToken ct = default);
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
