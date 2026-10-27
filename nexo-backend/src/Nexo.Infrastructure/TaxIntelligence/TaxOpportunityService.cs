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
/// Opportunities: detection after a calculation, then Identified → InReview → Approved/Rejected, with
/// the guardrails able to block at any point before approval. Approval writes the decision dossier in
/// the same transaction. Nothing here changes a sale, a price, a ledger or a document — an approved
/// opportunity is a recorded decision, and carrying it out is up to the business.
/// </summary>
public sealed class TaxOpportunityService
{
    private const int MaxList = 200;

    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly TaxAuditTrail _audit;
    private readonly ILogger<TaxOpportunityService> _log;

    public TaxOpportunityService(NexoDbContext db, ICurrentTenant tenant, TaxAuditTrail audit, ILogger<TaxOpportunityService> log)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _log = log;
    }

    /// <summary>Runs every detector on a calculation's input. Idempotent: one opportunity per detector and calculation.</summary>
    public async Task<IReadOnlyList<Guid>> DetectAsync(Guid calculationId, TaxEngineInput input, IReadOnlyCollection<TaxEngineRule> catalogue, CancellationToken ct)
    {
        var created = new List<Guid>();
        foreach (var detector in TaxOpportunityDetectors.All)
        {
            if (await _db.TaxOpportunities.AnyAsync(o => o.SourceCalculationId == calculationId && o.DetectorKey == detector.Key, ct))
                continue;
            foreach (var c in detector.Detect(input, catalogue))
            {
                var o = TaxOpportunity.Create(_tenant.Id, c.Type, c.Title, c.Summary, c.DetectorKey, calculationId,
                    TaxCanonical.Serialize(c.Current), TaxCanonical.Serialize(c.Proposed), c.EstimatedImpact, c.Risk, c.Confidence,
                    string.Join(",", c.LegalSourceCodes), c.Caveats);
                foreach (var r in c.Requirements) o.AddRequirement(r.Code, r.Text, r.LegalSourceCode);
                _db.TaxOpportunities.Add(o);
                _audit.Record(TaxAuditActions.OpportunityIdentified, "Opportunity", o.Id,
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
        return Map(o, decision);
    }

    public Task<TaxOpportunityDto> StartReviewAsync(Guid id, CancellationToken ct) => MutateAsync(id, o =>
    {
        o.StartReview(_audit.UserId, _audit.UserName, DateTime.UtcNow);
        _audit.Record(TaxAuditActions.OpportunityReviewStarted, "Opportunity", o.Id, $"Análise iniciada: {o.Title}.");
    }, ct);

    public Task<TaxOpportunityDto> AnswerRequirementAsync(Guid id, Guid requirementId, AnswerRequirementRequest r, CancellationToken ct) => MutateAsync(id, o =>
    {
        var requirement = o.Requirements.FirstOrDefault(x => x.Id == requirementId) ?? throw new NotFoundException("Requisito", requirementId);
        var before = requirement.Met;
        o.AnswerRequirement(requirementId, r.Met, r.Note, _audit.UserId, _audit.UserName, DateTime.UtcNow);
        _audit.Record(TaxAuditActions.OpportunityRequirementAnswered, "Opportunity", o.Id,
            $"Requisito \"{requirement.Text}\": {(r.Met ? "atendido" : "não atendido")}.",
            new { requirementId, requirement.Code, before, after = r.Met, r.Note });

        var violation = TaxGuardrails.OnRequirementAnswered(o.DetectorKey, requirement.Code, r.Met);
        if (violation is not null)
        {
            o.Block(violation.Structure, violation.Reason, _audit.UserId, _audit.UserName, DateTime.UtcNow);
            _audit.Record(TaxAuditActions.OpportunityBlocked, "Opportunity", o.Id,
                $"Bloqueada como estrutura proibida ({violation.Structure}): {violation.Reason}",
                new { structure = violation.Structure.ToString(), automatic = true }, AuditSeverity.Warning);
            _log.LogWarning("Tax opportunity {OpportunityId} blocked by guardrail as {Structure}", o.Id, violation.Structure);
        }
    }, ct);

    public Task<TaxOpportunityDto> AddEvidenceAsync(Guid id, AddEvidenceRequest r, CancellationToken ct) => MutateAsync(id, o =>
    {
        var hash = TaxCanonical.Hash($"{r.Description.Trim()}\n{r.DocumentReference?.Trim()}");
        var e = o.AddEvidence(r.Description, r.DocumentReference, hash, _audit.UserId, _audit.UserName);
        _db.TaxOpportunityEvidence.Add(e);
        _audit.Record(TaxAuditActions.OpportunityEvidenceAdded, "Opportunity", o.Id, "Evidência anexada.", new { evidenceId = e.Id, hash });
    }, ct);

    public Task<TaxOpportunityDto> BlockAsync(Guid id, BlockOpportunityRequest r, CancellationToken ct) => MutateAsync(id, o =>
    {
        var before = o.Status;
        o.Block(r.Structure, r.Reason, _audit.UserId, _audit.UserName, DateTime.UtcNow);
        _audit.Record(TaxAuditActions.OpportunityBlocked, "Opportunity", o.Id,
            $"Bloqueada como estrutura proibida ({r.Structure}): {r.Reason}",
            new { before = before.ToString(), structure = r.Structure.ToString(), automatic = false }, AuditSeverity.Warning);
    }, ct);

    public Task<TaxOpportunityDto> RejectAsync(Guid id, RejectOpportunityRequest r, CancellationToken ct) => MutateAsync(id, o =>
    {
        var before = o.Status;
        o.Reject(r.Reason, _audit.UserId, _audit.UserName, DateTime.UtcNow);
        _audit.Record(TaxAuditActions.OpportunityRejected, "Opportunity", o.Id, $"Rejeitada: {r.Reason}", new { before = before.ToString() });
    }, ct);

    /// <summary>
    /// Approves and freezes the dossier: facts, both scenarios, rule versions and their hashes, legal
    /// excerpts, requirements as answered, evidence, approver, risk and engine version.
    /// </summary>
    public async Task<TaxOpportunityDto> ApproveAsync(Guid id, ApproveOpportunityRequest r, CancellationToken ct)
    {
        var o = await LoadForUpdateAsync(id, ct);
        var now = DateTime.UtcNow;
        o.Approve(r.AcknowledgeHighRisk, _audit.UserId, _audit.UserName, now);

        var calc = o.SourceCalculationId is null ? null
            : await _db.TaxCalculations.AsNoTracking().Include(c => c.Executions).FirstOrDefaultAsync(c => c.Id == o.SourceCalculationId, ct);
        var codes = o.LegalSourceCodes.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Concat(calc?.Executions.Where(e => e.Matched).SelectMany(e => e.LegalSourceCodes.Split(',', StringSplitOptions.RemoveEmptyEntries)) ?? [])
            .Distinct().ToList();
        var sources = await _db.TaxLegalSources.AsNoTracking().Where(s => codes.Contains(s.Code))
            .OrderBy(s => s.Code).Select(s => new { s.Code, s.Title, s.Reference, s.Url, s.Excerpt, verification = s.Verification.ToString() })
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
            requirements = o.Requirements.OrderBy(x => x.Code).Select(x => new { x.Code, x.Text, x.LegalSourceCode, x.Met, x.Note, x.AnsweredByName, x.AnsweredAt }),
            evidence = o.Evidence.OrderBy(x => x.CreatedAt).Select(x => new { x.Description, x.DocumentReference, x.ContentHash, x.AddedByName, x.CreatedAt }),
            risk = o.Risk.ToString(),
            highRiskAcknowledged = r.AcknowledgeHighRisk,
            approvedBy = new { id = _audit.UserId, name = _audit.UserName },
            approvedAt = now,
            engineVersion = TaxEngine.Version,
        };
        var dossierJson = System.Text.Json.JsonSerializer.Serialize(dossier, TaxCanonical.Options);
        var decision = TaxDecision.Create(_tenant.Id, o.Id, o.Title, dossierJson, TaxCanonical.Hash(dossierJson), o.Risk,
            r.AcknowledgeHighRisk, TaxEngine.Version, _audit.UserId, _audit.UserName, now);
        _db.TaxDecisions.Add(decision);
        _audit.Record(TaxAuditActions.OpportunityApproved, "Opportunity", o.Id,
            $"Aprovada por {_audit.UserName}: {o.Title}. Dossiê {decision.DossierHash[..12]}.",
            new { decisionId = decision.Id, decision.DossierHash, risk = o.Risk.ToString(), r.AcknowledgeHighRisk }, AuditSeverity.Warning);
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

    private static TaxOpportunityDto Map(TaxOpportunity o, TaxDecision? d)
    {
        var blocked = o.Status == TaxOpportunityStatus.Blocked;
        return new TaxOpportunityDto(
            o.Id, o.Type, o.ImpactNature, o.Status, o.Title, o.Summary, o.DetectorKey, o.SourceCalculationId,
            blocked ? null : o.EstimatedImpact, o.Risk, o.Confidence,
            o.LegalSourceCodes.Split(',', StringSplitOptions.RemoveEmptyEntries), o.Caveats, o.BlockedAs, o.BlockReason,
            o.RejectionReason, o.ReviewedByName, o.ReviewedAt, o.CreatedAt,
            o.Requirements.OrderBy(r => r.CreatedAt).ThenBy(r => r.Code)
                .Select(r => new TaxOpportunityRequirementDto(r.Id, r.Code, r.Text, r.LegalSourceCode, r.Met, r.Note, r.AnsweredByName, r.AnsweredAt)).ToList(),
            o.Evidence.OrderBy(e => e.CreatedAt)
                .Select(e => new TaxOpportunityEvidenceDto(e.Id, e.Description, e.DocumentReference, e.ContentHash, e.AddedByName, e.CreatedAt)).ToList(),
            TaxCanonical.Deserialize<ScenarioResult>(o.CurrentScenarioJson),
            blocked ? null : TaxCanonical.Deserialize<ScenarioResult>(o.ProposedScenarioJson),
            d is null ? null : MapDecision(d));
    }

    private static TaxDecisionDto MapDecision(TaxDecision d) => new(
        d.Id, d.OpportunityId, d.Strategy, d.Risk, d.HighRiskAcknowledged, d.EngineVersion, d.ApprovedByName, d.ApprovedAt,
        d.DossierHash, TaxJson.Element(d.DossierJson));
}
