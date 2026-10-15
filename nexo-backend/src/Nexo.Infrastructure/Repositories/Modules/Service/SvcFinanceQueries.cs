using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

/// <summary>Read side of the Service finance panel. Query filters keep it inside the current tenant/store.</summary>
public class SvcFinanceQueries : ISvcFinanceQueries
{
    private readonly NexoDbContext _context;
    public SvcFinanceQueries(NexoDbContext context) => _context = context;

    public async Task<SvcFinanceRaw> LoadAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var payments = await _context.SvcPayments.AsNoTracking()
            .Where(p => (p.PaidAt >= fromUtc && p.PaidAt < toUtc) || (p.VoidedAt >= fromUtc && p.VoidedAt < toUtc))
            .Select(p => new SvcFinancePaymentRow(p.Id, p.OrderId, p.CustomerPackageId, p.Amount, p.Method, p.Status, p.PaidAt, p.VoidedAt))
            .ToListAsync(ct);

        var orderIds = payments.Where(p => p.OrderId is not null).Select(p => p.OrderId!.Value).Distinct().ToList();
        var orders = await _context.SvcOrders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.ProfessionalId })
            .ToListAsync(ct);
        var orderProfessional = orders.ToDictionary(o => o.Id, o => o.ProfessionalId);
        var items = (await _context.SvcOrderItems.AsNoTracking()
                .Where(i => orderIds.Contains(i.OrderId))
                .Select(i => new { i.OrderId, i.ProfessionalId, i.TotalAmount })
                .ToListAsync(ct))
            .Select(i => new SvcFinanceOrderItemRow(
                i.OrderId, i.ProfessionalId ?? (orderProfessional.TryGetValue(i.OrderId, out var op) ? op : null), i.TotalAmount))
            .ToList();

        var usages = await _context.SvcPackageUsages.AsNoTracking()
            .Where(u => u.CreatedAt >= fromUtc && u.CreatedAt < toUtc && u.ReversedAt == null)
            .Select(u => new SvcFinanceUsageRow(u.ProfessionalId, u.BaseAmountSnapshot ?? 0m))
            .ToListAsync(ct);

        var commissions = await _context.SvcCommissionEntries.AsNoTracking()
            .Where(c => c.RecognizedAt >= fromUtc && c.RecognizedAt < toUtc)
            .Select(c => new SvcFinanceCommissionRow(c.ProfessionalId, c.Kind, c.CommissionAmount, c.ReversedAt, c.PayoutId))
            .ToListAsync(ct);

        var statuses = await _context.SvcAppointments.AsNoTracking()
            .Where(a => a.StartsAt >= fromUtc && a.StartsAt < toUtc)
            .Select(a => a.Status)
            .ToListAsync(ct);

        var sold = await _context.SvcCustomerPackages.AsNoTracking()
            .Where(cp => cp.CreatedAt >= fromUtc && cp.CreatedAt < toUtc && cp.Status != SvcCustomerPackageStatus.Cancelled)
            .Select(cp => cp.PriceSnapshot)
            .ToListAsync(ct);

        var names = await _context.SvcProfessionals.AsNoTracking()
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        return new SvcFinanceRaw(payments, items, orderProfessional, usages, commissions, statuses, sold.Count, sold.Sum(), names);
    }
}
