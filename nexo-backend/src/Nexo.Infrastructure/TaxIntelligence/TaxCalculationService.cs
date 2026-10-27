using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Application.Modules.TaxIntelligence.Engine;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.Persistence;
using Npgsql;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>
/// Official calculations: facts → canonical transaction → engine → stored result with its full trace.
/// The calculation key (engine version + input hash + rule-set hash) makes it idempotent: the same
/// facts under the same rules return the stored calculation. After a new calculation the opportunity
/// detectors run — they only propose, nothing is applied. New calculations use the current engine; a
/// stored one is always read, re-detected and verified with the exact engine version it records.
/// </summary>
public sealed class TaxCalculationService
{
    private const int MaxList = 200;

    private readonly NexoDbContext _db;
    private readonly TaxEngineRegistry _engines;
    private readonly ICurrentTenant _tenant;
    private readonly TaxProfileService _profiles;
    private readonly TaxCatalogService _catalog;
    private readonly TaxOpportunityService _opportunities;
    private readonly SaleTaxFactsReader _sales;
    private readonly TaxAuditTrail _audit;
    private readonly ILogger<TaxCalculationService> _log;

    public TaxCalculationService(
        NexoDbContext db, TaxEngineRegistry engines, ICurrentTenant tenant, TaxProfileService profiles, TaxCatalogService catalog,
        TaxOpportunityService opportunities, SaleTaxFactsReader sales, TaxAuditTrail audit, ILogger<TaxCalculationService> log)
    {
        _db = db;
        _engines = engines;
        _tenant = tenant;
        _profiles = profiles;
        _catalog = catalog;
        _opportunities = opportunities;
        _sales = sales;
        _audit = audit;
        _log = log;
    }

    public Task<TaxCalculationDto> CalculateAsync(TaxOperationRequest request, CancellationToken ct)
        => RunAsync(new TaxOperationFacts(TaxTransactionSource.Manual, null, request), ct);

    public async Task<TaxCalculationDto> CalculateSaleAsync(Guid saleId, CancellationToken ct)
        => await RunAsync(await _sales.ReadAsync(saleId, ct), ct);

    /// <summary>Builds the engine input of an operation on its date: the facts plus the profile version in force.</summary>
    public async Task<(TaxEngineInput Input, TaxProfileVersion Profile)> BuildInputAsync(TaxOperationRequest op, CancellationToken ct)
    {
        var profile = await _profiles.GetInForceAsync(op.OperationDate, ct);
        return (ToInput(op, profile), profile);
    }

    public static TaxEngineInput ToInput(TaxOperationRequest op, TaxProfileVersion profile) => new(
        op.OperationDate,
        new TaxEngineProfile(profile.Id, profile.Regime, profile.State, profile.CityIbgeCode, profile.ComplianceForRules),
        op.Direction,
        op.CounterpartyRegime,
        ToLines(op.Lines));

    public static IReadOnlyList<TaxEngineLine> ToLines(IReadOnlyList<TaxLineRequest> lines)
        => lines.Select((l, i) => new TaxEngineLine(
            i + 1, l.ItemKind, l.GrossAmount, l.UnconditionalDiscount, l.ConditionalDiscount, l.Surcharges,
            l.ExcludedTaxes ?? 0m, l.ExcludedTaxes is not null,
            string.IsNullOrWhiteSpace(l.SpecialTreatment) ? null : l.SpecialTreatment.Trim())).ToList();

    private async Task<TaxCalculationDto> RunAsync(TaxOperationFacts facts, CancellationToken ct)
    {
        var op = facts.Operation;
        var (input, profile) = await BuildInputAsync(op, ct);
        var engine = _engines.Current;
        var canonical = engine.Canonical;
        var catalogue = await _catalog.LoadForDateAsync(op.OperationDate, canonical, ct);

        var inputJson = canonical.Serialize(input);
        var inputHash = canonical.Hash(inputJson);
        var ruleSetHash = engine.RuleSetHash(engine.SelectActive(catalogue, op.OperationDate));
        // Same facts (all of them, description included) + same rules + same engine = the same calculation.
        var factsHash = canonical.HashOf(op);
        var key = canonical.Hash($"{engine.Version}|{inputHash}|{ruleSetHash}|{facts.Source}|{facts.SourceId}|{factsHash}");

        var existing = await _db.TaxCalculations.AsNoTracking().FirstOrDefaultAsync(c => c.CalculationKey == key, ct);
        if (existing is not null) return await GetAsync(existing.Id, reused: true, ct);

        var output = engine.Run(input, catalogue);
        var outputJson = canonical.Serialize(output);

        var transaction = TaxTransaction.Create(_tenant.Id, facts.Source, facts.SourceId, op.Direction, op.OperationDate,
            op.Description, op.CounterpartyRegime, op.CounterpartyState, factsHash, _audit.UserId);
        foreach (var (l, i) in op.Lines.Select((l, i) => (l, i)))
            transaction.AddLine(i + 1, l.ItemKind, l.Description, l.Ncm, l.Quantity, l.GrossAmount, l.UnconditionalDiscount,
                l.ConditionalDiscount, l.Surcharges, l.ExcludedTaxes ?? 0m, l.ExcludedTaxes is not null, l.SpecialTreatment);

        var calc = TaxCalculation.Create(_tenant.Id, transaction.Id, profile.Id, op.OperationDate, engine.Version,
            inputJson, inputHash, ruleSetHash, key, outputJson, canonical.Hash(outputJson), output.TotalBase, output.TotalTax, _audit.UserId);
        foreach (var line in output.Lines)
            foreach (var c in line.Components)
                calc.AddComponent(line.Index, c.Tax, c.Base, c.RatePercent, c.Amount, c.RuleVersionId);
        foreach (var e in output.Executions)
            calc.AddExecution(e.RuleVersionId, e.RuleKey, e.RuleVersion, e.ContentHash, e.LineIndex, e.Matched,
                canonical.Serialize(e.Conditions), e.Output, string.Join(",", e.LegalSourceCodes));

        _db.TaxTransactions.Add(transaction);
        _db.TaxCalculations.Add(calc);
        _audit.Record(TaxAuditActions.CalculationCreated, "Calculation", calc.Id,
            $"Cálculo de {op.Description ?? facts.Source.ToString()} em {op.OperationDate:dd/MM/yyyy}: base {output.TotalBase:N2}, tributos {output.TotalTax:N2}.",
            new { source = facts.Source.ToString(), facts.SourceId, inputHash, ruleSetHash, engine = engine.Version, rules = output.Executions.Count });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The same calculation was stored by a concurrent request: return that one.
            _db.ChangeTracker.Clear();
            var stored = await _db.TaxCalculations.AsNoTracking().FirstAsync(c => c.CalculationKey == key, ct);
            return await GetAsync(stored.Id, reused: true, ct);
        }

        _log.LogInformation(
            "Tax calculation {CalculationId} from {Source}: {RuleCount} rule runs, {ComponentCount} components, engine {EngineVersion}, input {InputHash}",
            calc.Id, facts.Source, output.Executions.Count, calc.Components.Count, engine.Version, inputHash);

        await _opportunities.DetectAsync(calc.Id, engine, input, catalogue, ct);
        return await GetAsync(calc.Id, reused: false, ct);
    }

    /// <summary>Runs the detectors again on a stored calculation (e.g. after a new detector ships). Idempotent.</summary>
    public async Task<IReadOnlyList<Guid>> DetectAgainAsync(Guid id, CancellationToken ct)
    {
        var calc = await _db.TaxCalculations.AsNoTracking().Include(c => c.Executions).FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Cálculo", id);
        // Same engine and same rules as the calculation (and the dossier that may follow).
        var engine = EngineOf(calc);
        var input = engine.Canonical.Deserialize<TaxEngineInput>(calc.InputJson);
        var output = engine.Canonical.Deserialize<TaxEngineOutput>(calc.OutputJson);
        var executed = calc.Executions.Select(e => e.RuleVersionId).ToList();
        var rules = await _catalog.LoadByIdsAsync(executed.Concat(output.NotApplied.Select(n => n.RuleVersionId)).Distinct().ToList(),
            engine.Canonical, ct);
        return await _opportunities.DetectAsync(calc.Id, engine, input, TaxReproduction.RulesAsThen(executed, output, rules), ct);
    }

    /// <summary>The engine version that made the calculation; if this build no longer has it, nothing is guessed.</summary>
    private ITaxEngine EngineOf(TaxCalculation calc)
        => _engines.TryGet(calc.EngineVersion, out var engine)
            ? engine
            : throw new DomainException(
                $"Este cálculo foi feito pela engine {calc.EngineVersion}, que não está disponível nesta versão do Orken.",
                "tax_engine_unavailable");

    public async Task<IReadOnlyList<TaxCalculationSummaryDto>> ListAsync(int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, MaxList);
        var rows = await _db.TaxCalculations.AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .Take(take)
            .Join(_db.TaxTransactions.AsNoTracking(), c => c.TransactionId, t => t.Id, (c, t) => new { c, t })
            .Select(x => new
            {
                x.c.Id, x.c.ReferenceDate, x.t.Source, x.t.SourceId, x.t.Direction, x.t.Description, x.c.TotalBase, x.c.TotalTax, x.c.CreatedAt,
                Open = _db.TaxOpportunities.Count(o => o.SourceCalculationId == x.c.Id
                    && (o.Status == TaxOpportunityStatus.Identified || o.Status == TaxOpportunityStatus.InReview)),
            })
            .ToListAsync(ct);
        return rows.OrderByDescending(r => r.CreatedAt)
            .Select(r => new TaxCalculationSummaryDto(r.Id, r.ReferenceDate, r.Source, r.SourceId, r.Direction, r.Description,
                r.TotalBase, r.TotalTax, r.Open, r.CreatedAt))
            .ToList();
    }

    public Task<TaxCalculationDto> GetAsync(Guid id, CancellationToken ct) => GetAsync(id, reused: false, ct);

    private async Task<TaxCalculationDto> GetAsync(Guid id, bool reused, CancellationToken ct)
    {
        var calc = await _db.TaxCalculations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Cálculo", id);
        var transaction = await _db.TaxTransactions.AsNoTracking().FirstAsync(t => t.Id == calc.TransactionId, ct);
        var profile = await _db.TaxProfileVersions.AsNoTracking()
            .Where(v => v.Id == calc.ProfileVersionId).Select(v => new { v.VersionNumber, v.AccessoryObligations }).FirstAsync(ct);
        var opportunityIds = await _db.TaxOpportunities.AsNoTracking()
            .Where(o => o.SourceCalculationId == id).OrderBy(o => o.CreatedAt).Select(o => o.Id).ToListAsync(ct);

        var canonical = EngineOf(calc).Canonical;
        return new TaxCalculationDto(
            calc.Id, calc.TransactionId, transaction.Source, transaction.SourceId, transaction.Direction, transaction.Description,
            calc.ReferenceDate, calc.ProfileVersionId, profile.VersionNumber, calc.EngineVersion, calc.InputHash, calc.RuleSetHash,
            calc.OutputHash, calc.TotalBase, calc.TotalTax, reused, calc.CreatedAt,
            canonical.Deserialize<TaxEngineInput>(calc.InputJson),
            canonical.Deserialize<TaxEngineOutput>(calc.OutputJson),
            opportunityIds,
            TaxProfileAdvisories.For(profile.AccessoryObligations));
    }

    /// <summary>
    /// Runs the stored input again with exactly the engine version and the rule versions the calculation
    /// used (whatever their status today) and compares the output hash. If that engine version is not
    /// in this build, the answer is "cannot reproduce" — the current engine never stands in. Nothing is written.
    /// </summary>
    public async Task<TaxCalculationVerificationDto> VerifyAsync(Guid id, CancellationToken ct)
    {
        var calc = await _db.TaxCalculations.AsNoTracking().Include(c => c.Executions).FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Cálculo", id);
        var stored = new StoredCalculation(calc.EngineVersion, calc.InputJson, calc.InputHash, calc.OutputJson, calc.OutputHash,
            calc.Executions.Select(e => new StoredRuleRun(e.RuleVersionId, e.RuleKey, e.RuleVersion, e.ContentHash)).ToList());

        var result = await TaxReproduction.VerifyAsync(stored, _engines,
            (engine, ids) => _catalog.LoadByIdsAsync(ids, engine.Canonical, ct));

        _log.LogInformation("Tax calculation {CalculationId} verification with engine {EngineVersion}: available={Available} reproducible={Reproducible}",
            id, calc.EngineVersion, result.EngineAvailable, result.Reproducible);
        return new TaxCalculationVerificationDto(id, result.Reproducible, result.EngineAvailable,
            calc.EngineVersion == result.CurrentEngineVersion, calc.EngineVersion, result.CurrentEngineVersion,
            calc.OutputHash, result.RecomputedOutputHash, result.Differences);
    }
}
