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
            .Select(o => new { o.Id, o.ProfessionalId, o.AppointmentId })
            .ToListAsync(ct);
        var orderProfessional = orders.ToDictionary(o => o.Id, o => o.ProfessionalId);
        var rawItems = await _context.SvcOrderItems.AsNoTracking()
            .Where(i => orderIds.Contains(i.OrderId))
            .OrderBy(i => i.CreatedAt).ThenBy(i => i.Id)
            .Select(i => new { i.Id, i.OrderId, i.CatalogItemId, i.ProfessionalId, i.TotalAmount })
            .ToListAsync(ct);

        // Items a (not reversed) package consumption paid for: pointed at directly, or — for a
        // comanda opened from a package-paid appointment — its first item of the same service.
        var covered = (await _context.SvcPackageUsages.AsNoTracking()
                .Where(u => u.ReversedAt == null && u.OrderItemId != null && orderIds.Contains(u.OrderId!.Value))
                .Select(u => u.OrderItemId!.Value)
                .ToListAsync(ct))
            .ToHashSet();
        var apptOrders = orders.Where(o => o.AppointmentId != null).ToDictionary(o => o.AppointmentId!.Value, o => o.Id);
        var apptIds = apptOrders.Keys.ToList();
        var paidByAppointment = await _context.SvcPackageUsages.AsNoTracking()
            .Where(u => u.ReversedAt == null && u.OrderItemId == null && u.AppointmentId != null && apptIds.Contains(u.AppointmentId!.Value))
            .Select(u => new { AppointmentId = u.AppointmentId!.Value, u.CatalogItemId })
            .ToListAsync(ct);
        foreach (var u in paidByAppointment)
        {
            var orderId = apptOrders[u.AppointmentId];
            var first = rawItems.FirstOrDefault(i => i.OrderId == orderId && i.CatalogItemId == u.CatalogItemId && !covered.Contains(i.Id));
            if (first is not null) covered.Add(first.Id);
        }

        var items = rawItems
            .Select(i => new SvcFinanceOrderItemRow(
                i.OrderId, i.ProfessionalId ?? (orderProfessional.TryGetValue(i.OrderId, out var op) ? op : null),
                i.TotalAmount, covered.Contains(i.Id)))
            .ToList();

        var usages = await _context.SvcPackageUsages.AsNoTracking()
            .Where(u => u.CreatedAt >= fromUtc && u.CreatedAt < toUtc && u.ReversedAt == null)
            .Select(u => new SvcFinanceUsageRow(u.ProfessionalId, u.BaseAmountSnapshot ?? 0m))
            .ToListAsync(ct);

        var commissions = await _context.SvcCommissionEntries.AsNoTracking()
            .Where(c => c.RecognizedAt >= fromUtc && c.RecognizedAt < toUtc)
            .Select(c => new SvcFinanceCommissionRow(c.ProfessionalId, c.Kind, c.CommissionAmount, c.ReversedAt, c.PayoutId))
            .ToListAsync(ct);

        var byStatus = await _context.SvcAppointments.AsNoTracking()
            .Where(a => a.StartsAt >= fromUtc && a.StartsAt < toUtc)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

        var soldQuery = _context.SvcCustomerPackages.AsNoTracking()
            .Where(cp => cp.CreatedAt >= fromUtc && cp.CreatedAt < toUtc && cp.Status != SvcCustomerPackageStatus.Cancelled);
        var soldCount = await soldQuery.CountAsync(ct);
        var soldValue = soldCount == 0 ? 0m : await soldQuery.SumAsync(cp => cp.PriceSnapshot, ct);

        var names = await _context.SvcProfessionals.AsNoTracking()
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        return new SvcFinanceRaw(payments, items, orderProfessional, usages, commissions, byStatus, soldCount, soldValue, names);
    }
}
