using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>
/// The Tax Intelligence dashboard, from stored data only (nothing estimated for display). Open
/// opportunities are summed by the nature of their impact — savings, deferrals, cash flow and credits
/// are separate figures and are never added up.
/// </summary>
public sealed class TaxOverviewService
{
    private readonly NexoDbContext _db;
    private readonly TaxCreditService _credits;
    private readonly TaxSplitPaymentService _split;

    public TaxOverviewService(NexoDbContext db, TaxCreditService credits, TaxSplitPaymentService split)
    {
        _db = db;
        _credits = credits;
        _split = split;
    }

    public async Task<TaxOverviewDto> GetAsync(DateOnly today, CancellationToken ct)
    {
        var from = today.AddDays(-29);
        var profileConfigured = await _db.TaxProfileVersions.AnyAsync(v => v.ValidFrom <= today, ct);

        var calcs = await _db.TaxCalculations.AsNoTracking()
            .Where(c => c.ReferenceDate >= from && c.ReferenceDate <= today)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Base = g.Sum(c => c.TotalBase), Tax = g.Sum(c => c.TotalTax) })
            .FirstOrDefaultAsync(ct);

        var credits = await _credits.TotalsAsync(ct);

        var open = await _db.TaxOpportunities.AsNoTracking()
            .Where(o => o.Status == TaxOpportunityStatus.Identified || o.Status == TaxOpportunityStatus.InReview)
            .Select(o => new { o.Type, o.EstimatedImpact, o.Risk })
            .ToListAsync(ct);
        decimal Sum(TaxImpactNature n) => open.Where(o => TaxOpportunity.NatureOf(o.Type) == n).Sum(o => o.EstimatedImpact);
        var blocked = await _db.TaxOpportunities.CountAsync(o => o.Status == TaxOpportunityStatus.Blocked, ct);

        var divergences = await _split.CountDivergencesAsync(ct);

        var rulesToday = await _db.TaxRuleVersions.AsNoTracking()
            .Where(v => v.ValidFrom <= today && (v.ValidTo == null || v.ValidTo >= today))
            .GroupBy(v => v.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);

        var taxedBase = calcs?.Base ?? 0m;
        var tax = calcs?.Tax ?? 0m;
        return new TaxOverviewDto(
            profileConfigured, from, today, calcs?.Count ?? 0, taxedBase, tax,
            taxedBase > 0 ? decimal.Round(tax / taxedBase * 100m, 4) : null,
            credits.Sum(c => c.Potential), credits.Sum(c => c.ToBeAppropriated), credits.Sum(c => c.Available), credits.Sum(c => c.Blocked),
            open.Count, Sum(TaxImpactNature.DefinitiveSaving), Sum(TaxImpactNature.CalculatedTaxReduction), Sum(TaxImpactNature.Deferral), Sum(TaxImpactNature.CashFlow),
            Sum(TaxImpactNature.Credit), open.Count(o => o.Risk == TaxRiskLevel.High), blocked, divergences,
            rulesToday.Where(r => r.Key == TaxRuleStatus.Active).Sum(r => r.Count),
            rulesToday.Where(r => r.Key is TaxRuleStatus.PendingRegulation or TaxRuleStatus.Unverified).Sum(r => r.Count));
    }
}
