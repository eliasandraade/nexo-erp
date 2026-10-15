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
        if (from.Year < 2000 || to.Year > 2100) throw new DomainException("Período fora do intervalo aceito.");
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

        // Credit each order payment to the professionals of its items, pro rata to the cent. Items a
        // package paid for are left out (that money came from the package). Keys: Guid.Empty = nobody.
        var credit = new Dictionary<Guid, decimal>();
        void Add(Guid? who, decimal amount) { var k = who ?? Guid.Empty; credit[k] = (credit.TryGetValue(k, out var v) ? v : 0m) + amount; }
        var itemsByOrder = raw.OrderItems.GroupBy(i => i.OrderId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var p in paid.Where(p => p.OrderId is not null))
        {
            var orderId = p.OrderId!.Value;
            var items = itemsByOrder.TryGetValue(orderId, out var list)
                ? list.Where(i => i.TotalAmount > 0m && !i.CoveredByPackage).ToList() : [];
            if (items.Count == 0)
            {
                Add(raw.OrderProfessionals.TryGetValue(orderId, out var op) ? op : null, p.Amount);
                continue;
            }
            var shares = Allocate(p.Amount, items.Select(i => i.TotalAmount).ToList());
            for (var k = 0; k < items.Count; k++) Add(items[k].ProfessionalId, shares[k]);
        }

        var packageServices = raw.Usages.GroupBy(u => u.ProfessionalId ?? Guid.Empty).ToDictionary(g => g.Key, g => g.Sum(u => u.BaseAmount));
        // A reversed earning counts only if it had been paid out (its negative entry then discounts it).
        var commission = raw.Commissions
            .Where(c => c.Kind != SvcCommissionEntryKind.Earning || c.ReversedAt is null || c.PayoutId is not null)
            .GroupBy(c => c.ProfessionalId).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

        var who = credit.Keys.Concat(packageServices.Keys).Concat(commission.Keys).Distinct();
        var byProfessional = who
            .Select(id => new SvcFinanceProfessionalDto(
                id == Guid.Empty ? null : id,
                id != Guid.Empty && raw.ProfessionalNames.TryGetValue(id, out var n) ? n : "Sem profissional",
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

        int Count(SvcAppointmentStatus s) => raw.AppointmentsByStatus.TryGetValue(s, out var n) ? n : 0;
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
                Total: raw.AppointmentsByStatus.Values.Sum(),
                Completed: Count(SvcAppointmentStatus.Completed),
                Cancelled: Count(SvcAppointmentStatus.Cancelled),
                NoShow: Count(SvcAppointmentStatus.NoShow),
                Open: Count(SvcAppointmentStatus.Scheduled) + Count(SvcAppointmentStatus.Confirmed) + Count(SvcAppointmentStatus.InProgress)));
    }

    /// <summary>
    /// Splits <paramref name="amount"/> over the weights to the cent (largest remainder): every
    /// share is the weight's floor share, and the cents left go to the largest fractions — the
    /// result always adds up exactly and never goes negative.
    /// </summary>
    public static IReadOnlyList<decimal> Allocate(decimal amount, IReadOnlyList<decimal> weights)
    {
        var total = weights.Sum();
        var cents = (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
        var exact = weights.Select(w => cents * w / total).ToList();
        var floors = exact.Select(e => (long)Math.Floor(e)).ToList();
        var left = cents - floors.Sum();
        foreach (var k in exact.Select((e, i) => (Frac: e - Math.Floor(e), i)).OrderByDescending(x => x.Frac).ThenBy(x => x.i).Take((int)left))
            floors[k.i]++;
        return floors.Select(f => f / 100m).ToList();
    }
}
