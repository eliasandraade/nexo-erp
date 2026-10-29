using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Application.Modules.TaxIntelligence.Engine;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.Persistence;
using Npgsql;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>
/// Opportunities: detection after a calculation, then Identified → InReview → Approved/Rejected, with
/// the guardrails able to block at any point before approval. Approval writes the decision dossier in
/// the same transaction. Nothing here changes a sale, a price, a ledger or a document — an approved
/// opportunity is a recorded scenario decision, and carrying it out is up to the business. Approval
/// follows the <see cref="TaxIntelligencePolicyOptions"/>: rules not approved for the mode block it when
/// so configured — in the controlled pilot the internal pilot approval suffices, in the definitive mode
/// only a professional one — and the maker-checker requirement depends on risk and materiality.
/// After the operation, an execution verification records whether it really happened as approved.
/// Hashes and versions show the integrity of the records — not the material truth of the facts.
/// </summary>
/// <summary>Filters of the opportunity list.</summary>
public sealed record TaxOpportunityFilter(
    TaxOpportunityStatus? Status = null, TaxRiskLevel? Risk = null, IReadOnlyList<TaxOpportunityType>? Types = null, bool? Open = null,
    bool? AssignedToMe = null, DateOnly? From = null, DateOnly? To = null, string? Search = null, int Skip = 0, int Take = 50);

public sealed class TaxOpportunityService
{
    private const int MaxList = 200;
    private const string NotFoundName = "Oportunidade";
    private const string AuditEntity = "Opportunity";

    private readonly TaxContext _ctx;
    private readonly NexoDbContext _db;
    private readonly TaxEngineRegistry _engines;
    private readonly TaxCatalogService _catalog;
    private readonly TaxComplianceService _compliance;
    private readonly TaxApprovalGate _approvals;
    private readonly TaxMaterialityService _materiality;
    private readonly ICurrentTenant _tenant;
    private readonly TaxAuditTrail _audit;
    private readonly ILogger<TaxOpportunityService> _log;

    public TaxOpportunityService(
        TaxContext ctx, TaxEngineRegistry engines, TaxCatalogService catalog, TaxComplianceService compliance, TaxApprovalGate approvals,
        TaxMaterialityService materiality, ILogger<TaxOpportunityService> log)
    {
        _ctx = ctx;
        _db = ctx.Db;
        _engines = engines;
        _catalog = catalog;
        _compliance = compliance;
        _approvals = approvals;
        _materiality = materiality;
        _tenant = ctx.Tenant;
        _audit = ctx.Audit;
        _log = log;
    }

    private TaxIntelligencePolicyOptions Policy => _approvals.Policy;
    private static DateOnly Today => DateOnly.FromDateTime(BusinessClock.ToBrazil(DateTime.UtcNow));

    /// <summary>
    /// Runs every detector on a calculation's input with the engine that made the calculation.
    /// Idempotent: one opportunity per detector and calculation.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> DetectAsync(
        Guid calculationId, ITaxEngine engine, TaxEngineInput input, IReadOnlyCollection<TaxEngineRule> catalogue, CancellationToken ct)
    {
        var created = new List<Guid>();
        foreach (var detector in TaxOpportunityDetectors.All)
        {
            if (await _db.TaxOpportunities.AnyAsync(o => o.SourceCalculationId == calculationId && o.DetectorKey == detector.Key, ct))
                continue;
            foreach (var c in detector.Detect(engine, input, catalogue))
            {
                var o = TaxOpportunity.Create(_tenant.Id, c.Type, c.Title, c.Summary, c.DetectorKey, engine.Version, calculationId,
                    engine.Canonical.Serialize(c.Current), engine.Canonical.Serialize(c.Proposed), c.EstimatedImpact, c.Risk, c.Confidence,
                    string.Join(",", c.LegalSourceCodes), c.Caveats);
                foreach (var r in c.Requirements) o.AddRequirement(r.Code, r.Text, r.LegalSourceCode, r.Phase, r.RequiredEvidence);
                _db.TaxOpportunities.Add(o);
                _audit.Record(TaxAuditActions.OpportunityIdentified, AuditEntity, o.Id,
                    $"Oportunidade identificada: {c.Title} ({c.Type}, risco {c.Risk}).",
                    new { calculationId, detector = c.DetectorKey, impact = c.EstimatedImpact, nature = TaxOpportunity.NatureOf(c.Type).ToString() });
                created.Add(o.Id);
                _ctx.Metrics.OpportunityIdentified(c.DetectorKey);
            }
        }
        if (created.Count == 0) return created;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _db.ChangeTracker.Clear();   // a concurrent run stored them first
            return [];
        }
        _log.LogInformation("Tax opportunities identified for calculation {CalculationId}: {Count}", calculationId, created.Count);
        return created;
    }

    /// <summary>Opportunities with filters and paging. Blocked ones never show an amount.</summary>
    public async Task<TaxPage<TaxOpportunitySummaryDto>> ListAsync(TaxOpportunityFilter f, CancellationToken ct)
    {
        var take = Math.Clamp(f.Take, 1, MaxList);
        var skip = Math.Max(0, f.Skip);
        var q = _db.TaxOpportunities.AsNoTracking();
        if (f.Status is not null) q = q.Where(o => o.Status == f.Status);
        if (f.Risk is not null) q = q.Where(o => o.Risk == f.Risk);
        if (f.Types is { Count: > 0 }) q = q.Where(o => f.Types.Contains(o.Type));
        if (f.Open == true) q = q.Where(o => o.Status == TaxOpportunityStatus.Identified || o.Status == TaxOpportunityStatus.InReview);
        if (f.AssignedToMe == true) q = q.Where(o => o.AssignedToId == _audit.UserId);
        if (f.From is not null) q = q.Where(o => o.CreatedAt >= f.From.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        if (f.To is not null) q = q.Where(o => o.CreatedAt < f.To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        if (!string.IsNullOrWhiteSpace(f.Search)) q = q.Where(o => EF.Functions.ILike(o.Title, $"%{f.Search.Trim()}%"));
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(o => o.CreatedAt).Skip(skip).Take(take)
            .Select(o => new
            {
                o, Total = o.Requirements.Count, Met = o.Requirements.Count(r => r.Met == true),
            }).ToListAsync(ct);
        return new TaxPage<TaxOpportunitySummaryDto>(rows.Select(x => new TaxOpportunitySummaryDto(x.o.Id, x.o.Type, x.o.ImpactNature, x.o.Status,
            x.o.Title, x.o.Status == TaxOpportunityStatus.Blocked ? null : x.o.EstimatedImpact, x.o.Risk, x.o.Confidence, x.o.CreatedAt,
            x.o.AssignedToName, x.o.DetectorKey, x.Total, x.Met)).ToList(), total, skip, take);
    }

    public async Task<TaxOpportunityDto> GetAsync(Guid id, CancellationToken ct)
    {
        var o = await _db.TaxOpportunities.AsNoTracking().Include(x => x.Requirements).Include(x => x.Evidence)
            .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(NotFoundName, id);
        var decision = await _db.TaxDecisions.AsNoTracking().FirstOrDefaultAsync(d => d.OpportunityId == id, ct);
        var execution = await _db.TaxExecutionVerifications.AsNoTracking().FirstOrDefaultAsync(e => e.OpportunityId == id, ct);
        var (date, regime) = await ReferenceAsync(o, ct);
        var governance = await _approvals.ForDecisionAsync(await RuleVersionsBehindAsync(o, ct), date, ct, regime);
        var files = await FilesOfAsync(o, ct);
        var materiality = await _materiality.InForceAsync(Today, ct);
        return Map(o, decision, execution, governance, files, materiality);
    }

    private async Task<Dictionary<Guid, TaxEvidenceFile>> FilesOfAsync(TaxOpportunity o, CancellationToken ct)
    {
        var ids = o.Evidence.Where(e => e.FileId is not null).Select(e => e.FileId!.Value).ToList();
        return ids.Count == 0 ? [] : await _db.TaxEvidenceFiles.AsNoTracking().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
    }

    /// <summary>The date and establishment regime the opportunity's calculation ran on (the regime decides which professional reviews cover it).</summary>
    private async Task<(DateOnly Date, string? Regime)> ReferenceAsync(TaxOpportunity o, CancellationToken ct)
    {
        if (o.SourceCalculationId is null) return (DateOnly.FromDateTime(o.CreatedAt), null);
        var x = await _db.TaxCalculations.AsNoTracking().Where(c => c.Id == o.SourceCalculationId)
            .Join(_db.TaxProfileVersions.AsNoTracking(), c => c.ProfileVersionId, v => v.Id, (c, v) => new { c.ReferenceDate, v.Regime })
            .FirstAsync(ct);
        return (x.ReferenceDate, x.Regime.ToString());
    }

    /// <summary>The catalogue rule versions an opportunity rests on: those that matched in its calculation.</summary>
    private async Task<IReadOnlyList<Guid>> RuleVersionsBehindAsync(TaxOpportunity o, CancellationToken ct)
        => o.SourceCalculationId is null
            ? []
            : await _db.TaxRuleExecutions.AsNoTracking()
                .Where(e => e.CalculationId == o.SourceCalculationId && e.Matched)
                .Select(e => e.RuleVersionId).Distinct().ToListAsync(ct);

    public Task<TaxOpportunityDto> StartReviewAsync(Guid id, CancellationToken ct) => MutateAsync(id, o =>
    {
        o.StartReview(_audit.UserId, _audit.UserName, DateTime.UtcNow);
        _audit.Record(TaxAuditActions.OpportunityReviewStarted, AuditEntity, o.Id, $"Análise iniciada: {o.Title}.");
    }, ct);

    public Task<TaxOpportunityDto> AnswerRequirementAsync(Guid id, Guid requirementId, AnswerRequirementRequest r, CancellationToken ct) => MutateAsync(id, o =>
    {
        var requirement = o.Requirements.FirstOrDefault(x => x.Id == requirementId) ?? throw new NotFoundException("Requisito", requirementId);
        var before = requirement.Met;
        var met = r.Met ?? throw new DomainException("Diga se o requisito foi atendido.");
        o.AnswerRequirement(requirementId, met, r.Note, _audit.UserId, _audit.UserName, DateTime.UtcNow);
        _audit.Record(TaxAuditActions.OpportunityRequirementAnswered, AuditEntity, o.Id,
            $"Requisito \"{requirement.Text}\": {(met ? "atendido" : "não atendido")}.",
            new { requirementId, requirement.Code, before, after = met, r.Note });

        var violation = TaxGuardrails.OnRequirementAnswered(o.DetectorKey, requirement.Code, met);
        if (violation is not null)
        {
            o.Block(violation.Structure, violation.Reason, _audit.UserId, _audit.UserName, DateTime.UtcNow);
            _audit.Record(TaxAuditActions.OpportunityBlocked, AuditEntity, o.Id,
                $"Bloqueada como estrutura proibida ({violation.Structure}): {violation.Reason}",
                new { structure = violation.Structure.ToString(), automatic = true }, AuditSeverity.Warning);
            _log.LogWarning("Tax opportunity {OpportunityId} blocked by guardrail as {Structure}", o.Id, violation.Structure);
        }
    }, ct);

    public Task<TaxOpportunityDto> AddEvidenceAsync(Guid id, AddEvidenceRequest r, CancellationToken ct) => MutateAsync(id, o =>
    {
        var hash = ReferenceHash(o, r.Type, r.RequirementId, r.Description, r.DocumentReference, r.Notes);
        var e = o.AddEvidence(r.Type, r.RequirementId, r.Description, r.DocumentReference, hash, r.Notes, _audit.UserId, _audit.UserName);
        _db.TaxOpportunityEvidence.Add(e);
        _audit.Record(TaxAuditActions.OpportunityEvidenceAdded, AuditEntity, o.Id, "Evidência anexada (referência, sem arquivo).",
            new { evidenceId = e.Id, type = r.Type.ToString(), r.RequirementId, hash, hashKind = e.HashKind });
    }, ct);

    /// <summary>
    /// Fingerprint of evidence given only by reference: the same versioned hashing contract as the dossier (the canonical form of the
    /// opportunity's engine). It shows the record was not altered — it says nothing about a document's bytes.
    /// </summary>
    internal string ReferenceHash(TaxOpportunity o, TaxEvidenceType type, Guid? requirementId, string description, string? reference, string? notes)
        => EngineOf(o).Canonical.Hash($"{type}\n{requirementId}\n{description.Trim()}\n{reference?.Trim()}\n{notes?.Trim()}");

    /// <summary>Assigns the person responsible for moving the analysis forward — a work assignment, never an approval power.</summary>
    public async Task<TaxOpportunityDto> AssignAsync(Guid id, AssignOpportunityRequest r, CancellationToken ct)
    {
        // The name is the user's own record in this company — a client cannot assign a made-up person —
        // and only someone who can analyse (management) can be made responsible for an analysis.
        string? name = null;
        if (r.UserId is { } userId)
            name = await _db.Users.AsNoTracking()
                       .Where(u => u.Id == userId && u.TenantId == _tenant.Id && u.Status == UserStatus.Active
                                   && (u.Role == UserRole.Gerente || u.Role == UserRole.Diretoria))
                       .Select(u => u.FullName).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException("Usuário", userId);
        return await MutateAsync(id, o => Assign(o, r.UserId, name), ct);
    }

    private void Assign(TaxOpportunity o, Guid? userId, string? name)
    {
        var before = o.AssignedToName;
        o.Assign(userId, name, DateTime.UtcNow);
        _audit.Record(TaxAuditActions.OpportunityAssigned, AuditEntity, o.Id,
            userId is null ? "Responsável retirado." : $"Responsável: {o.AssignedToName}.", new { before, after = o.AssignedToName, userId });
    }

    /// <summary>What happened to an opportunity, from the audit trail of this company (newest last).</summary>
    public async Task<IReadOnlyList<TaxTimelineEventDto>> TimelineAsync(Guid id, CancellationToken ct)
    {
        if (!await _db.TaxOpportunities.AnyAsync(o => o.Id == id, ct)) throw new NotFoundException(NotFoundName, id);
        var key = id.ToString();
        var tenant = _tenant.Id;
        return (await _db.AuditRecords.AsNoTracking()
                .Where(a => a.TenantId == tenant && a.EntityType == TaxAuditTrail.EntityPrefix + AuditEntity && a.EntityId == key)
                .OrderBy(a => a.CreatedAt).Take(500).ToListAsync(ct))
            .Select(a => new TaxTimelineEventDto(a.ActionType, a.Description, a.ActorName ?? "?", a.CreatedAt, a.Severity)).ToList();
    }

    public Task<TaxOpportunityDto> ReviewEvidenceAsync(Guid id, Guid evidenceId, ReviewEvidenceRequest r, CancellationToken ct) => MutateAsync(id, o =>
    {
        o.ReviewEvidence(evidenceId, r.Accepted, r.Notes, _audit.UserId, _audit.UserName, DateTime.UtcNow);
        _audit.Record(TaxAuditActions.OpportunityEvidenceReviewed, AuditEntity, o.Id,
            r.Accepted ? "Evidência aceita." : $"Evidência recusada: {r.Notes}", new { evidenceId, r.Accepted, r.Notes });
    }, ct);

    public Task<TaxOpportunityDto> BlockAsync(Guid id, BlockOpportunityRequest r, CancellationToken ct) => MutateAsync(id, o =>
    {
        var before = o.Status;
        o.Block(r.Structure, r.Reason, _audit.UserId, _audit.UserName, DateTime.UtcNow);
        _audit.Record(TaxAuditActions.OpportunityBlocked, AuditEntity, o.Id,
            $"Bloqueada como estrutura proibida ({r.Structure}): {r.Reason}",
            new { before = before.ToString(), structure = r.Structure.ToString(), automatic = false }, AuditSeverity.Warning);
    }, ct);

    public Task<TaxOpportunityDto> RejectAsync(Guid id, RejectOpportunityRequest r, CancellationToken ct) => MutateAsync(id, o =>
    {
        var before = o.Status;
        o.Reject(r.Reason, _audit.UserId, _audit.UserName, DateTime.UtcNow);
        _audit.Record(TaxAuditActions.OpportunityRejected, AuditEntity, o.Id, $"Rejeitada: {r.Reason}", new { before = before.ToString() });
    }, ct);

    /// <summary>
    /// Approves and freezes the dossier: facts, both scenarios, rule versions and their hashes, legal
    /// excerpts (with their text verification), the professional review state of the rules, requirements
    /// as answered, evidence, who analysed and who approved (with the self-approval justification, if
    /// any), risk and engine version.
    /// </summary>
    public async Task<TaxOpportunityDto> ApproveAsync(Guid id, ApproveOpportunityRequest r, CancellationToken ct)
    {
        var (governance, referenceDate, ruleIds) = await AdmitDecisionAsync(id, "aprovação do cenário", ct);
        var o = await LoadForUpdateAsync(id, ct);
        var now = DateTime.UtcNow;
        // The materiality in force on the decision date — recorded in the dossier; a later change never rewrites it.
        var materiality = await _materiality.InForceAsync(Today, ct);
        var approval = Policy.RequiredFor(o.Risk, o.EstimatedImpact, materiality.Threshold);
        var selfApproval = o.IsSoleAnalyst(_audit.UserId);
        o.Approve(r.AcknowledgeHighRisk, _audit.UserId, _audit.UserName, now, approval, r.SelfApprovalJustification);
        var justification = selfApproval ? r.SelfApprovalJustification!.Trim() : null;

        var pendingReview = governance.ProfessionalReviewPending;

        var engine = EngineOf(o);
        var calc = o.SourceCalculationId is null ? null
            : await _db.TaxCalculations.AsNoTracking().Include(c => c.Executions).FirstOrDefaultAsync(c => c.Id == o.SourceCalculationId, ct);
        var codes = o.LegalSourceCodes.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Concat(calc?.Executions.Where(e => e.Matched).SelectMany(e => e.LegalSourceCodes.Split(',', StringSplitOptions.RemoveEmptyEntries)) ?? [])
            .Distinct().ToList();
        var sources = await _db.TaxLegalSources.AsNoTracking().Where(s => codes.Contains(s.Code))
            .OrderBy(s => s.Code).Select(s => new { s.Code, s.Title, s.Reference, s.Url, s.Excerpt, textVerification = s.TextVerification.ToString() })
            .ToListAsync(ct);
        var reviews = await _db.TaxRuleVersions.AsNoTracking().Where(v => ruleIds.Contains(v.Id))
            .Join(_db.TaxRules.AsNoTracking(), v => v.RuleId, x => x.Id, (v, x) => new { x.Key, v })
            .ToListAsync(ct);
        var transaction = calc is null ? null
            : await _db.TaxTransactions.AsNoTracking().Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == calc.TransactionId, ct);
        var profile = calc is null ? null : await _db.TaxProfileVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == calc.ProfileVersionId, ct);
        var fiscal = await _db.FiscalProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        // The assessment the calculation used (its result is frozen with it), not whatever is in force today.
        var compliance = calc is null ? await _compliance.InForceAsync(referenceDate, ct) : await _compliance.GetAsync(calc.ComplianceAssessmentId, ct);

        var files = await FilesOfAsync(o, ct);
        var commercial = o.Evidence.Where(x => x.Type == TaxEvidenceType.CommercialInstrument && x.InForce).ToList();
        var original = engine.Canonical.Deserialize<ScenarioResult>(o.CurrentScenarioJson);
        var proposed = engine.Canonical.Deserialize<ScenarioResult>(o.ProposedScenarioJson);
        var pendingIssues = new List<string> { "Verificação da execução pendente: o documento fiscal da operação ainda não foi conferido." };
        if (pendingReview.Count > 0) pendingIssues.Add($"Revisão tributária pendente: {string.Join(", ", pendingReview)}.");
        if (compliance?.Status != ComplianceAssessmentStatus.Verified)
            pendingIssues.Add("Obrigações acessórias sem verificação no período e no escopo da operação.");

        var dossier = new
        {
            kind = "scenario-approval",
            nature = TaxGovernance.DossierNature,
            integrityNotice = IntegrityNotice,
            strategy = o.Title,
            opportunity = new { o.Id, type = o.Type.ToString(), nature = o.ImpactNature.ToString(), o.Summary, o.DetectorKey, o.EstimatedImpact, o.Confidence, o.Caveats },
            taxpayer = fiscal is null ? null : new { fiscal.LegalName, fiscal.Cnpj, fiscal.State, fiscal.CityIbgeCode },
            establishment = profile is null ? null : new
            {
                storeId = profile.StoreId, profileVersionId = profile.Id, profile.VersionNumber, regime = profile.Regime.ToString(), profile.State,
                profile.CityIbgeCode, profile.PrimaryCnae,
            },
            counterparty = transaction is null ? null : new { regime = transaction.CounterpartyRegime?.ToString(), state = transaction.CounterpartyState },
            relevantDates = transaction is null ? null : new
            {
                transaction.OperationDate, supplyPattern = transaction.SupplyPattern.ToString(), transaction.DocumentDate, transaction.SupplyDate,
                transaction.PaymentOrAdvanceDate, transaction.CompetencePeriod, transaction.ExecutionFrom, transaction.ExecutionTo,
            },
            classification = transaction?.Lines.OrderBy(l => l.LineIndex)
                .Select(l => new { l.LineIndex, kind = l.ItemKind.ToString(), l.Ncm, l.Nbs, cClassTrib = l.CClassTrib, l.SpecialTreatment }).ToList(),
            facts = calc is null ? null : new { calc.Id, calc.ReferenceDate, calc.InputHash, calc.RuleSetHash, calc.OutputHash, calc.EngineVersion, input = TaxJson.Element(calc.InputJson) },
            originalScenario = TaxJson.Element(o.CurrentScenarioJson),
            proposedScenario = TaxJson.Element(o.ProposedScenarioJson),
            // Arithmetic between the two stored scenarios — not a statement about money saved.
            impactAnalysis = new
            {
                nature = o.ImpactNature.ToString(),
                baseDifference = original.Output.TotalBase - proposed.Output.TotalBase,
                calculatedTaxDifference = original.TaxTotal - proposed.TaxTotal,
                noCashEffectInPeriod = original.NoCashEffectInPeriod,
                financialEffect = FinancialEffectNotDetermined,
            },
            rules = calc?.Executions.Where(e => e.Matched).DistinctBy(e => e.RuleVersionId)
                .Select(e => new { e.RuleKey, e.RuleVersion, e.RuleVersionId, e.ContentHash, e.LegalSourceCodes }).ToList(),
            legalSources = sources,
            // Text verification of a source says only that the excerpt matches the official text.
            professionalReview = new
            {
                pendingRules = pendingReview,
                requiredByPolicy = governance.Enforced && governance.Mode == TaxApprovalMode.ProfessionalFinal,
                reviews = reviews.OrderBy(x => x.Key).Select(x => new
                {
                    x.Key, x.v.Version, status = x.v.ProfessionalReview.ToString(), reviewer = x.v.ProfessionalReviewedBy, x.v.ProfessionalReference,
                    scope = x.v.ProfessionalReviewScope, validFrom = x.v.ProfessionalReviewValidFrom, validTo = x.v.ProfessionalReviewValidTo,
                    assumptions = x.v.ProfessionalReviewAssumptions, caveats = x.v.ProfessionalReviewCaveats,
                }).ToList(),
            },
            // The internal pilot approval and the professional review, side by side — one never stands in for the other.
            governance = GovernanceView(governance),
            complianceAssessment = compliance is null ? null : new
            {
                status = compliance.Status.ToString(), compliance.PeriodFrom, compliance.PeriodTo, compliance.Scope, compliance.ObligationsChecked,
                compliance.AssessedBy, compliance.AssessedAt, compliance.Limitations, compliance.PnctParticipation,
            },
            requirements = o.Requirements.OrderBy(x => x.Phase).ThenBy(x => x.Code).Select(x => new
            {
                x.Code, phase = x.Phase.ToString(), x.Text, x.LegalSourceCode, x.Met, x.Note, x.AnsweredByName, x.AnsweredAt,
                requiredEvidence = x.RequiredEvidence?.ToString(),
            }),
            commercialEvidence = commercial.OrderBy(x => x.CreatedAt).Select(x => EvidenceView(x, files)),
            // Every version, in force or not, exactly as it stood when the decision was taken (file id, SHA-256 of its bytes).
            evidence = o.Evidence.OrderBy(x => x.CreatedAt).Select(x => EvidenceView(x, files)),
            // A scenario approval never states that a future fiscal document exists.
            fiscalDocumentEvidence = new { status = "pending-execution-verification" },
            executionVerification = new { status = "pending" },
            pendingIssues,
            risk = o.Risk.ToString(),
            highRiskAcknowledged = r.AcknowledgeHighRisk,
            reviewStartedBy = o.ReviewStartedByName,
            approvedBy = new { id = _audit.UserId, name = _audit.UserName },
            approvedAt = now,
            makerChecker = new
            {
                required = approval.ToString(), materialityThreshold = materiality.Threshold, selfApproval, justification,
                materiality = new
                {
                    source = materiality.Source, policyId = materiality.Policy?.Id, version = materiality.Policy?.Version,
                    effectiveFrom = materiality.Policy?.EffectiveFrom, decisionReference = materiality.Policy?.DecisionReference,
                },
            },
            engineVersion = o.EngineVersion,
            canonicalVersion = engine.Canonical.Id,
        };
        var dossierJson = System.Text.Json.JsonSerializer.Serialize(dossier, engine.Canonical.Options);
        var decision = TaxDecision.Create(_tenant.Id, o.Id, o.Title, dossierJson, engine.Canonical.Hash(dossierJson), o.Risk,
            r.AcknowledgeHighRisk, o.EngineVersion, _audit.UserId, _audit.UserName, now, justification);
        _db.TaxDecisions.Add(decision);
        _audit.Record(TaxAuditActions.OpportunityApproved, AuditEntity, o.Id,
            $"Cenário aprovado por {_audit.UserName}: {o.Title}. Dossiê {decision.DossierHash[..12]}.",
            new { decisionId = decision.Id, decision.DossierHash, risk = o.Risk.ToString(), r.AcknowledgeHighRisk, approval = approval.ToString(),
                selfApproval, justification, pendingProfessionalReview = pendingReview,
                approvalMode = governance.Mode.ToString(), approvalPolicy = governance.Policy, approvalEnforced = governance.Enforced }, AuditSeverity.Warning);
        await SaveAsync(ct);
        _ctx.Metrics.Approved("scenario");
        _log.LogInformation("Tax opportunity {OpportunityId} approved; decision {DecisionId}", o.Id, decision.Id);
        return await GetAsync(id, ct);
    }

    /// <summary>What the dossier says about money: the calculated-tax difference is not a demonstrated financial effect.</summary>
    public const string FinancialEffectNotDetermined =
        "Não determinado: a diferença é do IBS/CBS calculado entre os cenários; o efeito financeiro depende do período (compensação ou dispensa em 2026), dos preços praticados e de revisão profissional.";

    /// <summary>Integrity, not truth: what the hashes in a dossier do and do not show.</summary>
    public const string IntegrityNotice =
        "O hash e a versão demonstram a integridade deste registro — que ele não foi alterado — e não a veracidade material dos fatos declarados.";

    /// <summary>
    /// The governance frozen into a dossier: mode, policy, pilot scope and, per rule, its own internal approval
    /// (policy, actor, scope, validity, caveats and the exact decision reference) beside its professional review.
    /// </summary>
    private static object GovernanceView(TaxGovernanceDto g) => new
    {
        mode = g.Mode.ToString(), g.Policy, g.Enforced, g.Nature, g.Notice, effects = g.EffectsNotice, pilotScope = g.PilotScope,
        decisionReferences = g.DecisionReferences,
        rules = g.Rules.Select(x => new
        {
            x.Key, x.Version, x.RuleVersionId, technicalStatus = x.TechnicalStatus.ToString(),
            internalApproval = new
            {
                status = x.InternalApproval.ToString(), x.ApprovedForPilot, policy = x.InternalApprovalPolicy,
                approvedById = x.InternalApprovedById, approvedBy = x.InternalApprovedBy, approvedByRole = x.InternalApprovedByRole,
                approvedAt = x.InternalApprovedAt, scope = x.InternalApprovalScope, validFrom = x.InternalApprovalValidFrom,
                validTo = x.InternalApprovalValidTo, caveats = x.PilotCaveats, decisionReference = x.InternalApprovalDecisionRef,
            },
            professionalReview = new
            {
                status = x.ProfessionalReview.ToString(), approved = x.ProfessionallyApproved, scope = x.ProfessionalReviewScope,
                caveats = x.ProfessionalCaveats, recordId = x.ProfessionalReviewRecordId, reviewer = x.ProfessionalReviewer,
                reference = x.ProfessionalReference, validFrom = x.ProfessionalReviewValidFrom, validTo = x.ProfessionalReviewValidTo,
                coveredRegimes = x.ProfessionalReviewRegimes,
            },
        }),
        professionalReviewPending = g.ProfessionalReviewPending,
    };

    /// <summary>
    /// Admits new decision work (scenario approval, execution verification) before anything is locked:
    /// kill switch, approval mode, pilot scope, every rule behind it active and approved for the mode, and the
    /// calculation behind it still reproducible. A refusal is audited and nothing else is written.
    /// </summary>
    private async Task<(TaxGovernanceDto Governance, DateOnly ReferenceDate, IReadOnlyList<Guid> RuleIds)> AdmitDecisionAsync(
        Guid id, string what, CancellationToken ct)
    {
        var o = await _db.TaxOpportunities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(NotFoundName, id);
        if (await _approvals.AdmitDecisionAsync(ct) is { } admission) await RefuseDecisionAsync(o, what, admission, null, ct);

        var (referenceDate, regime) = await ReferenceAsync(o, ct);
        var ruleIds = await RuleVersionsBehindAsync(o, ct);
        // Pilot: the internal pilot approval suffices. Definitive mode: only a professional approval covering the regime. Either way the rule must still be active.
        var governance = await _approvals.ForDecisionAsync(ruleIds, referenceDate, ct, regime);
        if (governance.Enforced && governance.NotApproved.Count > 0)
            await RefuseDecisionAsync(o, what, TaxApprovalGate.NotApproved(governance), governance.NotApproved, ct);
        if (governance.Enforced && o.SourceCalculationId is { } calcId && !await ReproducesAsync(calcId, ct))
            await RefuseDecisionAsync(o, what, new TaxGateRefusal("tax_calculation_not_reproducible",
                "O cálculo por trás desta oportunidade não é mais reproduzível com a engine e as regras que usou: nenhuma decisão nova é registrada."), null, ct);
        return (governance, referenceDate, ruleIds);
    }

    private async Task RefuseDecisionAsync(TaxOpportunity o, string what, TaxGateRefusal refusal, IReadOnlyList<string>? details, CancellationToken ct)
    {
        _audit.Record(TaxAuditActions.DecisionRefused, AuditEntity, o.Id, $"Recusada a {what} ({refusal.Code}): {o.Title}.",
            new { refusal.Code, mode = _approvals.Mode.ToString(), notApproved = details });
        await _db.SaveChangesAsync(ct);
        _ctx.Metrics.Refused("decision", refusal.Code);
        _log.LogInformation("Tax decision refused for opportunity {OpportunityId}: {Code}", o.Id, refusal.Code);
        throw new DomainException(refusal.Message, refusal.Code, details ?? []);
    }

    /// <summary>Re-runs the calculation with the engine and rule versions it recorded (nothing is written).</summary>
    private async Task<bool> ReproducesAsync(Guid calculationId, CancellationToken ct)
    {
        var calc = await _db.TaxCalculations.AsNoTracking().Include(c => c.Executions).FirstAsync(c => c.Id == calculationId, ct);
        var stored = new StoredCalculation(calc.EngineVersion, calc.InputJson, calc.InputHash, calc.OutputJson, calc.OutputHash,
            calc.Executions.Select(e => new StoredRuleRun(e.RuleVersionId, e.RuleKey, e.RuleVersion, e.ContentHash)).ToList());
        var result = await TaxReproduction.VerifyAsync(stored, _engines, (engine, ids) => _catalog.LoadByIdsAsync(ids, engine.Canonical, ct));
        return result.Reproducible;
    }

    private static object EvidenceView(TaxOpportunityEvidence x, Dictionary<Guid, TaxEvidenceFile> files) => new
    {
        x.Id, type = x.Type.ToString(), x.RequirementId, x.Description, x.DocumentReference, x.ContentHash, x.HashKind, x.Version,
        x.SupersedesEvidenceId, status = x.Status.ToString(), x.Notes, x.AddedByName, x.CreatedAt, x.ReviewedByName, x.ReviewedAt, x.ReviewNotes,
        file = x.FileId is { } id && files.TryGetValue(id, out var f)
            ? new { f.Id, f.OriginalFileName, f.ContentType, f.SizeBytes, f.Sha256, f.Version, status = f.Status.ToString() }
            : null,
    };

    /// <summary>
    /// Records whether the operation really happened as the scenario was approved, and seals it: the
    /// dossier carries the decision's hash, the execution requirements as answered and the final
    /// evidence (authorised fiscal document…). The decision itself is never changed.
    /// </summary>
    public async Task<TaxOpportunityDto> VerifyExecutionAsync(Guid id, VerifyExecutionRequest r, CancellationToken ct)
    {
        var (governance, _, _) = await AdmitDecisionAsync(id, "verificação da execução", ct);
        var o = await LoadForUpdateAsync(id, ct);
        var now = DateTime.UtcNow;
        var decision = await _db.TaxDecisions.AsNoTracking().FirstOrDefaultAsync(d => d.OpportunityId == id, ct)
            ?? throw new DomainException("Sem cenário aprovado não há execução a verificar.", "tax_opportunity_execution_state");
        o.VerifyExecution(r.Outcome, r.Notes, _audit.UserId, _audit.UserName, now);

        var engine = EngineOf(o);
        var files = await FilesOfAsync(o, ct);
        var executionRequirements = o.Requirements.Where(x => x.Phase == RequirementPhase.ExecutionVerification).ToList();
        var ids = executionRequirements.Select(x => (Guid?)x.Id).ToHashSet();
        var dossier = new
        {
            kind = "execution-verification",
            nature = TaxGovernance.DossierNature,
            integrityNotice = IntegrityNotice,
            decision = new { decision.Id, decision.DossierHash, decision.ApprovedAt, decision.ApprovedByName },
            outcome = r.Outcome.ToString(),
            fiscalDocument = new { reference = r.FiscalDocumentReference?.Trim(), protocol = r.AuthorizationProtocol?.Trim() },
            requirements = executionRequirements.OrderBy(x => x.Code).Select(x => new
            {
                x.Code, x.Text, x.Met, x.Note, x.AnsweredByName, x.AnsweredAt, requiredEvidence = x.RequiredEvidence?.ToString(),
            }),
            fiscalDocumentEvidence = o.Evidence.Where(x => ids.Contains(x.RequirementId)).OrderBy(x => x.CreatedAt).Select(x => EvidenceView(x, files)),
            notes = r.Notes?.Trim(),
            verifiedBy = new { id = _audit.UserId, name = _audit.UserName },
            verifiedAt = now,
            governance = GovernanceView(governance),
            engineVersion = o.EngineVersion,
            canonicalVersion = engine.Canonical.Id,
        };
        var json = System.Text.Json.JsonSerializer.Serialize(dossier, engine.Canonical.Options);
        var verification = TaxExecutionVerification.Create(_tenant.Id, o.Id, decision.Id, r.Outcome, r.FiscalDocumentReference,
            r.AuthorizationProtocol, r.Notes, json, engine.Canonical.Hash(json), _audit.UserId, _audit.UserName, now);
        _db.TaxExecutionVerifications.Add(verification);
        _audit.Record(TaxAuditActions.OpportunityExecutionVerified, AuditEntity, o.Id,
            r.Outcome == TaxExecutionOutcome.Verified
                ? $"Execução verificada conforme o cenário aprovado. Dossiê {verification.DossierHash[..12]}."
                : $"Execução divergiu do cenário aprovado: {r.Notes}",
            new { verificationId = verification.Id, outcome = r.Outcome.ToString(), verification.DossierHash, decisionHash = decision.DossierHash },
            AuditSeverity.Warning);
        await SaveAsync(ct);
        _ctx.Metrics.ExecutionVerified(r.Outcome.ToString());
        _log.LogInformation("Tax opportunity {OpportunityId} execution {Outcome}", o.Id, r.Outcome);
        return await GetAsync(id, ct);
    }

    public async Task<TaxDecisionDto> GetDecisionAsync(Guid id, CancellationToken ct)
    {
        var d = await _db.TaxDecisions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Decisão", id);
        return MapDecision(d);
    }

    private async Task<TaxOpportunityDto> MutateAsync(Guid id, Action<TaxOpportunity> change, CancellationToken ct)
    {
        var o = await LoadForUpdateAsync(id, ct);
        change(o);
        await SaveAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Loads under a row lock so two reviewers cannot interleave state changes.</summary>
    internal async Task<TaxOpportunity> LoadForUpdateAsync(Guid id, CancellationToken ct)
    {
        if (_db.Database.CurrentTransaction is null) await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM nexo.tax_opportunities WHERE id = {id} AND tenant_id = {_tenant.Id} FOR UPDATE", ct);
        return await _db.TaxOpportunities.Include(x => x.Requirements).Include(x => x.Evidence)
                   .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct)
               ?? throw new NotFoundException(NotFoundName, id);
    }

    internal async Task SaveAsync(CancellationToken ct)
    {
        await _db.SaveChangesAsync(ct);
        if (_db.Database.CurrentTransaction is not null) await _db.Database.CommitTransactionAsync(ct);
    }

    /// <summary>The engine version that produced the opportunity's scenarios; never guessed.</summary>
    private ITaxEngine EngineOf(TaxOpportunity o)
        => _engines.TryGet(o.EngineVersion, out var engine)
            ? engine
            : throw new DomainException(
                $"Esta oportunidade foi produzida pela engine {o.EngineVersion}, que não está disponível nesta versão do Orken.",
                "tax_engine_unavailable");

    private TaxOpportunityDto Map(TaxOpportunity o, TaxDecision? d, TaxExecutionVerification? x, TaxGovernanceDto governance,
        Dictionary<Guid, TaxEvidenceFile> files, TaxMaterialityInForce materiality)
    {
        var blocked = o.Status == TaxOpportunityStatus.Blocked;
        var canonical = EngineOf(o).Canonical;
        return new TaxOpportunityDto(
            o.Id, o.Type, o.ImpactNature, o.Status, o.Title, blocked ? BlockedSummary : o.Summary, o.DetectorKey, o.EngineVersion,
            o.SourceCalculationId, blocked ? null : o.EstimatedImpact, o.Risk, o.Confidence,
            o.LegalSourceCodes.Split(',', StringSplitOptions.RemoveEmptyEntries), o.Caveats, o.BlockedAs, o.BlockReason,
            o.RejectionReason, o.ReviewStartedByName, o.ReviewedByName, o.ReviewedAt, o.CreatedAt, governance.ProfessionalReviewPending.Count > 0,
            Policy.RequiredFor(o.Risk, o.EstimatedImpact, materiality.Threshold),
            o.Requirements.OrderBy(r => r.Phase).ThenBy(r => r.CreatedAt).ThenBy(r => r.Code)
                .Select(r => new TaxOpportunityRequirementDto(r.Id, r.Code, r.Text, r.LegalSourceCode, r.Met, r.Note, r.AnsweredByName, r.AnsweredAt,
                    r.Phase, r.RequiredEvidence, o.HasRequiredEvidence(r))).ToList(),
            o.Evidence.OrderBy(e => e.CreatedAt)
                .Select(e => new TaxOpportunityEvidenceDto(e.Id, e.Type, e.RequirementId, e.Description, e.DocumentReference, e.ContentHash, e.Notes,
                    e.Status, e.AddedByName, e.CreatedAt, e.ReviewedByName, e.ReviewedAt, e.ReviewNotes, e.HashKind,
                    e.FileId is { } fid && files.TryGetValue(fid, out var f) ? TaxEvidenceService.MapFile(f) : null,
                    e.Version, e.SupersedesEvidenceId, e.SupersededByEvidenceId, e.RevokedAt, e.RevokedByName, e.RevocationReason)).ToList(),
            canonical.Deserialize<ScenarioResult>(o.CurrentScenarioJson),
            blocked ? null : canonical.Deserialize<ScenarioResult>(o.ProposedScenarioJson),
            d is null ? null : MapDecision(d),
            x is null ? null : new TaxExecutionVerificationDto(x.Id, x.DecisionId, x.Outcome, x.FiscalDocumentReference, x.AuthorizationProtocol,
                x.Notes, x.VerifiedByName, x.VerifiedAt, x.DossierHash, TaxJson.Element(x.DossierJson)),
            governance, o.AssignedToId, o.AssignedToName, o.AssignedAt,
            materiality.Policy is null ? null : TaxMaterialityService.Map(materiality.Policy));
    }

    /// <summary>The detector's summary states the saving; once blocked, only a neutral description is shown.</summary>
    public const string BlockedSummary = "Proposta bloqueada como estrutura proibida. O benefício estimado não é exibido nem oferecido; o registro fica para auditoria.";

    private static TaxDecisionDto MapDecision(TaxDecision d) => new(
        d.Id, d.OpportunityId, d.Strategy, d.Risk, d.HighRiskAcknowledged, d.EngineVersion, d.ApprovedByName, d.ApprovedAt,
        d.SelfApprovalJustification, d.DossierHash, TaxJson.Element(d.DossierJson));
}
