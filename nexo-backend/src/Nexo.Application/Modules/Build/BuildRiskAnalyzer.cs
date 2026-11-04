namespace Nexo.Application.Modules.Build;

public sealed record BuildRiskLateMaterial(Guid Id, string Name, DateOnly NeededBy, Guid? StageId);
public sealed record BuildRiskChangeOrder(Guid Id, int Number, string Title, string Status, int ScheduleImpactDays, decimal CostImpact);
public sealed record BuildRiskNonConformity(Guid Id, int Number, string Title, string Severity, Guid? StageId, DateOnly? DueDate);

public sealed record BuildDelayRiskInput(
    Guid ProjectId,
    DateOnly Today,
    DateOnly? ExpectedEnd,
    BuildProgressResult Progress,
    IReadOnlyList<BuildRiskLateMaterial> LateMaterials,
    IReadOnlyList<BuildRiskChangeOrder> PendingChangeOrders,
    IReadOnlyList<BuildRiskNonConformity> OpenNonConformities);

public sealed record BuildCostRiskStage(Guid? StageId, string Name, decimal Budgeted, decimal Realized, decimal ActualPercent);
public sealed record BuildUnlinkedMaterial(Guid Id, string Name, decimal EstimatedValue);

public sealed record BuildCostRiskInput(
    Guid ProjectId,
    DateOnly Today,
    IReadOnlyList<BuildCostRiskStage> Stages,
    decimal ApprovedChangeOrdersTotal,
    int ApprovedChangeOrdersCount,
    IReadOnlyList<BuildUnlinkedMaterial> UnlinkedMaterials);

/// <summary>
/// Deterministic schedule and cost risk (methodology <c>build-risk/1</c>) — the numbers the Build AI may only explain.
/// Pure; every item has a stable key and its evidence.
///
/// <list type="bullet">
/// <item><b>SPI-like</b> = actual % ÷ planned % (weighted stages, Wave 1 calculator); null without a plan or with planned 0.
///   &lt; 0.9 medium, &lt; 0.75 high.</item>
/// <item><b>Delayed stage</b>: planned end before today and &lt; 100% — &gt; 7 days medium, &gt; 30 days high.</item>
/// <item><b>Forecast after the contract end</b>: forecast delivery − expected end; &gt; 0 medium, &gt; 30 days high.</item>
/// <item>Late materials (needed before today, not delivered): medium, high after 14 days. Pending change orders with schedule
///   impact: low (≤ 7 days) or medium. Open non-conformities: Critical → high, High → medium.</item>
/// <item><b>Earned value</b> = Σ stage budget × actual %; <b>CPI-like</b> = EV ÷ realized (null when nothing was realized or
///   there is no budget); <b>EAC</b> = budget ÷ CPI (null when CPI is null or 0). CPI &lt; 0.95 medium, &lt; 0.85 high;
///   EAC &gt; 105% of the budget medium, &gt; 115% high. Stage realized above its budget: medium (high above 120%); realized on
///   a stage without budget: low. Approved change orders and materials ordered but not linked to an expense: informative.</item>
/// </list>
/// </summary>
public static class BuildRiskAnalyzer
{
    private static readonly System.Globalization.CultureInfo PtBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    public const string Methodology = "build-risk/1";
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";
    public const string None = "none";

    public static BuildRiskAnalysisDto Delay(BuildDelayRiskInput i)
    {
        var p = i.Progress;
        var spi = Spi(p.ActualPercent, p.PlannedPercent);
        var items = new List<BuildRiskItemDto>();

        if (spi is { } s && s < 0.9m)
            items.Add(Item("spi_low", "schedule_performance", s < 0.75m ? High : Medium, "Ritmo abaixo do planejado",
                string.Create(PtBr, $"Avanço real {p.ActualPercent:0.#}% contra {p.PlannedPercent:0.#}% planejado (SPI {s:0.00})."),
                new() { ["spi"] = s, ["actualPercent"] = p.ActualPercent, ["plannedPercent"] = p.PlannedPercent }, []));

        var delayed = 0;
        foreach (var st in p.Stages.Where(st => st.PlannedEnd is { } end && end < i.Today && st.ActualPercent < 100m).OrderBy(st => st.Order))
        {
            delayed++;
            var days = i.Today.DayNumber - st.PlannedEnd!.Value.DayNumber;
            items.Add(Item($"stage_delayed:{st.StageId}", "stage_delayed", days > 30 ? High : days > 7 ? Medium : Low,
                $"Etapa atrasada: {st.Name}",
                string.Create(PtBr, $"Fim planejado em {st.PlannedEnd:dd/MM/yyyy}, {st.ActualPercent:0.#}% concluída ({days} dia(s) após o fim)."),
                new() { ["daysLate"] = days, ["actualPercent"] = st.ActualPercent }, [st.StageId.ToString()]));
        }

        decimal? slip = null;
        if (p.ForecastDelivery is { } fd && i.ExpectedEnd is { } ee)
        {
            slip = fd.DayNumber - ee.DayNumber;
            if (slip > 0)
                items.Add(Item("forecast_after_contract", "forecast_delivery", slip > 30 ? High : Medium, "Previsão de entrega depois do prazo",
                    string.Create(PtBr, $"Previsão {fd:dd/MM/yyyy} contra término contratado {ee:dd/MM/yyyy} ({slip} dia(s))."),
                    new() { ["slipDays"] = slip }, []));
        }

        foreach (var m in i.LateMaterials.OrderBy(m => m.NeededBy))
        {
            var days = i.Today.DayNumber - m.NeededBy.DayNumber;
            items.Add(Item($"late_material:{m.Id}", "late_material", days > 14 ? High : Medium, $"Material atrasado: {m.Name}",
                string.Create(PtBr, $"Necessário em {m.NeededBy:dd/MM/yyyy}, ainda não entregue ({days} dia(s))."),
                new() { ["daysLate"] = days }, m.StageId is { } sid ? [m.Id.ToString(), sid.ToString()] : [m.Id.ToString()]));
        }

        foreach (var co in i.PendingChangeOrders.Where(c => c.ScheduleImpactDays > 0).OrderBy(c => c.Number))
            items.Add(Item($"pending_change_order:{co.Id}", "pending_change_order", co.ScheduleImpactDays > 7 ? Medium : Low,
                $"Aditivo pendente nº {co.Number}: {co.Title}",
                $"Aditivo {co.Status} com impacto de {co.ScheduleImpactDays} dia(s) no prazo, ainda sem decisão.",
                new() { ["scheduleImpactDays"] = co.ScheduleImpactDays }, [co.Id.ToString()]));

        foreach (var nc in i.OpenNonConformities.Where(n => n.Severity is "Critical" or "High").OrderBy(n => n.Number))
            items.Add(Item($"open_nc:{nc.Id}", "open_nonconformity", nc.Severity == "Critical" ? High : Medium,
                $"Não conformidade aberta nº {nc.Number}: {nc.Title}",
                nc.Severity == "Critical"
                    ? "NC crítica aberta: a etapa não pode chegar a 100% enquanto ela estiver aberta."
                    : "NC de gravidade alta aberta." + (nc.DueDate is { } due ? string.Create(PtBr, $" Prazo {due:dd/MM/yyyy}.") : ""),
                new(), nc.StageId is { } ns ? [nc.Id.ToString(), ns.ToString()] : [nc.Id.ToString()]));

        var metrics = new Dictionary<string, decimal?>
        {
            ["spi"] = spi,
            ["actualPercent"] = p.ActualPercent,
            ["plannedPercent"] = p.PlannedPercent,
            ["delayedStages"] = delayed,
            ["forecastSlipDays"] = slip,
            ["lateMaterials"] = i.LateMaterials.Count,
            ["pendingChangeOrders"] = i.PendingChangeOrders.Count,
            ["openCriticalNonConformities"] = i.OpenNonConformities.Count(n => n.Severity == "Critical"),
        };
        return new BuildRiskAnalysisDto(i.ProjectId, "delay", i.Today, Level(items), metrics, items, Methodology);
    }

    public static BuildRiskAnalysisDto Cost(BuildCostRiskInput i)
    {
        var budget = i.Stages.Sum(s => s.Budgeted);
        var realized = i.Stages.Sum(s => s.Realized);
        var earned = Math.Round(i.Stages.Sum(s => s.Budgeted * Math.Clamp(s.ActualPercent, 0m, 100m) / 100m), 2);
        var cpi = Cpi(earned, realized, budget);
        var eac = Eac(budget, cpi);
        var items = new List<BuildRiskItemDto>();

        if (cpi is { } c && c < 0.95m)
            items.Add(Item("cpi_low", "cost_performance", c < 0.85m ? High : Medium, "Custo acima do avanço",
                string.Create(PtBr, $"Valor agregado {Money(earned)} contra {Money(realized)} realizado (CPI {c:0.00})."),
                new() { ["cpi"] = c, ["earnedValue"] = earned, ["realized"] = realized }, []));
        if (eac is { } e && budget > 0 && e > budget * 1.05m)
            items.Add(Item("eac_over_budget", "estimate_at_completion", e > budget * 1.15m ? High : Medium, "Estimativa no término acima do orçamento",
                string.Create(PtBr, $"Estimativa no término {Money(e)} contra orçamento {Money(budget)} ({(e / budget - 1m) * 100m:0.#}% acima)."),
                new() { ["eac"] = e, ["budget"] = budget, ["variance"] = budget - e }, []));

        foreach (var s in i.Stages.Where(s => s.StageId is not null))
        {
            if (s.Budgeted > 0 && s.Realized > s.Budgeted)
                items.Add(Item($"stage_overrun:{s.StageId}", "stage_overrun", s.Realized > s.Budgeted * 1.2m ? High : Medium,
                    $"Etapa acima do orçado: {s.Name}",
                    string.Create(PtBr, $"Realizado {Money(s.Realized)} contra {Money(s.Budgeted)} orçado, com {s.ActualPercent:0.#}% concluído."),
                    new() { ["budgeted"] = s.Budgeted, ["realized"] = s.Realized, ["actualPercent"] = s.ActualPercent }, [s.StageId!.Value.ToString()]));
            else if (s.Budgeted == 0 && s.Realized > 0)
                items.Add(Item($"stage_no_budget:{s.StageId}", "stage_without_budget", Low, $"Gasto em etapa sem orçamento: {s.Name}",
                    $"Realizado {Money(s.Realized)} numa etapa sem valor no orçamento de referência.",
                    new() { ["realized"] = s.Realized }, [s.StageId!.Value.ToString()]));
        }

        if (i.ApprovedChangeOrdersTotal != 0)
            items.Add(Item("change_orders_approved", "change_orders", Low, "Aditivos aprovados",
                $"{i.ApprovedChangeOrdersCount} aditivo(s) aprovado(s) somando {Money(i.ApprovedChangeOrdersTotal)}.",
                new() { ["total"] = i.ApprovedChangeOrdersTotal, ["count"] = i.ApprovedChangeOrdersCount }, []));

        var unlinked = i.UnlinkedMaterials.Sum(m => m.EstimatedValue);
        if (i.UnlinkedMaterials.Count > 0)
            items.Add(Item("unlinked_materials", "unlinked_materials", i.UnlinkedMaterials.Count > 5 ? Medium : Low,
                "Materiais pedidos sem lançamento",
                $"{i.UnlinkedMaterials.Count} material(is) pedido(s) sem lançamento financeiro vinculado (estimado {Money(unlinked)}).",
                new() { ["count"] = i.UnlinkedMaterials.Count, ["estimated"] = unlinked }, i.UnlinkedMaterials.Select(m => m.Id.ToString()).Take(50).ToList()));

        var metrics = new Dictionary<string, decimal?>
        {
            ["budget"] = budget,
            ["realized"] = realized,
            ["earnedValue"] = earned,
            ["cpi"] = cpi,
            ["eac"] = eac,
            ["vac"] = eac is { } ev ? budget - ev : null,
            ["approvedChangeOrders"] = i.ApprovedChangeOrdersTotal,
            ["budgetWithChangeOrders"] = budget + i.ApprovedChangeOrdersTotal,
            ["unlinkedMaterialsEstimated"] = unlinked,
        };
        return new BuildRiskAnalysisDto(i.ProjectId, "cost", i.Today, Level(items), metrics, items, Methodology);
    }

    public static decimal? Spi(decimal actual, decimal? planned)
        => planned is { } pl && pl > 0m ? Math.Round(actual / pl, 3) : null;

    public static decimal? Cpi(decimal earnedValue, decimal realized, decimal budget)
        => realized > 0m && budget > 0m ? Math.Round(earnedValue / realized, 3) : null;

    public static decimal? Eac(decimal budget, decimal? cpi)
        => cpi is { } c && c > 0m ? Math.Round(budget / c, 2) : null;

    private static string Level(IReadOnlyList<BuildRiskItemDto> items)
        => items.Any(x => x.Severity == High) ? High : items.Any(x => x.Severity == Medium) ? Medium : items.Count > 0 ? Low : None;

    private static string Money(decimal v) => v.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

    private static BuildRiskItemDto Item(string key, string kind, string severity, string title, string evidence,
        Dictionary<string, decimal?> metrics, IReadOnlyList<string> refs)
        => new(key, kind, severity, title, evidence, metrics, refs);
}
