using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Build;
using Nexo.Application.Modules.Build.Interfaces;
using Nexo.Domain.Modules.Build;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Assembles the Build dashboard read-model for the current tenant.
/// Realized expenses come exclusively from confirmed outbound FinancialMovements
/// with ContextType=Obra. Tenant isolation is enforced by the global EF query filter.
///
/// Wave 6 (additive): the portfolio of the active projects (progress, forecast vs expected end, cost vs contract, CPI-like,
/// open NCs by severity, assistance, pending change orders, late materials, portal, last diary, pending AI suggestions),
/// totals and top risks — with a fixed number of queries whatever the number of projects (everything is batched by the
/// project ids and grouped in SQL; progress/forecast via <see cref="BuildProgressEngine.ComputeManyAsync"/>).
/// </summary>
public class BuildDashboardQueryService : IBuildDashboardQueryService
{
    private const int MaxTopRisks = 10;

    private readonly NexoDbContext _context;
    private readonly ICurrentTenant _tenant;
    private readonly BuildProgressEngine _engine;

    public BuildDashboardQueryService(NexoDbContext context, ICurrentTenant tenant, BuildProgressEngine engine)
    {
        _context = context;
        _tenant = tenant;
        _engine = engine;
    }

    public async Task<BuildDashboardDto> GetAsync(CancellationToken ct = default)
    {
        var today = BusinessClock.Today();
        var tid = _tenant.Id;

        // Project status counts + budget totals + overdue — single pass.
        var projects = await _context.BldProjects
            .Select(p => new { p.Id, p.Name, p.ClientName, p.Status, p.ExpectedEndDate, p.BudgetEstimated, p.BudgetApproved })
            .ToListAsync(ct);

        int planning   = projects.Count(p => p.Status == BuildProjectStatus.Planning);
        int inProgress = projects.Count(p => p.Status == BuildProjectStatus.InProgress);
        int paused     = projects.Count(p => p.Status == BuildProjectStatus.Paused);
        int completed  = projects.Count(p => p.Status == BuildProjectStatus.Completed);
        int cancelled  = projects.Count(p => p.Status == BuildProjectStatus.Cancelled);
        int overdue    = projects.Count(p =>
            p.ExpectedEndDate != null
            && p.ExpectedEndDate < today
            && p.Status != BuildProjectStatus.Completed
            && p.Status != BuildProjectStatus.Cancelled);

        decimal totalEstimated = projects.Sum(p => p.BudgetEstimated ?? 0m);
        decimal totalApproved  = projects.Sum(p => p.BudgetApproved ?? 0m);

        // Realized expenses per project (confirmed Obra outbound) — one grouped query; the tenant total is their sum.
        var realizedGroups = await _context.IntMovements
            .Where(m => m.ContextType == FinancialContextType.Obra
                     && m.Status      == MovementStatus.Confirmed
                     && m.Direction   == MovementDirection.Out)
            .GroupBy(m => m.ContextId)
            .Select(g => new { ProjectId = g.Key, Total = g.Sum(m => m.Amount) })
            .ToListAsync(ct);
        var realizedByProject = realizedGroups.Where(x => x.ProjectId != null).ToDictionary(x => x.ProjectId!.Value, x => x.Total);
        decimal totalRealized = realizedGroups.Sum(x => x.Total);

        // Average stage progress across all stages (null-safe on empty).
        double avgProgress = await _context.BldStages
            .Select(s => (double?)s.ProgressPercent)
            .AverageAsync(ct) ?? 0d;

        // Recent expenses: last 5 confirmed Obra outbound movements, with project name.
        var recent = await (
            from m in _context.IntMovements
            join p in _context.BldProjects on m.ContextId equals (Guid?)p.Id
            where m.ContextType == FinancialContextType.Obra
               && m.Status      == MovementStatus.Confirmed
               && m.Direction   == MovementDirection.Out
            orderby m.Date descending, m.CreatedAt descending
            select new BuildRecentExpenseDto(p.Id, p.Name, m.Amount, m.Date, m.Description))
            .Take(5)
            .ToListAsync(ct);

        // ── Wave 6: portfolio of the active projects ──
        var active = projects.Where(p => p.Status is BuildProjectStatus.Planning or BuildProjectStatus.InProgress or BuildProjectStatus.Paused)
            .OrderBy(p => p.Name).ToList();
        var ids = active.Select(p => p.Id).ToList();
        var portfolio = new List<BuildDashboardProjectDto>();
        if (ids.Count > 0)
        {
            var computed = await _engine.ComputeManyAsync(tid, ids, today, bypassFilters: false, ct);

            var orders = await _context.BldChangeOrders
                .Where(o => ids.Contains(o.ProjectId) && (o.Status == BuildChangeOrderStatus.Approved
                         || o.Status == BuildChangeOrderStatus.Draft || o.Status == BuildChangeOrderStatus.SentToClient))
                .GroupBy(o => new { o.ProjectId, o.Status })
                .Select(g => new { g.Key.ProjectId, g.Key.Status, Count = g.Count(), Cost = g.Sum(o => o.CostImpact) })
                .ToListAsync(ct);
            var ncs = await _context.BldNonConformities
                .Where(n => ids.Contains(n.ProjectId)
                         && (n.Status == BuildNonConformityStatus.Open || n.Status == BuildNonConformityStatus.InTreatment))
                .GroupBy(n => new { n.ProjectId, n.Severity })
                .Select(g => new { g.Key.ProjectId, g.Key.Severity, Count = g.Count() })
                .ToListAsync(ct);
            var assistance = await _context.BldAssistanceRequests
                .Where(a => ids.Contains(a.ProjectId) && (a.Status == BuildAssistanceStatus.Open || a.Status == BuildAssistanceStatus.Triaged
                         || a.Status == BuildAssistanceStatus.Scheduled || a.Status == BuildAssistanceStatus.InProgress))
                .GroupBy(a => a.ProjectId).Select(g => new { ProjectId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ProjectId, x => x.Count, ct);
            var lateMaterials = await _context.BldMaterialRequests
                .Where(m => ids.Contains(m.ProjectId) && m.NeededBy != null && m.NeededBy < today
                         && m.Status != BuildMaterialRequestStatus.Delivered && m.Status != BuildMaterialRequestStatus.Cancelled)
                .GroupBy(m => m.ProjectId).Select(g => new { ProjectId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ProjectId, x => x.Count, ct);
            var published = (await _context.BldProjectPortals
                .Where(p => ids.Contains(p.ProjectId) && p.IsPublished).Select(p => p.ProjectId).ToListAsync(ct)).ToHashSet();
            var lastDiary = await _context.BldDailyLogs
                .Where(l => ids.Contains(l.ProjectId))
                .GroupBy(l => l.ProjectId).Select(g => new { ProjectId = g.Key, Last = g.Max(l => l.Date) })
                .ToDictionaryAsync(x => x.ProjectId, x => x.Last, ct);
            var nowUtc = DateTime.UtcNow;
            var pendingAi = await _context.BldAiSuggestions
                .Where(s => ids.Contains(s.ProjectId) && s.Status == BuildAiSuggestionStatus.Pending
                         && (s.ExpiresAt == null || s.ExpiresAt > nowUtc))
                .GroupBy(s => s.ProjectId).Select(g => new { ProjectId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ProjectId, x => x.Count, ct);

            foreach (var p in active)
            {
                var c = computed.GetValueOrDefault(p.Id);
                var approvedCo = orders.Where(o => o.ProjectId == p.Id && o.Status == BuildChangeOrderStatus.Approved).Sum(o => o.Cost);
                var pendingCo = orders.Where(o => o.ProjectId == p.Id && o.Status != BuildChangeOrderStatus.Approved).Sum(o => o.Count);
                var baseContract = p.BudgetApproved ?? p.BudgetEstimated;
                decimal? contract = baseContract is { } b ? b + approvedCo : approvedCo != 0 ? approvedCo : null;
                var realized = realizedByProject.GetValueOrDefault(p.Id);
                var actual = c?.Result.ActualPercent ?? 0m;
                decimal? cpi = realized > 0m && contract is { } ct2 && ct2 > 0m ? Math.Round(actual / 100m * ct2 / realized, 3) : null;
                var forecast = c?.Result.ForecastDelivery;
                int? late = forecast is { } fd && p.ExpectedEndDate is { } ee ? fd.DayNumber - ee.DayNumber : null;
                var ncBySeverity = Enum.GetValues<BuildNonConformitySeverity>()
                    .ToDictionary(s => s.ToString(), s => ncs.Where(n => n.ProjectId == p.Id && n.Severity == s).Sum(n => n.Count));

                portfolio.Add(new BuildDashboardProjectDto(p.Id, p.Name, p.ClientName, p.Status.ToString(), actual, c?.Result.PlannedPercent,
                    p.ExpectedEndDate, forecast, late, c?.Result.Methodology ?? BuildProgressCalculator.Methodology, realized, contract, cpi,
                    ncBySeverity, assistance.GetValueOrDefault(p.Id), pendingCo, lateMaterials.GetValueOrDefault(p.Id),
                    published.Contains(p.Id), lastDiary.TryGetValue(p.Id, out var last) ? last : null, pendingAi.GetValueOrDefault(p.Id)));
            }
        }

        var totals = new BuildDashboardTotalsDto(
            ActiveProjects: portfolio.Count,
            LateProjects: portfolio.Count(p => p.DaysLate > 0),
            CostRealized: portfolio.Sum(p => p.CostRealized),
            ContractTotal: portfolio.Sum(p => p.ContractTotal ?? 0m),
            OpenNonConformities: portfolio.Sum(p => p.OpenNonConformities.Values.Sum()),
            OpenCriticalNonConformities: portfolio.Sum(p => p.OpenNonConformities.GetValueOrDefault(nameof(BuildNonConformitySeverity.Critical))),
            OpenAssistance: portfolio.Sum(p => p.OpenAssistance),
            PendingChangeOrders: portfolio.Sum(p => p.PendingChangeOrders),
            LateMaterials: portfolio.Sum(p => p.LateMaterials),
            PublishedPortals: portfolio.Count(p => p.PortalPublished),
            PendingAiSuggestions: portfolio.Sum(p => p.PendingAiSuggestions));

        return new BuildDashboardDto(
            TotalProjects:    projects.Count,
            PlanningCount:    planning,
            InProgressCount:  inProgress,
            PausedCount:      paused,
            CompletedCount:   completed,
            CancelledCount:   cancelled,
            OverdueCount:     overdue,
            TotalEstimated:   totalEstimated,
            TotalApproved:    totalApproved,
            TotalRealized:    totalRealized,
            Balance:          totalApproved - totalRealized,
            AvgStageProgress: Math.Round(avgProgress, 1),
            RecentExpenses:   recent,
            Projects:         portfolio,
            Totals:           totals,
            TopRisks:         TopRisks(portfolio),
            AsOf:             today);
    }

    /// <summary>
    /// The most serious signals of the portfolio (deterministic): forecast after the expected end (&gt; 30 days high, else medium),
    /// open critical NCs (high), CPI-like below 0.85 (high) / 0.95 (medium), late materials (medium). Ordered by severity, then size.
    /// </summary>
    public static IReadOnlyList<BuildDashboardRiskDto> TopRisks(IEnumerable<BuildDashboardProjectDto> projects)
    {
        var risks = new List<BuildDashboardRiskDto>();
        foreach (var p in projects)
        {
            if (p.DaysLate is { } late && late > 0)
                risks.Add(new(p.ProjectId, p.Name, "forecast_late", late > 30 ? "high" : "medium",
                    $"Previsão de entrega {late} dia(s) depois do término contratado.", late));
            var critical = p.OpenNonConformities.GetValueOrDefault(nameof(BuildNonConformitySeverity.Critical));
            if (critical > 0)
                risks.Add(new(p.ProjectId, p.Name, "critical_nonconformity", "high", $"{critical} não conformidade(s) crítica(s) aberta(s).", critical));
            if (p.CpiLike is { } cpi && cpi < 0.95m)
                risks.Add(new(p.ProjectId, p.Name, "cost_performance", cpi < 0.85m ? "high" : "medium",
                    $"Custo acima do avanço (CPI {cpi:0.00}).", cpi));
            if (p.LateMaterials > 0)
                risks.Add(new(p.ProjectId, p.Name, "late_material", "medium", $"{p.LateMaterials} material(is) atrasado(s).", p.LateMaterials));
        }
        return risks
            .OrderBy(r => r.Severity == "high" ? 0 : 1)
            .ThenByDescending(r => r.Kind == "cost_performance" ? 1m - (r.Value ?? 1m) : r.Value ?? 0m)
            .Take(MaxTopRisks).ToList();
    }
}
