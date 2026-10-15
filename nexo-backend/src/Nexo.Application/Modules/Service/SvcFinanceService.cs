using Nexo.Application.Common;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Financeiro do Service por período (dias do Brasil), regime de caixa: o que entrou pelos
/// pagamentos ainda válidos, por meio e por dia; o que cada profissional gerou (comandas pagas
/// rateadas pelos itens, serviços feitos por pacote) e a comissão que ganhou; pacotes vendidos e
/// o desfecho da agenda. Somente leitura — nada aqui muda dinheiro.
/// </summary>
public class SvcFinanceService
{
    public const int MaxDays = 366;
    private readonly ISvcFinanceQueries _queries;

    public SvcFinanceService(ISvcFinanceQueries queries) => _queries = queries;

    public async Task<SvcFinanceSummaryDto> GetSummaryAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) throw new DomainException("O fim do período precisa ser depois do início.");
        if (to.DayNumber - from.DayNumber + 1 > MaxDays) throw new DomainException($"Período de até {MaxDays} dias.");
        var raw = await _queries.LoadAsync(BusinessClock.DayStartUtc(from), BusinessClock.DayStartUtc(to.AddDays(1)), ct);
        return Build(from, to, raw);
    }

    /// <summary>Pure assembly of the summary (unit-tested).</summary>
    public static SvcFinanceSummaryDto Build(DateOnly from, DateOnly to, SvcFinanceRaw raw)
    {
        var fromUtc = BusinessClock.DayStartUtc(from);
        var toUtc = BusinessClock.DayStartUtc(to.AddDays(1));
        var paid = raw.Payments
            .Where(p => p.Status == SvcPaymentStatus.Paid && p.PaidAt >= fromUtc && p.PaidAt < toUtc).ToList();
        var voided = raw.Payments
            .Where(p => p.Status == SvcPaymentStatus.Voided && p.VoidedAt >= fromUtc && p.VoidedAt < toUtc).ToList();

        var received = paid.Sum(p => p.Amount);
        var targets = paid.Select(p => p.OrderId ?? p.CustomerPackageId).Distinct().Count();

        // Credit each order payment to the professionals of its items, pro rata (cents, last one takes the rest).
        var credit = new Dictionary<Guid?, decimal>();
        void Add(Guid? who, decimal amount) => credit[who] = (credit.TryGetValue(who, out var v) ? v : 0m) + amount;
        var itemsByOrder = raw.OrderItems.GroupBy(i => i.OrderId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var p in paid.Where(p => p.OrderId is not null))
        {
            var orderId = p.OrderId!.Value;
            var items = itemsByOrder.TryGetValue(orderId, out var list) ? list.Where(i => i.TotalAmount > 0m).ToList() : [];
            var total = items.Sum(i => i.TotalAmount);
            if (total <= 0m)
            {
                Add(raw.OrderProfessionals.TryGetValue(orderId, out var op) ? op : null, p.Amount);
                continue;
            }
            var left = p.Amount;
            for (var k = 0; k < items.Count; k++)
            {
                var share = k == items.Count - 1
                    ? left
                    : Math.Round(p.Amount * items[k].TotalAmount / total, 2, MidpointRounding.AwayFromZero);
                left -= share;
                Add(items[k].ProfessionalId, share);
            }
        }

        var packageServices = raw.Usages.GroupBy(u => u.ProfessionalId).ToDictionary(g => g.Key, g => g.Sum(u => u.BaseAmount));
        // A reversed earning counts only if it had been paid out (its negative entry then discounts it).
        var commission = raw.Commissions
            .Where(c => c.Kind != SvcCommissionEntryKind.Earning || c.ReversedAt is null || c.PayoutId is not null)
            .GroupBy(c => (Guid?)c.ProfessionalId).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

        var who = credit.Keys.Concat(packageServices.Keys).Concat(commission.Keys).Distinct();
        var byProfessional = who
            .Select(id => new SvcFinanceProfessionalDto(
                id,
                id is { } g && raw.ProfessionalNames.TryGetValue(g, out var n) ? n : "Sem profissional",
                credit.TryGetValue(id, out var r) ? r : 0m,
                packageServices.TryGetValue(id, out var ps) ? ps : 0m,
                commission.TryGetValue(id, out var cm) ? cm : 0m))
            .Where(x => x.ReceivedFromOrders != 0m || x.PackageServices != 0m || x.Commission != 0m)
            .OrderByDescending(x => x.ReceivedFromOrders + x.PackageServices)
            .ThenBy(x => x.Name)
            .ToList();

        var byDay = paid
            .GroupBy(p => DateOnly.FromDateTime(BusinessClock.ToBrazil(p.PaidAt)))
            .Select(g => new SvcFinanceDayDto(g.Key, g.Sum(p => p.Amount)))
            .OrderBy(d => d.Day)
            .ToList();

        var statuses = raw.AppointmentStatuses;
        return new SvcFinanceSummaryDto(
            From: from, To: to,
            Received: received,
            PaymentsCount: paid.Count,
            AverageTicket: targets == 0 ? 0m : Math.Round(received / targets, 2, MidpointRounding.AwayFromZero),
            ReceivedFromOrders: paid.Where(p => p.OrderId is not null).Sum(p => p.Amount),
            ReceivedFromPackages: paid.Where(p => p.CustomerPackageId is not null).Sum(p => p.Amount),
            Voided: voided.Sum(p => p.Amount),
            VoidedCount: voided.Count,
            ByMethod: paid.GroupBy(p => p.Method)
                .Select(g => new SvcFinanceMethodDto(g.Key, g.Sum(p => p.Amount), g.Count()))
                .OrderByDescending(m => m.Amount).ToList(),
            ByDay: byDay,
            ByProfessional: byProfessional,
            CommissionTotal: commission.Values.Sum(),
            PackagesSold: raw.PackagesSold,
            PackagesSoldValue: raw.PackagesSoldValue,
            Appointments: new SvcFinanceAppointmentsDto(
                Total: statuses.Count,
                Completed: statuses.Count(s => s == SvcAppointmentStatus.Completed),
                Cancelled: statuses.Count(s => s == SvcAppointmentStatus.Cancelled),
                NoShow: statuses.Count(s => s == SvcAppointmentStatus.NoShow),
                Open: statuses.Count(s => s is SvcAppointmentStatus.Scheduled or SvcAppointmentStatus.Confirmed or SvcAppointmentStatus.InProgress)));
    }
}
