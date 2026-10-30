using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

public enum BuildScheduleRevisionKind
{
    Baseline = 1,
    Revision = 2,
}

/// <summary>
/// A frozen copy of the project's plan (per stage: planned start, planned end, weight). Number 1 is the
/// baseline; later ones are revisions and must say why. Immutable once written. The planned curve uses the
/// latest frozen version; without one, the stages' current dates.
/// </summary>
public class BuildScheduleRevision : TenantEntity
{
    private BuildScheduleRevision() { }
    private BuildScheduleRevision(Guid tenantId) : base(tenantId) { }

    public Guid                      ProjectId     { get; private set; }
    public int                       Number        { get; private set; }
    public BuildScheduleRevisionKind Kind          { get; private set; }
    public string?                   Reason        { get; private set; }
    /// <summary>JSON array of <c>{ stageId, name, plannedStart, plannedEnd, weight }</c> (column type json, order kept).</summary>
    public string                    ItemsJson     { get; private set; } = "[]";
    public Guid                      CreatedBy     { get; private set; }
    public string                    CreatedByName { get; private set; } = string.Empty;

    public static BuildScheduleRevision Freeze(
        Guid tenantId, Guid projectId, int number, string? reason, string itemsJson, Guid createdBy, string createdByName)
    {
        if (projectId == Guid.Empty) throw new DomainException("Obra inválida.");
        if (number < 1) throw new DomainException("Número de revisão inválido.");
        var cleanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (number > 1 && cleanReason is null)
            throw new DomainException("A linha de base já foi definida: informe o motivo da revisão do cronograma.",
                "schedule_revision_reason_required");
        if (cleanReason is { Length: > 1000 }) throw new DomainException("O motivo pode ter até 1000 caracteres.");
        if (string.IsNullOrWhiteSpace(itemsJson)) throw new DomainException("Cronograma vazio.");

        return new BuildScheduleRevision(tenantId)
        {
            ProjectId     = projectId,
            Number        = number,
            Kind          = number == 1 ? BuildScheduleRevisionKind.Baseline : BuildScheduleRevisionKind.Revision,
            Reason        = cleanReason,
            ItemsJson     = itemsJson,
            CreatedBy     = createdBy,
            CreatedByName = string.IsNullOrWhiteSpace(createdByName) ? "usuário" : createdByName.Trim(),
        };
    }
}
