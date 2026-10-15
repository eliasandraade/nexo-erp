using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Financial;

public enum CashFlowGranularity { Day, Week, Month }

/// <summary>Money of one Brazil calendar day: realized (paid) or projected (open, due that day).</summary>
public record CashFlowDayRow(DateOnly Day, TransactionType Type, bool Realized, decimal Amount);

public record CashFlowBucketDto(
    DateOnly Start,
    DateOnly End,               // inclusive
    decimal RealizedIn,
    decimal RealizedOut,
    decimal ProjectedIn,
    decimal ProjectedOut,
    decimal Balance);           // closing balance of the bucket: realized so far + projected from today on

public record CashFlowDto(
    DateOnly From,
    DateOnly To,
    string Granularity,
    decimal OpeningBalance,
    bool OpeningBalanceConfigured,
    decimal OverdueIn,          // open títulos already past due: may still come in…
    decimal OverdueOut,         // …or go out — shown apart, never in the projection
    decimal ClosingBalance,
    IReadOnlyList<CashFlowBucketDto> Buckets);

/// <summary>
/// Fluxo de caixa: what came in and went out (realized, by payment date) and what is due to (projected,
/// open títulos by due date, from today on), period by period, with the running balance. The balance
/// starts from the saldo inicial of the Caixa/Banco accounts (or zero at the first lançamento when none
/// was set) plus everything realized since then.
/// </summary>
public class CashFlowService
{
    public const int MaxDays = 366;

    private readonly IFinancialRepository _financial;
    private readonly FinancialRecurrenceService _recurrences;

    public CashFlowService(IFinancialRepository financial, FinancialRecurrenceService recurrences)
    {
        _financial   = financial;
        _recurrences = recurrences;
    }

    public async Task<CashFlowDto> GetAsync(DateOnly from, DateOnly to, CashFlowGranularity granularity, CancellationToken ct = default)
    {
        if (to < from)
            throw new DomainException("O fim do período precisa ser depois do início.");
        if (to.DayNumber - from.DayNumber + 1 > MaxDays)
            throw new DomainException($"Período de até {MaxDays} dias.");

        try { await _recurrences.EnsureGeneratedAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* the projection shows what exists */ }

        var fromUtc  = BusinessClock.DayStartUtc(from);
        var toUtc    = BusinessClock.DayStartUtc(to.AddDays(1));
        var todayUtc = BusinessClock.TodayStartUtc();
        var today    = DateOnly.FromDateTime(BusinessClock.ToBrazil(DateTime.UtcNow));

        // Opening balance at `from`: saldo inicial (end of its day, one company cut-off date, inactive
        // accounts included — closing an account doesn't rewrite history) + realized since then.
        var accounts = (await _financial.GetAllAccountsAsync(includeInactive: true, ct))
            .Where(a => a.AccountType is FinancialAccountType.Cash or FinancialAccountType.Bank && a.OpeningBalanceDate.HasValue)
            .ToList();
        var configured = accounts.Count > 0;
        decimal opening;
        if (configured)
        {
            var cutoff = BusinessClock.DayStartUtc(accounts.Min(a => a.OpeningBalanceDate!.Value).AddDays(1));
            var baseAmount = accounts.Sum(a => a.OpeningBalance);
            opening = cutoff <= fromUtc
                ? baseAmount + await _financial.GetRealizedNetAsync(cutoff, fromUtc, ct)
                : baseAmount - await _financial.GetRealizedNetAsync(fromUtc, cutoff, ct);
        }
        else
        {
            opening = await _financial.GetRealizedNetAsync(null, fromUtc, ct);
        }

        // Recurring títulos not generated yet (beyond next month) are projected without being saved.
        var recurring = await RecurringProjectionAsync(today, to, ct);

        // A period starting in the future opens with what is due between today and its start.
        if (from > today)
            opening += await _financial.GetProjectedNetAsync(todayUtc, fromUtc, ct)
                       + recurring.Where(r => r.Day < from).Sum(r => r.Type == TransactionType.Receivable ? r.Amount : -r.Amount);

        var rows = (await _financial.GetCashFlowDaysAsync(fromUtc, toUtc, todayUtc, ct))
            .Concat(recurring.Where(r => r.Day >= from && r.Day >= today))
            .ToList();
        var (overdueIn, overdueOut) = await _financial.GetOverdueTotalsAsync(todayUtc, ct);

        var buckets = new List<CashFlowBucketDto>();
        var balance = opening;
        foreach (var (start, end) in Buckets(from, to, granularity))
        {
            var inBucket = rows.Where(r => r.Day >= start && r.Day <= end).ToList();
            decimal Sum(bool realized, TransactionType type)
                => inBucket.Where(r => r.Realized == realized && r.Type == type).Sum(r => r.Amount);
            var b = new CashFlowBucketDto(start, end,
                Sum(true, TransactionType.Receivable), Sum(true, TransactionType.Payable),
                Sum(false, TransactionType.Receivable), Sum(false, TransactionType.Payable), 0);
            balance += b.RealizedIn - b.RealizedOut + b.ProjectedIn - b.ProjectedOut;
            buckets.Add(b with { Balance = balance });
        }

        return new CashFlowDto(from, to, granularity.ToString(), opening, configured, overdueIn, overdueOut, balance, buckets);
    }

    /// <summary>Months of active recurrences not generated yet, as projected days in [today, to].</summary>
    private async Task<IReadOnlyList<CashFlowDayRow>> RecurringProjectionAsync(DateOnly today, DateOnly to, CancellationToken ct)
    {
        var rows = new List<CashFlowDayRow>();
        foreach (var r in await _financial.GetRecurrencesAsync(includeInactive: false, ct))
            foreach (var month in r.PendingMonths(new DateOnly(to.Year, to.Month, 1)))
            {
                var day = r.DueDayIn(month);
                if (day >= today && day <= to) rows.Add(new CashFlowDayRow(day, r.TransactionType, false, r.Amount));
            }
        return rows;
    }

    /// <summary>Calendar buckets clipped to [from, to]: days, weeks starting on Monday, or months.</summary>
    private static IEnumerable<(DateOnly Start, DateOnly End)> Buckets(DateOnly from, DateOnly to, CashFlowGranularity g)
    {
        var start = from;
        while (start <= to)
        {
            var end = g switch
            {
                CashFlowGranularity.Day   => start,
                CashFlowGranularity.Week  => start.AddDays((7 - ((int)start.DayOfWeek + 6) % 7) - 1),   // through Sunday
                _                         => new DateOnly(start.Year, start.Month, DateTime.DaysInMonth(start.Year, start.Month)),
            };
            if (end > to) end = to;
            yield return (start, end);
            start = end.AddDays(1);
        }
    }
}
