using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

/// <summary>Where a stage progress change came from.</summary>
public enum BuildProgressSource
{
    Manual      = 1,
    Measurement = 2,
    DailyLog    = 3,
    AiConfirmed = 4,
}

/// <summary>
/// Append-only history of a stage's real progress: every change of <see cref="BuildStage.ProgressPercent"/>
/// is one event (previous → new, source, effective date, who). Never updated nor deleted — the stage keeps
/// the current value, the events explain how it got there.
/// </summary>
public class BuildStageProgressEvent : TenantEntity
{
    private BuildStageProgressEvent() { }
    private BuildStageProgressEvent(Guid tenantId) : base(tenantId) { }

    public Guid                ProjectId       { get; private set; }
    public Guid                StageId         { get; private set; }
    public int                 PreviousPercent { get; private set; }
    public int                 Percent         { get; private set; }
    public BuildProgressSource Source          { get; private set; }
    /// <summary>Optional reference to the record that produced the change (measurement, daily log, AI suggestion).</summary>
    public Guid?               SourceRef       { get; private set; }
    public string?             Note            { get; private set; }
    public DateOnly            EffectiveDate   { get; private set; }
    public Guid                RecordedBy      { get; private set; }
    public string              RecordedByName  { get; private set; } = string.Empty;

    public static BuildStageProgressEvent Record(
        Guid tenantId, Guid projectId, Guid stageId, int previousPercent, int percent,
        BuildProgressSource source, DateOnly effectiveDate, Guid recordedBy, string recordedByName,
        string? note = null, Guid? sourceRef = null)
    {
        if (projectId == Guid.Empty || stageId == Guid.Empty) throw new DomainException("Etapa inválida.");
        if (percent is < 0 or > 100 || previousPercent is < 0 or > 100)
            throw new DomainException("O progresso precisa ficar entre 0 e 100.");
        if (!Enum.IsDefined(source)) throw new DomainException("Origem do progresso inválida.");
        var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (cleanNote is { Length: > 1000 }) throw new DomainException("A observação pode ter até 1000 caracteres.");

        return new BuildStageProgressEvent(tenantId)
        {
            ProjectId       = projectId,
            StageId         = stageId,
            PreviousPercent = previousPercent,
            Percent         = percent,
            Source          = source,
            SourceRef       = sourceRef,
            Note            = cleanNote,
            EffectiveDate   = effectiveDate,
            RecordedBy      = recordedBy,
            RecordedByName  = string.IsNullOrWhiteSpace(recordedByName) ? "usuário" : recordedByName.Trim(),
        };
    }
}
