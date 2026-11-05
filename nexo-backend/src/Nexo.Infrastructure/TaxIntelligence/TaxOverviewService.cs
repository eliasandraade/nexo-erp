using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Application.Common.Interfaces;
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
    private readonly ICurrentTenant _tenant;
    private readonly TaxCreditService _credits;
    private readonly TaxSplitPaymentService _split;
    private readonly TaxComplianceService _compliance;

    public TaxOverviewService(TaxContext ctx, TaxCreditService credits, TaxSplitPaymentService split, TaxComplianceService compliance)
    {
        _db = ctx.Db;
        _tenant = ctx.Tenant;
        _credits = credits;
        _split = split;
        _compliance = compliance;
    }

    public async Task<TaxOverviewDto> GetAsync(DateOnly today, CancellationToken ct)
    {
        var from = today.AddDays(-29);
        var profileConfigured = await _db.TaxProfileVersions.AnyAsync(v => v.ValidFrom <= today, ct);

        // Outbound tax (the establishment's own operations) and inbound tax (stated by suppliers) are different things.
        var byDirection = await _db.TaxCalculations.AsNoTracking()
            .Where(c => c.ReferenceDate >= from && c.ReferenceDate <= today)
            .Join(_db.TaxTransactions.AsNoTracking(), c => c.TransactionId, t => t.Id, (c, t) => new { t.Direction, c.TotalBase, c.TotalTax })
            .GroupBy(x => x.Direction)
            .Select(g => new { g.Key, Count = g.Count(), Base = g.Sum(c => c.TotalBase), Tax = g.Sum(c => c.TotalTax) })
            .ToListAsync(ct);
        var outbound = byDirection.FirstOrDefault(x => x.Key == TaxDirection.Outbound);
        var inbound = byDirection.FirstOrDefault(x => x.Key == TaxDirection.Inbound);
        var calcs = new { Count = byDirection.Sum(x => x.Count), Base = outbound?.Base ?? 0m, Tax = outbound?.Tax ?? 0m };

        var credits = await _credits.TotalsAsync(ct);

        var open = await _db.TaxOpportunities.AsNoTracking()
            .Where(o => o.Status == TaxOpportunityStatus.Identified || o.Status == TaxOpportunityStatus.InReview)
            .Select(o => new { o.Type, o.EstimatedImpact, o.Risk })
            .ToListAsync(ct);
        decimal Sum(TaxImpactNature n) => open.Where(o => TaxOpportunity.NatureOf(o.Type) == n).Sum(o => o.EstimatedImpact);
        var blocked = await _db.TaxOpportunities.CountAsync(o => o.Status == TaxOpportunityStatus.Blocked, ct);
        var awaitingExecution = await _db.TaxOpportunities.CountAsync(o => o.Status == TaxOpportunityStatus.Approved, ct);

        var divergences = await _split.CountDivergencesAsync(ct);

        var versionsToday = await _db.TaxRuleVersions.AsNoTracking()
            .Where(v => v.ValidFrom <= today && (v.ValidTo == null || v.ValidTo >= today)).ToListAsync(ct);
        var rulesToday = versionsToday.GroupBy(v => v.Status).Select(g => new { g.Key, Count = g.Count() }).ToList();
        var reviewPending = versionsToday.Count(v => v.Status == TaxRuleStatus.Active && !v.IsProfessionallyApprovedOn(today));
        var compliance = await _compliance.InForceAsync(today, ct);
        var refusals = await RefusalsAsync(from, ct);

        var taxedBase = calcs.Base;
        var tax = calcs.Tax;
        return new TaxOverviewDto(
            profileConfigured, from, today, calcs.Count, taxedBase, tax,
            taxedBase > 0 ? decimal.Round(tax / taxedBase * 100m, 4) : null,
            credits.Sum(c => c.Potential), credits.Sum(c => c.ToBeAppropriated), credits.Sum(c => c.Available), credits.Sum(c => c.Blocked),
            open.Count, Sum(TaxImpactNature.DefinitiveSaving), Sum(TaxImpactNature.CalculatedTaxReduction), Sum(TaxImpactNature.Deferral), Sum(TaxImpactNature.CashFlow),
            Sum(TaxImpactNature.Credit), open.Count(o => o.Risk == TaxRiskLevel.High), blocked, divergences,
            rulesToday.Where(r => r.Key == TaxRuleStatus.Active).Sum(r => r.Count),
            rulesToday.Where(r => r.Key == TaxRuleStatus.PendingRegulation).Sum(r => r.Count),
            outbound?.Tax ?? 0m, inbound?.Tax ?? 0m, credits.Sum(c => c.Appropriated), credits.Sum(c => c.Used), awaitingExecution,
            rulesToday.Where(r => r.Key == TaxRuleStatus.Unverified).Sum(r => r.Count), reviewPending,
            compliance?.Status, compliance?.PeriodTo,
            refusals.Where(r => !r.Key.StartsWith("tax_", StringComparison.Ordinal)).Sum(r => r.Value),
            refusals.Where(r => !r.Key.StartsWith("tax_", StringComparison.Ordinal)).ToDictionary(r => r.Key, r => r.Value),
            refusals.Where(r => r.Key.StartsWith("tax_", StringComparison.Ordinal)).Sum(r => r.Value));
    }

    /// <summary>
    /// Refused calculations of this company in the period, by reason: a coverage status (operation outside the matrix) or a governance
    /// code ("tax_…", e.g. module off, not in the pilot). Read from the audit trail, bounded.
    /// </summary>
    private async Task<Dictionary<string, int>> RefusalsAsync(DateOnly from, CancellationToken ct)
    {
        var since = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var tenant = _tenant.Id;
        var descriptions = await _db.AuditRecords.AsNoTracking()
            .Where(a => a.TenantId == tenant && a.ActionType == TaxAuditActions.CalculationRefused && a.CreatedAt >= since)
            .OrderByDescending(a => a.CreatedAt).Take(5000).Select(a => a.Description).ToListAsync(ct);
        return descriptions.Select(TaxModuleService.RefusalCode).Where(c => c is not null).GroupBy(c => c!)
            .ToDictionary(g => g.Key, g => g.Count());
    }
}
