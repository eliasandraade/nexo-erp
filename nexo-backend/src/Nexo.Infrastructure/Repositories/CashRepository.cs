using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Cash;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories;

public class CashRepository : ICashRepository
{
    private readonly NexoDbContext _context;

    public CashRepository(NexoDbContext context) => _context = context;

    public async Task LockSessionAsync(Guid sessionId, Guid tenantId, CancellationToken ct = default)
    {
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM nexo.cash_sessions WHERE id = {sessionId} AND tenant_id = {tenantId} FOR UPDATE", ct);
        var tracked = _context.ChangeTracker.Entries<CashSession>().FirstOrDefault(e => e.Entity.Id == sessionId);
        if (tracked is not null) await tracked.ReloadAsync(ct);
    }

    public async Task LockUserOpeningAsync(Guid userId, CancellationToken ct = default)
    {
        var key = BitConverter.ToInt64(userId.ToByteArray(), 0);
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    }

    public async Task<IReadOnlyList<CashPaymentTotalDto>> GetPaymentTotalsBySessionAsync(
        Guid sessionId, CancellationToken ct = default)
    {
        var rows = await _context.SalePayments
            .Where(p => p.Sale!.CashSessionId == sessionId
                        && (p.Sale.Status == SaleStatus.Confirmed || p.Sale.Status == SaleStatus.Paid
                            || p.Sale.Status == SaleStatus.PartiallyReturned || p.Sale.Status == SaleStatus.Returned))
            .GroupBy(p => new { p.Method, p.Type })
            .Select(g => new { g.Key.Method, g.Key.Type, Count = g.Count(), Amount = g.Sum(p => p.Amount) })
            .ToListAsync(ct);
        return rows
            .OrderByDescending(r => r.Amount)
            .Select(r => new CashPaymentTotalDto(r.Method.ToString(), r.Type.ToString(), r.Count, r.Amount))
            .ToList();
    }

    public async Task<CashSession?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.CashSessions.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<CashSession?> GetOpenSessionByUserAsync(Guid userId, CancellationToken ct = default)
        => await _context.CashSessions.FirstOrDefaultAsync(
            x => x.Status == CashSessionStatus.Open && x.OpenedByUserId == userId, ct);

    public async Task<CashSession?> GetOpenSessionAsync(CancellationToken ct = default)
        => await _context.CashSessions
            .Where(x => x.Status == CashSessionStatus.Open)
            .OrderBy(x => x.OpenedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CashMovement>> GetMovementsByReferenceAsync(
        string referenceType, Guid referenceId, CancellationToken ct = default)
        => await _context.CashMovements
            .Where(x => x.ReferenceType == referenceType && x.ReferenceId == referenceId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CashSession>> GetAllAsync(CancellationToken ct = default)
        => await _context.CashSessions
            .OrderByDescending(x => x.OpenedAt)
            .ToListAsync(ct);

    public async Task AddSessionAsync(CashSession session, CancellationToken ct = default)
        => await _context.CashSessions.AddAsync(session, ct);

    public async Task AddMovementAsync(CashMovement movement, CancellationToken ct = default)
        => await _context.CashMovements.AddAsync(movement, ct);

    public async Task<IReadOnlyList<CashMovement>> GetMovementsBySessionAsync(Guid sessionId, CancellationToken ct = default)
        => await _context.CashMovements
            .Where(x => x.CashSessionId == sessionId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
