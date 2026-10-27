using Microsoft.EntityFrameworkCore;
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
        => (await _db.TaxComplianceAssessments.AsNoTracking()
                .OrderByDescending(a => a.PeriodFrom).ThenByDescending(a => a.CreatedAt).Take(MaxList).ToListAsync(ct))
            .Select(Map).ToList();

    public async Task<TaxComplianceAssessment?> GetAsync(Guid? id, CancellationToken ct)
        => id is null ? null : await _db.TaxComplianceAssessments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);

    /// <summary>The assessment in force on a date, or null when none covers it.</summary>
    public async Task<TaxComplianceAssessment?> InForceAsync(DateOnly date, CancellationToken ct)
        => TaxComplianceAssessment.InForce(
            await _db.TaxComplianceAssessments.AsNoTracking().Where(a => a.PeriodFrom <= date && a.PeriodTo >= date).ToListAsync(ct), date);

    public async Task<TaxComplianceAssessmentDto> RecordAsync(RecordComplianceAssessmentRequest r, DateTime nowUtc, CancellationToken ct)
    {
        var a = TaxComplianceAssessment.Record(_tenant.Id, r.Status, r.PeriodFrom, r.PeriodTo, r.Scope, r.ObligationsChecked ?? [],
            r.Evidence, r.AssessedBy, r.AssessedAt ?? nowUtc, r.Notes, r.Limitations, r.PnctParticipation, _audit.UserId, _audit.UserName);
        _db.TaxComplianceAssessments.Add(a);
        _audit.Record(TaxAuditActions.ComplianceAssessmentRecorded, "ComplianceAssessment", a.Id,
            $"Obrigações acessórias {a.Status} de {a.PeriodFrom:dd/MM/yyyy} a {a.PeriodTo:dd/MM/yyyy} ({a.Scope}).",
            new
            {
                status = a.Status.ToString(), a.PeriodFrom, a.PeriodTo, a.Scope, a.ObligationsChecked, a.Evidence, a.AssessedBy,
                a.AssessedAt, a.Notes, a.Limitations, a.PnctParticipation,
            },
            AuditSeverity.Warning);
        await _db.SaveChangesAsync(ct);
        return Map(a);
    }

    public static TaxComplianceAssessmentDto Map(TaxComplianceAssessment a) => new(
        a.Id, a.Status, a.PeriodFrom, a.PeriodTo, a.Scope, a.ObligationsChecked, a.Evidence, a.AssessedBy, a.AssessedAt, a.Notes,
        a.Limitations, a.PnctParticipation, a.RecordedByName, a.CreatedAt);
}
