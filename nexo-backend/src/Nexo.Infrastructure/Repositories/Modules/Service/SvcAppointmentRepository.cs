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

    public async Task LockProfessionalAsync(Guid professionalId, CancellationToken ct = default)
    {
        const long salt = 0x5C0_0000_0000_0056;   // stable across processes
        var key = BitConverter.ToInt64(professionalId.ToByteArray(), 0) ^ salt;
        try
        {
            // bounded wait (this transaction only): a stalled one can't hang every booking of the professional
            await _context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", ct);
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "55P03")   // lock_not_available
        {
            throw new Nexo.Domain.Exceptions.ConflictException("A agenda deste profissional está sendo alterada agora. Tente de novo.");
        }
    }

    public async Task<bool> IsBlockedAsync(Guid professionalId, DateTime startsAt, DateTime endsAt, CancellationToken ct = default)
        => await _context.SvcTimeBlocks.AnyAsync(b =>
            (b.ProfessionalId == null || b.ProfessionalId == professionalId) &&
            b.StartsAt < endsAt && b.EndsAt > startsAt, ct);

    public async Task<IReadOnlyList<SvcTimeBlock>> GetBlocksPublicAsync(
        Guid professionalId, Guid tenantId, Guid storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
        => await _context.SvcTimeBlocks.IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId && b.StoreId == storeId &&
                        (b.ProfessionalId == null || b.ProfessionalId == professionalId) &&
                        b.StartsAt < toUtc && b.EndsAt > fromUtc)
            .ToListAsync(ct);

    public async Task<SvcAppointment?> GetByPublicTokenAsync(
        string token, Guid tenantId, Guid storeId, CancellationToken ct = default)
        => await _context.SvcAppointments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.PublicToken == token && a.TenantId == tenantId && a.StoreId == storeId, ct);

    public async Task LockByPublicTokenAsync(string token, Guid tenantId, Guid storeId, CancellationToken ct = default)
        => await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM nexo.svc_appointments WHERE public_token = {token} AND tenant_id = {tenantId} AND store_id = {storeId} FOR UPDATE", ct);

    public async Task LockRowAsync(Guid id, Guid tenantId, CancellationToken ct = default)
        => await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM nexo.svc_appointments WHERE id = {id} AND tenant_id = {tenantId} FOR UPDATE", ct);

    public async Task<bool> HasChargesPublicAsync(Guid appointmentId, Guid tenantId, CancellationToken ct = default)
        => await _context.SvcOrders.IgnoreQueryFilters().AnyAsync(o =>
               o.TenantId == tenantId && o.AppointmentId == appointmentId && o.Status != SvcOrderStatus.Cancelled, ct)
           || await _context.SvcPackageUsages.IgnoreQueryFilters().AnyAsync(u =>
               u.TenantId == tenantId && u.AppointmentId == appointmentId && u.ReversedAt == null, ct);

    public async Task<bool> IsBlockedPublicAsync(
        Guid professionalId, Guid tenantId, Guid storeId, DateTime startsAt, DateTime endsAt, CancellationToken ct = default)
        => await _context.SvcTimeBlocks.IgnoreQueryFilters().AnyAsync(b =>
            b.TenantId == tenantId && b.StoreId == storeId &&
            (b.ProfessionalId == null || b.ProfessionalId == professionalId) &&
            b.StartsAt < endsAt && b.EndsAt > startsAt, ct);

    public async Task<bool> HasOverlapAsync(
        Guid professionalId, DateTime startsAt, DateTime endsAt, Guid? excludeId, CancellationToken ct = default)
        => await _context.SvcAppointments.AnyAsync(a =>
            a.ProfessionalId == professionalId &&
            (a.Status == SvcAppointmentStatus.Scheduled ||
             a.Status == SvcAppointmentStatus.Confirmed ||
             a.Status == SvcAppointmentStatus.InProgress) &&
            a.StartsAt < endsAt && a.EndsAt > startsAt &&
            (excludeId == null || a.Id != excludeId), ct);

    public async Task<IReadOnlyList<SvcAppointment>> GetBlockingForProfessionalPublicAsync(
        Guid professionalId, Guid tenantId, Guid storeId, DateTime fromUtc, DateTime toUtc,
        CancellationToken ct = default)
        => await _context.SvcAppointments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a =>
                a.ProfessionalId == professionalId &&
                a.TenantId == tenantId && a.StoreId == storeId &&
                (a.Status == SvcAppointmentStatus.Scheduled ||
                 a.Status == SvcAppointmentStatus.Confirmed ||
                 a.Status == SvcAppointmentStatus.InProgress) &&
                a.StartsAt < toUtc && a.EndsAt > fromUtc)
            .OrderBy(a => a.StartsAt)
            .ToListAsync(ct);

    public async Task<bool> HasOverlapPublicAsync(
        Guid professionalId, Guid tenantId, Guid storeId, DateTime startsAt, DateTime endsAt,
        CancellationToken ct = default)
        => await _context.SvcAppointments
            .IgnoreQueryFilters()
            .AnyAsync(a =>
                a.ProfessionalId == professionalId &&
                a.TenantId == tenantId && a.StoreId == storeId &&
                (a.Status == SvcAppointmentStatus.Scheduled ||
                 a.Status == SvcAppointmentStatus.Confirmed ||
                 a.Status == SvcAppointmentStatus.InProgress) &&
                a.StartsAt < endsAt && a.EndsAt > startsAt, ct);

    public async Task AddAsync(SvcAppointment entity, CancellationToken ct = default)
        => await _context.SvcAppointments.AddAsync(entity, ct);

    public void Update(SvcAppointment entity) => _context.SvcAppointments.Update(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
