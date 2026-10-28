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

    private readonly NexoDbContext _db;
    private readonly TaxEngineRegistry _engines;
    private readonly ICurrentTenant _tenant;
    private readonly TaxProfileService _profiles;
    private readonly TaxComplianceService _compliance;
    private readonly TaxCatalogService _catalog;
    private readonly TaxApprovalGate _approvals;
    private readonly TaxAuditTrail _audit;
    private readonly ILogger<TaxSimulationService> _log;

    public TaxSimulationService(NexoDbContext db, TaxEngineRegistry engines, ICurrentTenant tenant, TaxProfileService profiles,
        TaxComplianceService compliance, TaxCatalogService catalog, TaxApprovalGate approvals,
        TaxAuditTrail audit, ILogger<TaxSimulationService> log)
    {
        _db = db;
        _engines = engines;
        _tenant = tenant;
        _profiles = profiles;
        _compliance = compliance;
        _catalog = catalog;
        _approvals = approvals;
        _audit = audit;
        _log = log;
    }

    public async Task<TaxSimulationDto> CreateAsync(CreateTaxSimulationRequest r, CancellationToken ct)
    {
        await AdmitAsync(ct);
        var profile = await _profiles.GetInForceAsync(r.Baseline.OperationDate, ct);
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
        var catalogue = await LoadCatalogueAsync(engine, baseline.ReferenceDate, scenarios, ct);
        var results = TaxSimulator.Compare(engine, baseline, scenarios, catalogue);

        var simulation = TaxSimulation.Create(_tenant.Id, r.Name, engine.Version, _audit.UserId);
        foreach (var (res, i) in results.Select((x, i) => (x, i)))
            simulation.AddScenario(i, res.Name, res.IsBaseline, engine.Canonical.Serialize(res.Input), engine.Canonical.Serialize(res),
                res.TaxTotal, res.EffectiveCost);
        _db.TaxSimulations.Add(simulation);
        _audit.Record(TaxAuditActions.SimulationCreated, "Simulation", simulation.Id,
            $"Simulação \"{r.Name}\" com {scenarios.Count} cenário(s).",
            new { scenarios = scenarios.Count, hypotheses = results.SelectMany(x => x.Hypotheses).Distinct().Count() });
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Tax simulation {SimulationId} with {ScenarioCount} scenarios", simulation.Id, scenarios.Count);
        return Map(simulation);
    }

    public async Task<IReadOnlyList<TaxSimulationSummaryDto>> ListAsync(int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, MaxList);
        return await _db.TaxSimulations.AsNoTracking().OrderByDescending(s => s.CreatedAt).Take(take)
            .Select(s => new TaxSimulationSummaryDto(s.Id, s.Name, s.Scenarios.Count, s.CreatedAt))
            .ToListAsync(ct);
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
        await AdmitAsync(ct);
        var profile = await _profiles.GetInForceAsync(r.OperationDate, ct);
        var compliance = await _compliance.InForceAsync(r.OperationDate, ct);
        var hypotheses = r.RateHypotheses?.Select(h => new RateHypothesis(h.Tax, h.RatePercent, h.Label)).ToList();
        var engine = _engines.Current;
        var catalogue = await LoadCatalogueAsync(engine, r.OperationDate, [], ct);

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
    private async Task AdmitAsync(CancellationToken ct)
    {
        if (await _approvals.AdmitCalculationAsync(ct) is { } refusal) throw new DomainException(refusal.Message, refusal.Code);
    }

    private async Task<IReadOnlyCollection<TaxEngineRule>> LoadCatalogueAsync(
        ITaxEngine engine, DateOnly baseDate, IEnumerable<TaxScenario> scenarios, CancellationToken ct)
    {
        var dates = scenarios.Select(s => s.ReferenceDate ?? baseDate).Append(baseDate).Distinct().ToList();
        var rules = new Dictionary<Guid, TaxEngineRule>();
        foreach (var d in dates)
            foreach (var rule in await _catalog.LoadForDateAsync(d, engine.Canonical, ct))
                rules[rule.VersionId] = rule;
        return rules.Values.ToList();
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
            s.Scenarios.OrderBy(x => x.ScenarioIndex).Select(x => canonical.Deserialize<ScenarioResult>(x.ResultJson)).ToList());
    }
}
