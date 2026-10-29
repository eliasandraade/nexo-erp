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
/// Before anything runs, the supported-operation matrix of V1 (<see cref="TaxCoveragePolicyV1"/>)
/// decides whether the fact pattern is covered: if not, the attempt is refused with its reasons and
/// recorded — it is never calculated as zero. A system calculation states the rules and the data it used.
/// </summary>
public sealed class TaxCalculationService
{
    private const int MaxList = 200;

    private readonly NexoDbContext _db;
    private readonly TaxEngineRegistry _engines;
    private readonly ICurrentTenant _tenant;
    private readonly TaxProfileService _profiles;
    private readonly TaxComplianceService _compliance;
    private readonly TaxCatalogService _catalog;
    private readonly TaxApprovalGate _approvals;
    private readonly TaxOpportunityService _opportunities;
    private readonly SaleTaxFactsReader _sales;
    private readonly TaxAuditTrail _audit;
    private readonly ILogger<TaxCalculationService> _log;

    public TaxCalculationService(
        NexoDbContext db, TaxEngineRegistry engines, ICurrentTenant tenant, TaxProfileService profiles, TaxComplianceService compliance,
        TaxCatalogService catalog, TaxApprovalGate approvals,
        TaxOpportunityService opportunities, SaleTaxFactsReader sales, TaxAuditTrail audit, ILogger<TaxCalculationService> log)
    {
        _db = db;
        _engines = engines;
        _tenant = tenant;
        _profiles = profiles;
        _compliance = compliance;
        _catalog = catalog;
        _approvals = approvals;
        _opportunities = opportunities;
        _sales = sales;
        _audit = audit;
        _log = log;
    }

    public Task<TaxCalculationDto> CalculateAsync(TaxOperationRequest request, CancellationToken ct)
        => RunAsync(new TaxOperationFacts(TaxTransactionSource.Manual, null, request), ct);

    public async Task<TaxCalculationDto> CalculateSaleAsync(Guid saleId, CancellationToken ct)
        => await RunAsync(await _sales.ReadAsync(saleId, ct), ct);

    /// <summary>
    /// Builds the engine input of an operation on its date: the facts, the profile version in force and
    /// the compliance fact of the ancillary-obligation assessment in force (only a verification counts).
    /// </summary>
    public async Task<(TaxEngineInput Input, TaxProfileVersion Profile, TaxComplianceAssessment? Compliance)> BuildInputAsync(
        TaxOperationRequest op, CancellationToken ct)
    {
        var profile = await _profiles.GetInForceAsync(op.OperationDate, ct);
        var compliance = await _compliance.InForceAsync(op.OperationDate, ct);
        return (ToInput(op, profile, compliance?.ForRules), profile, compliance);
    }

    /// <summary>Preview of the supported-operation matrix for an operation, without calculating anything.</summary>
    public async Task<TaxCoverageDecision> CoverageAsync(TaxOperationRequest op, CancellationToken ct)
        => TaxCoveragePolicyV1.Assess(op, (await _profiles.GetInForceAsync(op.OperationDate, ct)).Regime);

    public static TaxEngineInput ToInput(TaxOperationRequest op, TaxProfileVersion profile, bool? complianceForRules) => new(
        op.OperationDate,
        new TaxEngineProfile(profile.Id, profile.Regime, profile.State, profile.CityIbgeCode, complianceForRules),
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
        // Kill switch, approval mode and pilot scope come first: outside them nothing runs (fail closed).
        if (await _approvals.AdmitCalculationAsync(ct) is { } admission) await RefuseAsync(facts, admission, ct);
        var (input, profile, compliance) = await BuildInputAsync(op, ct);
        var coverage = TaxCoveragePolicyV1.Assess(op, profile.Regime);
        if (!coverage.Supported) await RefuseAsync(facts, coverage, ct);
        var engine = _engines.Current;
        var canonical = engine.Canonical;
        var catalogue = await _catalog.LoadForDateAsync(op.OperationDate, canonical, ct);

        var inputJson = canonical.Serialize(input);
        var inputHash = canonical.Hash(inputJson);
        var active = engine.SelectActive(catalogue, op.OperationDate);
        var ruleSetHash = engine.RuleSetHash(active);
        // Every active rule the engine could apply must be approved for the mode (pilot or final): fail closed.
        var governance = await _approvals.ForCalculationAsync(active.Select(r => r.VersionId).ToList(), op.OperationDate, ct);
        if (governance.Enforced && governance.NotApproved.Count > 0)
            await RefuseAsync(facts, TaxApprovalGate.NotApproved(governance), ct, governance.NotApproved);
        // Same facts (all of them, description included) + same rules + same engine = the same calculation.
        var factsHash = canonical.HashOf(op);
        var key = canonical.Hash($"{engine.Version}|{inputHash}|{ruleSetHash}|{facts.Source}|{facts.SourceId}|{factsHash}");

        var existing = await _db.TaxCalculations.AsNoTracking().FirstOrDefaultAsync(c => c.CalculationKey == key, ct);
        if (existing is not null) return await GetAsync(existing.Id, reused: true, ct, coverage);

        var output = engine.Run(input, catalogue);
        var outputJson = canonical.Serialize(output);

        var transaction = TaxTransaction.Create(_tenant.Id, facts.Source, facts.SourceId, op.Direction, op.OperationDate,
            op.Description, op.CounterpartyRegime, op.CounterpartyState, factsHash, _audit.UserId);
        var t = op.Timing!;   // a supported operation always states its timing
        transaction.SetTiming(t.SupplyPattern, t.DocumentDate, t.SupplyDate, t.PaymentOrAdvanceDate, t.CompetencePeriod, t.ExecutionFrom, t.ExecutionTo);
        foreach (var (l, i) in op.Lines.Select((l, i) => (l, i)))
            transaction.AddLine(i + 1, l.ItemKind, l.Description, l.Ncm, l.Quantity, l.GrossAmount, l.UnconditionalDiscount,
                l.ConditionalDiscount, l.Surcharges, l.ExcludedTaxes ?? 0m, l.ExcludedTaxes is not null, l.SpecialTreatment, l.Nbs, l.CClassTrib);

        var calc = TaxCalculation.Create(_tenant.Id, transaction.Id, profile.Id, op.OperationDate, engine.Version,
            inputJson, inputHash, ruleSetHash, key, outputJson, canonical.Hash(outputJson), output.TotalBase, output.TotalTax, _audit.UserId);
        calc.UsedComplianceAssessment(compliance?.Id);
        calc.RanUnderGovernance(governance.Mode, governance.Policy, governance.Enforced, canonical.Serialize(governance));
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
            new { source = facts.Source.ToString(), facts.SourceId, inputHash, ruleSetHash, engine = engine.Version, rules = output.Executions.Count,
                coverage = coverage.PolicyVersion, complianceAssessmentId = compliance?.Id,
                governance = new { mode = governance.Mode.ToString(), governance.Policy, governance.Enforced, governance.Nature,
                    rules = governance.Rules.Select(r => new { r.Key, r.Version, internalApproval = r.InternalApproval.ToString(),
                        professionalReview = r.ProfessionalReview.ToString(), r.PilotCaveats }) } });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The same calculation was stored by a concurrent request: return that one.
            _db.ChangeTracker.Clear();
            var stored = await _db.TaxCalculations.AsNoTracking().FirstAsync(c => c.CalculationKey == key, ct);
            return await GetAsync(stored.Id, reused: true, ct, coverage);
        }

        _log.LogInformation(
            "Tax calculation {CalculationId} from {Source}: {RuleCount} rule runs, {ComponentCount} components, engine {EngineVersion}, input {InputHash}",
            calc.Id, facts.Source, output.Executions.Count, calc.Components.Count, engine.Version, inputHash);

        await _opportunities.DetectAsync(calc.Id, engine, input, catalogue, ct);
        return await GetAsync(calc.Id, reused: false, ct, coverage);
    }

    /// <summary>
    /// Refuses an operation outside the V1 recorte: records the attempt and its reasons in the audit
    /// trail, then answers 422 with each reason. Nothing is calculated — never a zero.
    /// </summary>
    private async Task RefuseAsync(TaxOperationFacts facts, TaxCoverageDecision coverage, CancellationToken ct)
    {
        var op = facts.Operation;
        var reasons = coverage.Findings.Select(f => f.LineIndex is null ? f.Message : $"Linha {f.LineIndex}: {f.Message}").ToList();
        _audit.Record(TaxAuditActions.CalculationRefused, "Calculation", Guid.NewGuid(),
            $"Cálculo recusado ({coverage.Status}): {op.Description ?? facts.Source.ToString()} em {op.OperationDate:dd/MM/yyyy}.",
            new { source = facts.Source.ToString(), facts.SourceId, status = coverage.Status.ToString(), coverage.PolicyVersion, findings = coverage.Findings });
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Tax calculation refused from {Source}: {Status} ({Codes})", facts.Source, coverage.Status,
            string.Join(",", coverage.Findings.Select(f => f.Code)));
        throw new DomainException($"{TaxCoveragePolicyV1.NotSupportedMessage} ({coverage.Status})", "tax_operation_not_supported", reasons);
    }

    /// <summary>
    /// Refuses a calculation the gate does not admit (kill switch, mode, pilot scope, rules not approved for
    /// the mode): records the attempt with its reason, then answers 422. Nothing is calculated.
    /// </summary>
    private async Task RefuseAsync(TaxOperationFacts facts, TaxGateRefusal refusal, CancellationToken ct, IReadOnlyList<string>? details = null)
    {
        var op = facts.Operation;
        _audit.Record(TaxAuditActions.CalculationRefused, "Calculation", Guid.NewGuid(),
            $"Cálculo recusado ({refusal.Code}): {op.Description ?? facts.Source.ToString()} em {op.OperationDate:dd/MM/yyyy}.",
            new { source = facts.Source.ToString(), facts.SourceId, refusal.Code, mode = _approvals.Mode.ToString(), notApproved = details });
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Tax calculation refused from {Source}: {Code}", facts.Source, refusal.Code);
        throw new DomainException(refusal.Message, refusal.Code, details ?? []);
    }

    /// <summary>
    /// Runs the detectors again on a stored calculation (e.g. after a new detector ships). Idempotent.
    /// New proposals are new work: the rules the calculation ran must still back it in the approval mode
    /// today (active, approval neither withdrawn nor expired). Reading and reproducing it never needs this.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> DetectAgainAsync(Guid id, CancellationToken ct)
    {
        if (await _approvals.AdmitCalculationAsync(ct) is { } refusal) await RefuseDetectionAsync(id, refusal, ct);
        var calc = await _db.TaxCalculations.AsNoTracking().Include(c => c.Executions).FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Cálculo", id);
        var executed = calc.Executions.Select(e => e.RuleVersionId).ToList();
        var governance = await _approvals.ForCalculationAsync(executed.Distinct().ToList(), calc.ReferenceDate, ct);
        if (governance.Enforced && governance.NotApproved.Count > 0)
            await RefuseDetectionAsync(id, TaxApprovalGate.NotApproved(governance), ct, governance.NotApproved);
        // Same engine and same rules as the calculation (and the dossier that may follow).
        var engine = EngineOf(calc);
        var input = engine.Canonical.Deserialize<TaxEngineInput>(calc.InputJson);
        var output = engine.Canonical.Deserialize<TaxEngineOutput>(calc.OutputJson);
        var rules = await _catalog.LoadByIdsAsync(executed.Concat(output.NotApplied.Select(n => n.RuleVersionId)).Distinct().ToList(),
            engine.Canonical, ct);
        return await _opportunities.DetectAsync(calc.Id, engine, input, TaxReproduction.RulesAsThen(executed, output, rules), ct);
    }

    /// <summary>Records a refused detection with its reason, then answers 422. No opportunity is created.</summary>
    private async Task RefuseDetectionAsync(Guid calculationId, TaxGateRefusal refusal, CancellationToken ct, IReadOnlyList<string>? details = null)
    {
        _audit.Record(TaxAuditActions.DetectionRefused, "Calculation", calculationId,
            $"Nova detecção de oportunidades recusada ({refusal.Code}).",
            new { refusal.Code, mode = _approvals.Mode.ToString(), notApproved = details });
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Tax detection refused for calculation {CalculationId}: {Code}", calculationId, refusal.Code);
        throw new DomainException(refusal.Message, refusal.Code, details ?? []);
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

    public Task<TaxCalculationDto> GetAsync(Guid id, CancellationToken ct) => GetAsync(id, reused: false, ct, null);

    private async Task<TaxCalculationDto> GetAsync(Guid id, bool reused, CancellationToken ct, TaxCoverageDecision? coverage)
    {
        var calc = await _db.TaxCalculations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Cálculo", id);
        var transaction = await _db.TaxTransactions.AsNoTracking().FirstAsync(t => t.Id == calc.TransactionId, ct);
        var profile = await _db.TaxProfileVersions.AsNoTracking()
            .Where(v => v.Id == calc.ProfileVersionId).Select(v => new { v.VersionNumber, v.Regime }).FirstAsync(ct);
        // The assessment the calculation used — never a later one (its input and output are frozen with it).
        var compliance = await _compliance.GetAsync(calc.ComplianceAssessmentId, ct);
        var current = await _compliance.InForceAsync(calc.ReferenceDate, ct);
        // A stored calculation was supported when it ran; the matrix is re-derived from its stored facts for display.
        coverage ??= TaxCoveragePolicyV1.Assess(await StoredOperationAsync(transaction, ct), profile.Regime);
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
            TaxComplianceAdvisories.For(compliance),
            coverage,
            compliance is null ? null : TaxComplianceService.Map(compliance),
            current is null || current.Id == compliance?.Id ? null : TaxComplianceService.Map(current),
            calc.GovernanceJson is null ? null : canonical.Deserialize<TaxGovernanceDto>(calc.GovernanceJson));
    }

    private async Task<TaxOperationRequest> StoredOperationAsync(TaxTransaction t, CancellationToken ct)
    {
        var lines = await _db.TaxTransactionLines.AsNoTracking().Where(l => l.TransactionId == t.Id).OrderBy(l => l.LineIndex).ToListAsync(ct);
        return new TaxOperationRequest(t.OperationDate, t.Direction, t.CounterpartyRegime, t.CounterpartyState, t.Description,
            lines.Select(l => new TaxLineRequest(l.ItemKind, l.Description, l.Ncm, l.Quantity, l.GrossAmount, l.UnconditionalDiscount,
                l.ConditionalDiscount, l.Surcharges, l.ExcludedTaxesInformed ? l.ExcludedTaxes : null, l.SpecialTreatment, l.Nbs, l.CClassTrib)).ToList(),
            new TaxOperationTiming(t.SupplyPattern, t.DocumentDate, t.SupplyDate, t.PaymentOrAdvanceDate, t.CompetencePeriod, t.ExecutionFrom, t.ExecutionTo));
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
