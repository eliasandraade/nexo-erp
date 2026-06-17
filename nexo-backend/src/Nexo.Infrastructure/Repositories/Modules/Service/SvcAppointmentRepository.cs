using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcAppointmentRepository : ISvcAppointmentRepository
{
    private readonly NexoDbContext _context;

    public SvcAppointmentRepository(NexoDbContext context) => _context = context;

    public async Task<SvcAppointment?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcAppointments.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SvcAppointment>> GetAllAsync(
        DateTime? from, DateTime? to, Guid? professionalId, SvcAppointmentStatus? status,
        Guid? customerId, Guid? subjectId, CancellationToken ct = default)
    {
        var q = _context.SvcAppointments.AsQueryable();
        if (from is { } f)           q = q.Where(a => a.StartsAt >= f);
        if (to is { } t)             q = q.Where(a => a.StartsAt <= t);
        if (professionalId is { } p) q = q.Where(a => a.ProfessionalId == p);
        if (status is { } s)         q = q.Where(a => a.Status == s);
        if (customerId is { } c)     q = q.Where(a => a.CustomerId == c);
        if (subjectId is { } sub)    q = q.Where(a => a.SubjectId == sub);
        return await q.OrderBy(a => a.StartsAt).ToListAsync(ct);
    }

    public async Task<bool> HasOverlapAsync(
        Guid professionalId, DateTime startsAt, DateTime endsAt, Guid? excludeId, CancellationToken ct = default)
        => await _context.SvcAppointments.AnyAsync(a =>
            a.ProfessionalId == professionalId &&
            (a.Status == SvcAppointmentStatus.Scheduled ||
             a.Status == SvcAppointmentStatus.Confirmed ||
             a.Status == SvcAppointmentStatus.InProgress) &&
            a.StartsAt < endsAt && a.EndsAt > startsAt &&
            (excludeId == null || a.Id != excludeId), ct);

    public async Task AddAsync(SvcAppointment entity, CancellationToken ct = default)
        => await _context.SvcAppointments.AddAsync(entity, ct);

    public void Update(SvcAppointment entity) => _context.SvcAppointments.Update(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
