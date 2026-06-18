using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcOrderRepository : ISvcOrderRepository
{
    private readonly NexoDbContext _context;
    public SvcOrderRepository(NexoDbContext context) => _context = context;

    public async Task<SvcOrder?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcOrders.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<SvcOrder?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcOrders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SvcOrder>> GetAllAsync(
        SvcOrderStatus? status, Guid? customerId, Guid? subjectId, Guid? professionalId,
        Guid? appointmentId, CancellationToken ct = default)
    {
        var q = _context.SvcOrders.AsQueryable();
        if (status is { } s)         q = q.Where(o => o.Status == s);
        if (customerId is { } c)     q = q.Where(o => o.CustomerId == c);
        if (subjectId is { } sub)    q = q.Where(o => o.SubjectId == sub);
        if (professionalId is { } p) q = q.Where(o => o.ProfessionalId == p);
        if (appointmentId is { } a)  q = q.Where(o => o.AppointmentId == a);
        return await q.OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
    }

    public async Task<bool> ExistsForAppointmentAsync(Guid appointmentId, CancellationToken ct = default)
        => await _context.SvcOrders.AnyAsync(o => o.AppointmentId == appointmentId, ct);

    public async Task AddAsync(SvcOrder entity, CancellationToken ct = default)
        => await _context.SvcOrders.AddAsync(entity, ct);

    public void Update(SvcOrder entity) => _context.SvcOrders.Update(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
