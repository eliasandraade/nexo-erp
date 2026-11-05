using Microsoft.EntityFrameworkCore;
using Nexo.Domain.Exceptions;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>
/// Ancillary-obligation assessments of the establishment, per period and scope. Only ever appended;
/// the one in force on a date is the newest whose period covers it. Recording one is a Diretoria act,
/// audited with everything it states.
/// </summary>
public sealed class TaxComplianceService
{
    private const int MaxList = 200;

    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly TaxAuditTrail _audit;

    public TaxComplianceService(NexoDbContext db, ICurrentTenant tenant, TaxAuditTrail audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
    }

    public async Task<IReadOnlyList<TaxComplianceAssessmentDto>> ListAsync(CancellationToken ct)
        => (await _db.TaxComplianceAssessments.AsNoTracking().Include(a => a.Results)
                .OrderByDescending(a => a.PeriodFrom).ThenByDescending(a => a.CreatedAt).Take(MaxList).ToListAsync(ct))
            .Select(Map).ToList();

    public async Task<TaxComplianceAssessment?> GetAsync(Guid? id, CancellationToken ct)
        => id is null ? null : await _db.TaxComplianceAssessments.AsNoTracking().Include(a => a.Results).FirstOrDefaultAsync(a => a.Id == id, ct);

    /// <summary>The assessment in force on a date, or null when none covers it.</summary>
    public async Task<TaxComplianceAssessment?> InForceAsync(DateOnly date, CancellationToken ct)
        => TaxComplianceAssessment.InForce(
            await _db.TaxComplianceAssessments.AsNoTracking().Include(a => a.Results)
                .Where(a => a.PeriodFrom <= date && a.PeriodTo >= date).ToListAsync(ct), date);

    /// <summary>
    /// Records an assessment. Each obligation result names an obligation of the platform catalogue (active), whose name is kept as it
    /// stood; a referenced evidence file must be one of this company's. A reassessment points at the assessment it replaces.
    /// </summary>
    public async Task<TaxComplianceAssessmentDto> RecordAsync(RecordComplianceAssessmentRequest r, DateTime nowUtc, CancellationToken ct)
    {
        var results = await ResultsAsync(r.Obligations ?? [], ct);
        if (r.SupersedesAssessmentId is { } previous && !await _db.TaxComplianceAssessments.AnyAsync(x => x.Id == previous, ct))
            throw new NotFoundException("Avaliação", previous);
        var a = TaxComplianceAssessment.Record(_tenant.Id, r.Status, r.PeriodFrom, r.PeriodTo, r.Scope, r.ObligationsChecked ?? [],
            r.Evidence, r.AssessedBy, r.AssessedAt ?? nowUtc, r.Notes, r.Limitations, r.PnctParticipation, _audit.UserId, _audit.UserName,
            results, r.SupersedesAssessmentId);
        _db.TaxComplianceAssessments.Add(a);
        _audit.Record(TaxAuditActions.ComplianceAssessmentRecorded, "ComplianceAssessment", a.Id,
            $"Obrigações acessórias {a.Status} de {a.PeriodFrom:dd/MM/yyyy} a {a.PeriodTo:dd/MM/yyyy} ({a.Scope}).",
            new
            {
                status = a.Status.ToString(), a.PeriodFrom, a.PeriodTo, a.Scope, a.ObligationsChecked, a.Evidence, a.AssessedBy,
                a.AssessedAt, a.Notes, a.Limitations, a.PnctParticipation, a.SupersedesAssessmentId,
                results = a.Results.Select(x => new { x.ObligationCode, status = x.Status.ToString(), x.EvidenceReference, x.EvidenceFileId,
                    x.Remediation, x.RemediationVerifiedOn, x.RemediationVerifiedBy }),
            },
            AuditSeverity.Warning);
        await _db.SaveChangesAsync(ct);
        return Map(a);
    }

    private async Task<IReadOnlyList<TaxObligationResultInput>> ResultsAsync(IReadOnlyList<TaxObligationResultRequest> requested, CancellationToken ct)
    {
        if (requested.Count == 0) return [];
        var codes = requested.Select(x => x.ObligationCode.Trim().ToUpperInvariant()).Distinct().ToList();
        var catalogue = await _db.TaxObligationDefinitions.AsNoTracking().Where(o => codes.Contains(o.Code) && o.Active)
            .ToDictionaryAsync(o => o.Code, o => o.Name, ct);
        var unknown = codes.Except(catalogue.Keys).ToList();
        if (unknown.Count > 0)
            throw new DomainException($"Obrigações fora do catálogo ativo: {string.Join(", ", unknown)}.", "tax_compliance_obligation_unknown");
        var files = requested.Where(x => x.EvidenceFileId is not null).Select(x => x.EvidenceFileId!.Value).Distinct().ToList();
        var found = await _db.TaxEvidenceFiles.AsNoTracking().CountAsync(f => files.Contains(f.Id), ct);
        if (found != files.Count) throw new DomainException("Um arquivo de evidência indicado não existe nesta empresa.", "tax_compliance_evidence_file");
        return requested.Select(x =>
        {
            var code = x.ObligationCode.Trim().ToUpperInvariant();
            return new TaxObligationResultInput(code, catalogue[code], x.Status, x.EvidenceReference, x.EvidenceFileId, x.Remediation,
                x.RemediationVerifiedOn, x.RemediationVerifiedBy, x.Notes);
        }).ToList();
    }

    // ── Obligation catalogue (platform) ─────────────────────────────────────────

    public async Task<IReadOnlyList<TaxObligationDefinitionDto>> ObligationsAsync(bool onlyActive, CancellationToken ct)
        => (await _db.TaxObligationDefinitions.AsNoTracking().Where(o => !onlyActive || o.Active).OrderBy(o => o.Code).ToListAsync(ct))
            .Select(MapObligation).ToList();

    public async Task<TaxObligationDefinitionDto> DefineObligationAsync(CreateObligationDefinitionRequest r, CancellationToken ct)
    {
        var o = TaxObligationDefinition.Create(r.Code, r.Name, r.Description ?? string.Empty, r.Reference, _audit.UserName);
        if (await _db.TaxObligationDefinitions.AnyAsync(x => x.Code == o.Code, ct))
            throw new ConflictException($"Já existe a obrigação {o.Code}.", "tax_obligation_duplicate");
        _db.TaxObligationDefinitions.Add(o);
        _audit.RecordPlatform(TaxAuditActions.ObligationDefined, "Obligation", o.Id, $"Obrigação acessória {o.Code} cadastrada ({o.Reference}).",
            new { o.Code, o.Name, o.Reference });
        await _db.SaveChangesAsync(ct);
        return MapObligation(o);
    }

    public async Task<TaxObligationDefinitionDto> SetObligationActiveAsync(Guid id, SetObligationActiveRequest r, CancellationToken ct)
    {
        var o = await _db.TaxObligationDefinitions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Obrigação", id);
        o.SetActive(r.Active);
        _audit.RecordPlatform(TaxAuditActions.ObligationActiveChanged, "Obligation", o.Id, $"Obrigação {o.Code} {(r.Active ? "ativada" : "desativada")}.");
        await _db.SaveChangesAsync(ct);
        return MapObligation(o);
    }

    private static TaxObligationDefinitionDto MapObligation(TaxObligationDefinition o)
        => new(o.Id, o.Code, o.Name, o.Description, o.Reference, o.Active, o.CreatedByName);

    public static TaxComplianceAssessmentDto Map(TaxComplianceAssessment a) => new(
        a.Id, a.Status, a.PeriodFrom, a.PeriodTo, a.Scope, a.ObligationsChecked, a.Evidence, a.AssessedBy, a.AssessedAt, a.Notes,
        a.Limitations, a.PnctParticipation, a.RecordedByName, a.CreatedAt,
        a.Results.OrderBy(x => x.ObligationCode).Select(x => new TaxObligationResultDto(x.ObligationCode, x.ObligationName, x.Status,
            x.EvidenceReference, x.EvidenceFileId, x.Remediation, x.RemediationVerifiedOn, x.RemediationVerifiedBy, x.Notes)).ToList(),
        a.SupersedesAssessmentId);
}
