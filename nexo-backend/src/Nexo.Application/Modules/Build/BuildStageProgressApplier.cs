using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

/// <summary>
/// The single code path that changes a stage's real progress: sets the % on the stage (start/end dates and
/// status follow) and stages the history event (previous → new, source, reference, who) when the % changes or
/// a note is given. Used by the manual progress update and by measurement approval; the caller saves and then
/// refreshes today's snapshot.
/// </summary>
public static class BuildStageProgressApplier
{
    public static BuildStageProgressEvent? Apply(
        IBuildProgressRecorder recorder,
        BuildStage             stage,
        int                    percent,
        DateOnly               effectiveDate,
        BuildProgressSource    source,
        Guid                   recordedBy,
        string?                recordedByName,
        string?                note      = null,
        Guid?                  sourceRef = null)
    {
        var previous = stage.ProgressPercent;
        stage.UpdateProgress(percent, effectiveDate);
        if (previous == stage.ProgressPercent && string.IsNullOrWhiteSpace(note)) return null;

        var e = BuildStageProgressEvent.Record(
            tenantId:        stage.TenantId,
            projectId:       stage.ProjectId,
            stageId:         stage.Id,
            previousPercent: previous,
            percent:         stage.ProgressPercent,
            source:          source,
            effectiveDate:   effectiveDate,
            recordedBy:      recordedBy,
            recordedByName:  recordedByName ?? "usuário",
            note:            note,
            sourceRef:       sourceRef);
        recorder.Stage(e);
        return e;
    }
}
