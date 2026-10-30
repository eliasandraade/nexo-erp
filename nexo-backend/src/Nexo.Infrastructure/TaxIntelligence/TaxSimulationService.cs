using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Application.Modules.TaxIntelligence.Engine;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>
/// What-if simulations. The only rows written are the simulation and its scenarios: no transaction,
/// calculation, opportunity, credit, ledger entry or split expectation is ever created from here.
/// </summary>
public sealed class TaxSimulationService
{
    private const int MaxList = 100;

    private const string AuditEntity = "Simulation";

    private readonly TaxContext _ctx;
    private readonly NexoDbContext _db;
    private readonly TaxEngineRegistry _engines;
    private readonly ICurrentTenant _tenant;
    private readonly TaxInputBuilder _inputs;
    private readonly TaxComplianceService _compliance;
    private readonly TaxCatalogService _catalog;
    private readonly TaxApprovalGate _approvals;
    private readonly TaxAuditTrail _audit;
    private readonly ILogger<TaxSimulationService> _log;

    public TaxSimulationService(TaxContext ctx, TaxEngineRegistry engines, TaxInputBuilder inputs, TaxCatalogService catalog,
        TaxApprovalGate approvals, ILogger<TaxSimulationService> log)
    {
        _ctx = ctx;
        _db = ctx.Db;
        _engines = engines;
        _tenant = ctx.Tenant;
        _inputs = inputs;
        _compliance = inputs.Compliance;
        _catalog = catalog;
        _approvals = approvals;
        _audit = ctx.Audit;
        _log = log;
    }

    public async Task<TaxSimulationDto> CreateAsync(CreateTaxSimulationRequest r, CancellationToken ct)
    {
        await AdmitAsync("Simulação", ct);
        var profile = await _inputs.ProfileAsync(r.Baseline.OperationDate, ct);
        var compliance = await _compliance.InForceAsync(r.Baseline.OperationDate, ct);
        var baseline = TaxCalculationService.ToInput(r.Baseline, profile, compliance?.ForRules);
        var scenarios = new List<TaxScenario>();
        foreach (var s in r.Scenarios.Select(ToScenario))
        {
            // An assessment never extends past its period: a scenario on another date uses the
            // compliance in force on that date (unknown when none covers it).
            if (s.ReferenceDate is { } date && date != baseline.ReferenceDate)
            {
                var atDate = await _compliance.InForceAsync(date, ct);
                scenarios.Add(s with { Profile = baseline.Profile with { AccessoryObligationsCompliant = atDate?.ForRules } });
            }
            else scenarios.Add(s);
        }
        var engine = _engines.Current;
        // What each scenario actually runs with is what TaxSimulator.Apply makes of it: the baseline first, then each scenario.
        var runs = scenarios.Select(s => TaxSimulator.Apply(baseline, s)).Prepend(baseline)
            .Select(x => new TaxRun(x.ReferenceDate, x.Profile.Regime.ToString())).ToList();
        var (catalogue, governance) = await LoadCatalogueAsync(engine, runs, "Simulação", ct);
        var results = TaxSimulator.Compare(engine, baseline, scenarios, catalogue);

        // The governance each scenario ran under — its own date and regime — is frozen with it; a later approval change
        // never rewrites a stored simulation. The simulation keeps the baseline's, as before.
        var simulation = TaxSimulation.Create(_tenant.Id, r.Name, engine.Version, _audit.UserId, _audit.UserName, r.Assumptions, r.Caveats,
            engine.Canonical.Serialize(governance[runs[0]]));
        foreach (var (res, i) in results.Select((x, i) => (x, i)))
            simulation.AddScenario(i, res.Name, res.IsBaseline, engine.Canonical.Serialize(res.Input), engine.Canonical.Serialize(res),
                    res.TaxTotal, res.EffectiveCost)
                .FreezeGovernance(engine.Canonical.Serialize(
                    new TaxScenarioGovernanceDto(i, res.Name, runs[i].Date, runs[i].Regime, governance[runs[i]])));
        _db.TaxSimulations.Add(simulation);
        _audit.Record(TaxAuditActions.SimulationCreated, "Simulation", simulation.Id,
            $"Simulação \"{r.Name}\" com {scenarios.Count} cenário(s).",
            new { scenarios = scenarios.Count, hypotheses = results.SelectMany(x => x.Hypotheses).Distinct().Count() });
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Tax simulation {SimulationId} with {ScenarioCount} scenarios", simulation.Id, scenarios.Count);
        return Map(simulation);
    }

    public async Task<TaxPage<TaxSimulationSummaryDto>> ListAsync(string? search, int skip, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, MaxList);
        skip = Math.Max(0, skip);
        var q = _db.TaxSimulations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(s => EF.Functions.ILike(s.Name, $"%{search.Trim()}%"));
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(s => s.CreatedAt).Skip(skip).Take(take)
            .Select(s => new TaxSimulationSummaryDto(s.Id, s.Name, s.Scenarios.Count, s.CreatedAt, s.CreatedByName))
            .ToListAsync(ct);
        return new TaxPage<TaxSimulationSummaryDto>(rows, total, skip, take);
    }

    /// <summary>
    /// The scenarios as CSV for spreadsheets (base, calculated tax, credit estimate, effective cost, hypotheses). Values are
    /// calculated figures under the scenario's assumptions — never a saving. Audited.
    /// </summary>
    public async Task<(string FileName, byte[] Content)> ExportCsvAsync(Guid id, CancellationToken ct)
    {
        var s = await GetAsync(id, ct);
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("cenario;base;imposto_calculado;estimativa_credito;custo_efetivo;sem_efeito_caixa_no_periodo;hipoteses;premissas;ressalvas");
        foreach (var x in s.Scenarios)
            csv.AppendLine(string.Join(';', Csv(x.Name), x.Output.TotalBase.ToString(System.Globalization.CultureInfo.InvariantCulture),
                x.TaxTotal.ToString(System.Globalization.CultureInfo.InvariantCulture), x.CreditEstimate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                x.EffectiveCost.ToString(System.Globalization.CultureInfo.InvariantCulture), x.NoCashEffectInPeriod ? "sim" : "nao",
                Csv(string.Join(" | ", x.Hypotheses)), Csv(s.Assumptions ?? ""), Csv(s.Caveats ?? "")));
        _audit.Record(TaxAuditActions.SimulationExported, AuditEntity, id, $"Simulação \"{s.Name}\" exportada (CSV).");
        await _db.SaveChangesAsync(ct);
        return ($"simulacao-{id:N}.csv", System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv.ToString())).ToArray());
    }

    /// <summary>A CSV cell: quoted, inner quotes doubled, and never starting with a formula character.</summary>
    internal static string Csv(string value)
    {
        var v = value.Replace("\r", " ").Replace("\n", " ");
        if (v.Length > 0 && "=+-@".Contains(v[0])) v = "'" + v;
        return $"\"{v.Replace("\"", "\"\"")}\"";
    }

    public async Task<TaxSimulationDto> GetAsync(Guid id, CancellationToken ct)
    {
        var s = await _db.TaxSimulations.AsNoTracking().Include(x => x.Scenarios).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Simulação", id);
        return Map(s);
    }

    /// <summary>
    /// Effective cost of the same purchase from different suppliers: price minus the credit the buyer
    /// could take. In memory only — the procurement module can call this without writing anything.
    /// </summary>
    public async Task<IReadOnlyList<SupplierOfferResultDto>> CompareSuppliersAsync(CompareSuppliersRequest r, CancellationToken ct)
    {
        await AdmitAsync("Comparação de fornecedores", ct);
        var profile = await _inputs.ProfileAsync(r.OperationDate, ct);
        var compliance = await _compliance.InForceAsync(r.OperationDate, ct);
        var hypotheses = r.RateHypotheses?.Select(h => new RateHypothesis(h.Tax, h.RatePercent, h.Label)).ToList();
        var engine = _engines.Current;
        var (catalogue, _) = await LoadCatalogueAsync(engine, [new TaxRun(r.OperationDate, profile.Regime.ToString())], "Comparação de fornecedores", ct);

        // Each offer runs on its own facts: a supplier whose regime is unknown stays unknown, never
        // inheriting another offer's regime, and the result does not depend on the order of the offers.
        var results = r.Offers.Select(o => TaxSimulator.RunAlone(engine,
            TaxCalculationService.ToInput(
                new TaxOperationRequest(r.OperationDate, TaxDirection.Inbound, o.SupplierRegime, null, o.Supplier, o.Lines), profile,
                compliance?.ForRules),
            new TaxScenario(o.Supplier, RateHypotheses: hypotheses, AssumeCreditable: r.AssumeCreditable), catalogue)).ToList();
        var cheapest = results.Min(x => x.EffectiveCost);
        return results.Select(x => new SupplierOfferResultDto(x.Name, x.Charged, x.TaxTotal, x.CreditEstimate, x.EffectiveCost,
            x.EffectiveCost == cheapest, x.Hypotheses, x.Output.Warnings)).ToList();
    }

    /// <summary>Kill switch, approval mode and pilot scope apply to simulations too (fail closed).</summary>
    private async Task AdmitAsync(string what, CancellationToken ct)
    {
        if (await _approvals.AdmitCalculationAsync(ct) is { } refusal) await RefuseAsync(what, refusal, ct);
    }

    /// <summary>A date and the establishment's regime a scenario actually runs with.</summary>
    private sealed record TaxRun(DateOnly Date, string Regime);

    /// <summary>
    /// The rules of every (date, regime) the simulation actually runs with — the baseline's and each scenario's own.
    /// For each one, the rules that would run on that date must back new work in the approval mode for that regime,
    /// exactly as for a calculation: a scenario that moves to another regime is checked against the reviews of that
    /// regime, never the baseline's. Any combination not covered refuses the whole simulation (fail closed), naming
    /// every rule involved.
    /// </summary>
    private async Task<(IReadOnlyCollection<TaxEngineRule> Rules, Dictionary<TaxRun, TaxGovernanceDto> Governance)> LoadCatalogueAsync(
        ITaxEngine engine, IReadOnlyList<TaxRun> runs, string what, CancellationToken ct)
    {
        var rules = new Dictionary<Guid, TaxEngineRule>();
        var byDate = new Dictionary<DateOnly, IReadOnlyCollection<TaxEngineRule>>();
        var governanceByRun = new Dictionary<TaxRun, TaxGovernanceDto>();
        TaxGovernanceDto? refused = null;
        var notApproved = new Dictionary<Guid, TaxRuleGovernanceDto>();
        foreach (var run in runs.Distinct().OrderBy(x => x.Date).ThenBy(x => x.Regime, StringComparer.Ordinal))
        {
            if (!byDate.TryGetValue(run.Date, out var atDate))
            {
                atDate = await _catalog.LoadForDateAsync(run.Date, engine.Canonical, ct);
                byDate[run.Date] = atDate;
                foreach (var rule in atDate) rules[rule.VersionId] = rule;
            }
            var governance = await _approvals.ForCalculationAsync(
                engine.SelectActive(atDate, run.Date).Select(x => x.VersionId).ToList(), run.Date, ct, run.Regime);
            governanceByRun[run] = governance;
            if (!governance.Enforced || governance.NotApproved.Count == 0) continue;
            refused ??= governance;
            foreach (var rule in governance.Rules.Where(x => !TaxGovernance.Satisfies(governance.Mode,
                         x.TechnicalStatus == TaxRuleStatus.Active, x.ApprovedForPilot, x.ProfessionallyApproved)))
                notApproved.TryAdd(rule.RuleVersionId, rule);
        }
        if (refused is not null)
        {
            var all = refused with { Rules = notApproved.Values.OrderBy(x => x.Key, StringComparer.Ordinal).ThenBy(x => x.Version).ToList() };
            await RefuseAsync(what, TaxApprovalGate.NotApproved(all), ct, all.NotApproved);
        }
        return (rules.Values.ToList(), governanceByRun);
    }

    /// <summary>Records the refused attempt with its reason, then answers 422. Nothing is simulated or written.</summary>
    private async Task RefuseAsync(string what, TaxGateRefusal refusal, CancellationToken ct, IReadOnlyList<string>? details = null)
    {
        _audit.Record(TaxAuditActions.SimulationRefused, "Simulation", Guid.NewGuid(),
            $"{what} recusada ({refusal.Code}).",
            new { what, refusal.Code, mode = _approvals.Mode.ToString(), notApproved = details });
        await _db.SaveChangesAsync(ct);
        _ctx.Metrics.Refused("simulation", refusal.Code);
        _log.LogInformation("Tax simulation refused: {Code}", refusal.Code);
        throw new DomainException(refusal.Message, refusal.Code, details ?? []);
    }

    private static TaxScenario ToScenario(TaxScenarioRequest s) => new(
        s.Name, s.Regime, s.ReferenceDate, s.Direction, s.CounterpartyRegime,
        s.Lines is null ? null : TaxCalculationService.ToLines(s.Lines),
        s.ConvertConditionalDiscounts,
        s.RateHypotheses?.Select(h => new RateHypothesis(h.Tax, h.RatePercent, h.Label)).ToList(),
        s.AssumeCreditable);

    /// <summary>Scenarios are read with the canonical form of the engine that produced them.</summary>
    private TaxSimulationDto Map(TaxSimulation s)
    {
        var canonical = _engines.TryGet(s.EngineVersion, out var engine)
            ? engine.Canonical
            : throw new DomainException(
                $"Esta simulação foi feita pela engine {s.EngineVersion}, que não está disponível nesta versão do Orken.", "tax_engine_unavailable");
        return new TaxSimulationDto(s.Id, s.Name, s.EngineVersion, s.CreatedAt,
            s.Scenarios.OrderBy(x => x.ScenarioIndex).Select(x => canonical.Deserialize<ScenarioResult>(x.ResultJson)).ToList(),
            s.Assumptions, s.Caveats, s.CreatedByName,
            s.GovernanceJson is null ? null : canonical.Deserialize<TaxGovernanceDto>(s.GovernanceJson),
            s.Scenarios.Where(x => x.GovernanceJson is not null).OrderBy(x => x.ScenarioIndex)
                .Select(x => canonical.Deserialize<TaxScenarioGovernanceDto>(x.GovernanceJson!)).ToList());
    }
}
