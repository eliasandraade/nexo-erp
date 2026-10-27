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
/// opportunity is a recorded decision, and carrying it out is up to the business. Approval follows the
/// <see cref="TaxIntelligencePolicyOptions"/>: rules not yet approved by a tax professional block it
/// when so configured (production), and whoever analysed alone must justify approving their own work.
/// </summary>
public sealed class TaxOpportunityService
{
    private const int MaxList = 200;
    private const string AuditEntity = "Opportunity";

    private readonly NexoDbContext _db;
    private readonly TaxEngineRegistry _engines;
    private readonly TaxCatalogService _catalog;
    private readonly TaxIntelligencePolicyOptions _policy;
    private readonly ICurrentTenant _tenant;
    private readonly TaxAuditTrail _audit;
    private readonly ILogger<TaxOpportunityService> _log;

    public TaxOpportunityService(
        NexoDbContext db, TaxEngineRegistry engines, TaxCatalogService catalog, IOptions<TaxIntelligencePolicyOptions> policy,
        ICurrentTenant tenant, TaxAuditTrail audit, ILogger<TaxOpportunityService> log)
    {
        _db = db;
        _engines = engines;
        _catalog = catalog;
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
                foreach (var r in c.Requirements) o.AddRequirement(r.Code, r.Text, r.LegalSourceCode);
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
        var pending = await _catalog.PendingProfessionalReviewAsync(await RuleVersionsBehindAsync(o, ct), ct);
        return Map(o, decision, pending.Count > 0);
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
        // Same versioned hashing contract as the dossier: the canonical form of the opportunity's engine.
        var hash = EngineOf(o).Canonical.Hash($"{r.Description.Trim()}\n{r.DocumentReference?.Trim()}");
        var e = o.AddEvidence(r.Description, r.DocumentReference, hash, _audit.UserId, _audit.UserName);
        _db.TaxOpportunityEvidence.Add(e);
        _audit.Record(TaxAuditActions.OpportunityEvidenceAdded, AuditEntity, o.Id, "Evidência anexada.", new { evidenceId = e.Id, hash });
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
        var selfApproval = _policy.RequireSeparateApprover && o.IsSoleAnalyst(_audit.UserId);
        o.Approve(r.AcknowledgeHighRisk, _audit.UserId, _audit.UserName, now, _policy.RequireSeparateApprover, r.SelfApprovalJustification);
        var justification = selfApproval ? r.SelfApprovalJustification!.Trim() : null;

        var ruleIds = await RuleVersionsBehindAsync(o, ct);
        var pendingReview = await _catalog.PendingProfessionalReviewAsync(ruleIds, ct);
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

        var dossier = new
        {
            strategy = o.Title,
            opportunity = new { o.Id, type = o.Type.ToString(), nature = o.ImpactNature.ToString(), o.Summary, o.DetectorKey, o.EstimatedImpact, o.Confidence, o.Caveats },
            facts = calc is null ? null : new { calc.Id, calc.ReferenceDate, calc.InputHash, calc.RuleSetHash, calc.OutputHash, calc.EngineVersion, input = TaxJson.Element(calc.InputJson) },
            currentScenario = TaxJson.Element(o.CurrentScenarioJson),
            proposedScenario = TaxJson.Element(o.ProposedScenarioJson),
            rules = calc?.Executions.Where(e => e.Matched).DistinctBy(e => e.RuleVersionId)
                .Select(e => new { e.RuleKey, e.RuleVersion, e.RuleVersionId, e.ContentHash, e.LegalSourceCodes }).ToList(),
            legalSources = sources,
            // Text verification of a source says only that the excerpt matches the official text.
            professionalReview = new { pendingRules = pendingReview, requiredByPolicy = _policy.RequireApprovedRulesForDecisions },
            requirements = o.Requirements.OrderBy(x => x.Code).Select(x => new { x.Code, x.Text, x.LegalSourceCode, x.Met, x.Note, x.AnsweredByName, x.AnsweredAt }),
            evidence = o.Evidence.OrderBy(x => x.CreatedAt).Select(x => new { x.Description, x.DocumentReference, x.ContentHash, x.AddedByName, x.CreatedAt }),
            risk = o.Risk.ToString(),
            highRiskAcknowledged = r.AcknowledgeHighRisk,
            reviewStartedBy = o.ReviewStartedByName,
            approvedBy = new { id = _audit.UserId, name = _audit.UserName },
            approvedAt = now,
            segregationOfDuties = new { required = _policy.RequireSeparateApprover, selfApproval, justification },
            engineVersion = o.EngineVersion,
        };
        var dossierJson = System.Text.Json.JsonSerializer.Serialize(dossier, engine.Canonical.Options);
        var decision = TaxDecision.Create(_tenant.Id, o.Id, o.Title, dossierJson, engine.Canonical.Hash(dossierJson), o.Risk,
            r.AcknowledgeHighRisk, o.EngineVersion, _audit.UserId, _audit.UserName, now, justification);
        _db.TaxDecisions.Add(decision);
        _audit.Record(TaxAuditActions.OpportunityApproved, AuditEntity, o.Id,
            $"Aprovada por {_audit.UserName}: {o.Title}. Dossiê {decision.DossierHash[..12]}.",
            new { decisionId = decision.Id, decision.DossierHash, risk = o.Risk.ToString(), r.AcknowledgeHighRisk, selfApproval, justification,
                pendingProfessionalReview = pendingReview }, AuditSeverity.Warning);
        await SaveAsync(ct);
        _log.LogInformation("Tax opportunity {OpportunityId} approved; decision {DecisionId}", o.Id, decision.Id);
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

    private TaxOpportunityDto Map(TaxOpportunity o, TaxDecision? d, bool professionalReviewPending)
    {
        var blocked = o.Status == TaxOpportunityStatus.Blocked;
        var canonical = EngineOf(o).Canonical;
        return new TaxOpportunityDto(
            o.Id, o.Type, o.ImpactNature, o.Status, o.Title, blocked ? BlockedSummary : o.Summary, o.DetectorKey, o.EngineVersion,
            o.SourceCalculationId, blocked ? null : o.EstimatedImpact, o.Risk, o.Confidence,
            o.LegalSourceCodes.Split(',', StringSplitOptions.RemoveEmptyEntries), o.Caveats, o.BlockedAs, o.BlockReason,
            o.RejectionReason, o.ReviewStartedByName, o.ReviewedByName, o.ReviewedAt, o.CreatedAt, professionalReviewPending,
            o.Requirements.OrderBy(r => r.CreatedAt).ThenBy(r => r.Code)
                .Select(r => new TaxOpportunityRequirementDto(r.Id, r.Code, r.Text, r.LegalSourceCode, r.Met, r.Note, r.AnsweredByName, r.AnsweredAt)).ToList(),
            o.Evidence.OrderBy(e => e.CreatedAt)
                .Select(e => new TaxOpportunityEvidenceDto(e.Id, e.Description, e.DocumentReference, e.ContentHash, e.AddedByName, e.CreatedAt)).ToList(),
            canonical.Deserialize<ScenarioResult>(o.CurrentScenarioJson),
            blocked ? null : canonical.Deserialize<ScenarioResult>(o.ProposedScenarioJson),
            d is null ? null : MapDecision(d));
    }

    /// <summary>The detector's summary states the saving; once blocked, only a neutral description is shown.</summary>
    public const string BlockedSummary = "Proposta bloqueada como estrutura proibida. O benefício estimado não é exibido nem oferecido; o registro fica para auditoria.";

    private static TaxDecisionDto MapDecision(TaxDecision d) => new(
        d.Id, d.OpportunityId, d.Strategy, d.Risk, d.HighRiskAcknowledged, d.EngineVersion, d.ApprovedByName, d.ApprovedAt,
        d.SelfApprovalJustification, d.DossierHash, TaxJson.Element(d.DossierJson));
}
