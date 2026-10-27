using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Application.Modules.TaxIntelligence.Engine;
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
/// follows the <see cref="TaxIntelligencePolicyOptions"/>: rules not professionally approved block it
/// when so configured (production), and the maker-checker requirement depends on risk and materiality.
/// After the operation, an execution verification records whether it really happened as approved.
/// Hashes and versions show the integrity of the records — not the material truth of the facts.
/// </summary>
public sealed class TaxOpportunityService
{
    private const int MaxList = 200;
    private const string AuditEntity = "Opportunity";

    private readonly NexoDbContext _db;
    private readonly TaxEngineRegistry _engines;
    private readonly TaxCatalogService _catalog;
    private readonly TaxComplianceService _compliance;
    private readonly TaxIntelligencePolicyOptions _policy;
    private readonly ICurrentTenant _tenant;
    private readonly TaxAuditTrail _audit;
    private readonly ILogger<TaxOpportunityService> _log;

    public TaxOpportunityService(
        NexoDbContext db, TaxEngineRegistry engines, TaxCatalogService catalog, TaxComplianceService compliance,
        IOptions<TaxIntelligencePolicyOptions> policy, ICurrentTenant tenant, TaxAuditTrail audit, ILogger<TaxOpportunityService> log)
    {
        _db = db;
        _engines = engines;
        _catalog = catalog;
        _compliance = compliance;
        _policy = policy.Value;
        _tenant = tenant;
        _audit = audit;
        _log = log;
    }

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

    public async Task<IReadOnlyList<TaxOpportunitySummaryDto>> ListAsync(TaxOpportunityStatus? status, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, MaxList);
        var q = _db.TaxOpportunities.AsNoTracking();
        if (status is not null) q = q.Where(o => o.Status == status);
        var rows = await q.OrderByDescending(o => o.CreatedAt).Take(take).ToListAsync(ct);
        return rows.Select(o => new TaxOpportunitySummaryDto(o.Id, o.Type, o.ImpactNature, o.Status, o.Title,
            o.Status == TaxOpportunityStatus.Blocked ? null : o.EstimatedImpact, o.Risk, o.Confidence, o.CreatedAt)).ToList();
    }

    public async Task<TaxOpportunityDto> GetAsync(Guid id, CancellationToken ct)
    {
        var o = await _db.TaxOpportunities.AsNoTracking().Include(x => x.Requirements).Include(x => x.Evidence)
            .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Oportunidade", id);
        var decision = await _db.TaxDecisions.AsNoTracking().FirstOrDefaultAsync(d => d.OpportunityId == id, ct);
        var execution = await _db.TaxExecutionVerifications.AsNoTracking().FirstOrDefaultAsync(e => e.OpportunityId == id, ct);
        var pending = await _catalog.PendingProfessionalReviewAsync(await RuleVersionsBehindAsync(o, ct), await ReferenceDateAsync(o, ct), ct);
        return Map(o, decision, execution, pending.Count > 0);
    }

    private async Task<DateOnly> ReferenceDateAsync(TaxOpportunity o, CancellationToken ct)
        => o.SourceCalculationId is null
            ? DateOnly.FromDateTime(o.CreatedAt)
            : await _db.TaxCalculations.AsNoTracking().Where(c => c.Id == o.SourceCalculationId).Select(c => c.ReferenceDate).FirstAsync(ct);

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
        // Same versioned hashing contract as the dossier: the canonical form of the opportunity's engine.
        var hash = EngineOf(o).Canonical.Hash(
            $"{r.Type}\n{r.RequirementId}\n{r.Description.Trim()}\n{r.DocumentReference?.Trim()}\n{r.Notes?.Trim()}");
        var e = o.AddEvidence(r.Type, r.RequirementId, r.Description, r.DocumentReference, hash, r.Notes, _audit.UserId, _audit.UserName);
        _db.TaxOpportunityEvidence.Add(e);
        _audit.Record(TaxAuditActions.OpportunityEvidenceAdded, AuditEntity, o.Id, "Evidência anexada.",
            new { evidenceId = e.Id, type = r.Type.ToString(), r.RequirementId, hash });
    }, ct);

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
        var o = await LoadForUpdateAsync(id, ct);
        var now = DateTime.UtcNow;
        var approval = _policy.RequiredFor(o.Risk, o.EstimatedImpact);
        var selfApproval = o.IsSoleAnalyst(_audit.UserId);
        o.Approve(r.AcknowledgeHighRisk, _audit.UserId, _audit.UserName, now, approval, r.SelfApprovalJustification);
        var justification = selfApproval ? r.SelfApprovalJustification!.Trim() : null;

        var referenceDate = await ReferenceDateAsync(o, ct);
        var ruleIds = await RuleVersionsBehindAsync(o, ct);
        var pendingReview = await _catalog.PendingProfessionalReviewAsync(ruleIds, referenceDate, ct);
        if (_policy.RequireApprovedRulesForDecisions && pendingReview.Count > 0)
            throw new DomainException(
                $"Regras ainda sem revisão tributária aprovada: {string.Join(", ", pendingReview)}. Ativação técnica não é aprovação profissional.",
                "tax_professional_review_pending");

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
        var compliance = await _compliance.InForceAsync(referenceDate, ct);

        var commercial = o.Evidence.Where(x => x.Type == TaxEvidenceType.CommercialInstrument).ToList();
        var original = engine.Canonical.Deserialize<ScenarioResult>(o.CurrentScenarioJson);
        var proposed = engine.Canonical.Deserialize<ScenarioResult>(o.ProposedScenarioJson);
        var pendingIssues = new List<string> { "Verificação da execução pendente: o documento fiscal da operação ainda não foi conferido." };
        if (pendingReview.Count > 0) pendingIssues.Add($"Revisão tributária pendente: {string.Join(", ", pendingReview)}.");
        if (compliance?.Status != ComplianceAssessmentStatus.Verified)
            pendingIssues.Add("Obrigações acessórias sem verificação no período e no escopo da operação.");

        var dossier = new
        {
            kind = "scenario-approval",
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
                requiredByPolicy = _policy.RequireApprovedRulesForDecisions,
                reviews = reviews.OrderBy(x => x.Key).Select(x => new
                {
                    x.Key, x.v.Version, status = x.v.ProfessionalReview.ToString(), reviewer = x.v.ProfessionalReviewedBy, x.v.ProfessionalReference,
                    scope = x.v.ProfessionalReviewScope, validFrom = x.v.ProfessionalReviewValidFrom, validTo = x.v.ProfessionalReviewValidTo,
                    assumptions = x.v.ProfessionalReviewAssumptions, caveats = x.v.ProfessionalReviewCaveats,
                }).ToList(),
            },
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
            commercialEvidence = commercial.OrderBy(x => x.CreatedAt).Select(EvidenceView),
            evidence = o.Evidence.OrderBy(x => x.CreatedAt).Select(EvidenceView),
            // A scenario approval never states that a future fiscal document exists.
            fiscalDocumentEvidence = new { status = "pending-execution-verification" },
            executionVerification = new { status = "pending" },
            pendingIssues,
            risk = o.Risk.ToString(),
            highRiskAcknowledged = r.AcknowledgeHighRisk,
            reviewStartedBy = o.ReviewStartedByName,
            approvedBy = new { id = _audit.UserId, name = _audit.UserName },
            approvedAt = now,
            makerChecker = new { required = approval.ToString(), materialityThreshold = _policy.MaterialityThreshold, selfApproval, justification },
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
                selfApproval, justification, pendingProfessionalReview = pendingReview }, AuditSeverity.Warning);
        await SaveAsync(ct);
        _log.LogInformation("Tax opportunity {OpportunityId} approved; decision {DecisionId}", o.Id, decision.Id);
        return await GetAsync(id, ct);
    }

    /// <summary>What the dossier says about money: the calculated-tax difference is not a demonstrated financial effect.</summary>
    public const string FinancialEffectNotDetermined =
        "Não determinado: a diferença é do IBS/CBS calculado entre os cenários; o efeito financeiro depende do período (compensação ou dispensa em 2026), dos preços praticados e de revisão profissional.";

    /// <summary>Integrity, not truth: what the hashes in a dossier do and do not show.</summary>
    public const string IntegrityNotice =
        "O hash e a versão demonstram a integridade deste registro — que ele não foi alterado — e não a veracidade material dos fatos declarados.";

    private static object EvidenceView(TaxOpportunityEvidence x) => new
    {
        type = x.Type.ToString(), x.RequirementId, x.Description, x.DocumentReference, x.ContentHash, status = x.Status.ToString(), x.Notes,
        x.AddedByName, x.CreatedAt, x.ReviewedByName, x.ReviewedAt, x.ReviewNotes,
    };

    /// <summary>
    /// Records whether the operation really happened as the scenario was approved, and seals it: the
    /// dossier carries the decision's hash, the execution requirements as answered and the final
    /// evidence (authorised fiscal document…). The decision itself is never changed.
    /// </summary>
    public async Task<TaxOpportunityDto> VerifyExecutionAsync(Guid id, VerifyExecutionRequest r, CancellationToken ct)
    {
        var o = await LoadForUpdateAsync(id, ct);
        var now = DateTime.UtcNow;
        var decision = await _db.TaxDecisions.AsNoTracking().FirstOrDefaultAsync(d => d.OpportunityId == id, ct)
            ?? throw new DomainException("Sem cenário aprovado não há execução a verificar.", "tax_opportunity_execution_state");
        o.VerifyExecution(r.Outcome, r.Notes, _audit.UserId, _audit.UserName, now);

        var engine = EngineOf(o);
        var executionRequirements = o.Requirements.Where(x => x.Phase == RequirementPhase.ExecutionVerification).ToList();
        var ids = executionRequirements.Select(x => (Guid?)x.Id).ToHashSet();
        var dossier = new
        {
            kind = "execution-verification",
            integrityNotice = IntegrityNotice,
            decision = new { decision.Id, decision.DossierHash, decision.ApprovedAt, decision.ApprovedByName },
            outcome = r.Outcome.ToString(),
            fiscalDocument = new { reference = r.FiscalDocumentReference?.Trim(), protocol = r.AuthorizationProtocol?.Trim() },
            requirements = executionRequirements.OrderBy(x => x.Code).Select(x => new
            {
                x.Code, x.Text, x.Met, x.Note, x.AnsweredByName, x.AnsweredAt, requiredEvidence = x.RequiredEvidence?.ToString(),
            }),
            fiscalDocumentEvidence = o.Evidence.Where(x => ids.Contains(x.RequirementId)).OrderBy(x => x.CreatedAt).Select(EvidenceView),
            notes = r.Notes?.Trim(),
            verifiedBy = new { id = _audit.UserId, name = _audit.UserName },
            verifiedAt = now,
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
    private async Task<TaxOpportunity> LoadForUpdateAsync(Guid id, CancellationToken ct)
    {
        if (_db.Database.CurrentTransaction is null) await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM nexo.tax_opportunities WHERE id = {id} AND tenant_id = {_tenant.Id} FOR UPDATE", ct);
        return await _db.TaxOpportunities.Include(x => x.Requirements).Include(x => x.Evidence)
                   .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct)
               ?? throw new NotFoundException("Oportunidade", id);
    }

    private async Task SaveAsync(CancellationToken ct)
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

    private TaxOpportunityDto Map(TaxOpportunity o, TaxDecision? d, TaxExecutionVerification? x, bool professionalReviewPending)
    {
        var blocked = o.Status == TaxOpportunityStatus.Blocked;
        var canonical = EngineOf(o).Canonical;
        return new TaxOpportunityDto(
            o.Id, o.Type, o.ImpactNature, o.Status, o.Title, blocked ? BlockedSummary : o.Summary, o.DetectorKey, o.EngineVersion,
            o.SourceCalculationId, blocked ? null : o.EstimatedImpact, o.Risk, o.Confidence,
            o.LegalSourceCodes.Split(',', StringSplitOptions.RemoveEmptyEntries), o.Caveats, o.BlockedAs, o.BlockReason,
            o.RejectionReason, o.ReviewStartedByName, o.ReviewedByName, o.ReviewedAt, o.CreatedAt, professionalReviewPending,
            _policy.RequiredFor(o.Risk, o.EstimatedImpact),
            o.Requirements.OrderBy(r => r.Phase).ThenBy(r => r.CreatedAt).ThenBy(r => r.Code)
                .Select(r => new TaxOpportunityRequirementDto(r.Id, r.Code, r.Text, r.LegalSourceCode, r.Met, r.Note, r.AnsweredByName, r.AnsweredAt,
                    r.Phase, r.RequiredEvidence, o.HasRequiredEvidence(r))).ToList(),
            o.Evidence.OrderBy(e => e.CreatedAt)
                .Select(e => new TaxOpportunityEvidenceDto(e.Id, e.Type, e.RequirementId, e.Description, e.DocumentReference, e.ContentHash, e.Notes,
                    e.Status, e.AddedByName, e.CreatedAt, e.ReviewedByName, e.ReviewedAt, e.ReviewNotes)).ToList(),
            canonical.Deserialize<ScenarioResult>(o.CurrentScenarioJson),
            blocked ? null : canonical.Deserialize<ScenarioResult>(o.ProposedScenarioJson),
            d is null ? null : MapDecision(d),
            x is null ? null : new TaxExecutionVerificationDto(x.Id, x.DecisionId, x.Outcome, x.FiscalDocumentReference, x.AuthorizationProtocol,
                x.Notes, x.VerifiedByName, x.VerifiedAt, x.DossierHash, TaxJson.Element(x.DossierJson)));
    }

    /// <summary>The detector's summary states the saving; once blocked, only a neutral description is shown.</summary>
    public const string BlockedSummary = "Proposta bloqueada como estrutura proibida. O benefício estimado não é exibido nem oferecido; o registro fica para auditoria.";

    private static TaxDecisionDto MapDecision(TaxDecision d) => new(
        d.Id, d.OpportunityId, d.Strategy, d.Risk, d.HighRiskAcknowledged, d.EngineVersion, d.ApprovedByName, d.ApprovedAt,
        d.SelfApprovalJustification, d.DossierHash, TaxJson.Element(d.DossierJson));
}
