using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Interpreter;

// Immutable audit of every reprocess run. Never overwritten — each run appends a new record.
// DiffJson enables observability, tuning, and regression detection between analyzer versions.
public class ReprocessLog : TenantEntity
{
    private ReprocessLog() { }
    private ReprocessLog(Guid tenantId) : base(tenantId) { }

    public Guid             MovementId                  { get; private set; }
    public Guid             TriggeredBy                 { get; private set; }
    public TriggerReason    TriggerReason               { get; private set; }
    public Guid             PreviousExtractionResultId  { get; private set; }
    public Guid             NewExtractionResultId       { get; private set; }
    public Guid             PreviousSuggestionId        { get; private set; }
    public Guid             NewSuggestionId             { get; private set; }
    public AnalyzerProvider AnalyzerProvider            { get; private set; }
    public string           PromptVersion               { get; private set; } = string.Empty;
    public DateTime         StartedAt                   { get; private set; }
    public DateTime?        FinishedAt                  { get; private set; }
    public int?             DurationMs                  { get; private set; }
    public bool?            WasAccepted                 { get; private set; }
    public string?          Notes                       { get; private set; }
    public string           DiffJson                    { get; private set; } = "{}";

    public static ReprocessLog Start(
        Guid             tenantId,
        Guid             movementId,
        Guid             triggeredBy,
        TriggerReason    reason,
        Guid             previousExtractionResultId,
        Guid             previousSuggestionId,
        AnalyzerProvider analyzerProvider,
        string           promptVersion)
    {
        if (movementId == Guid.Empty)
            throw new DomainException("MovementId is required.");
        if (triggeredBy == Guid.Empty)
            throw new DomainException("TriggeredBy is required.");

        return new ReprocessLog(tenantId)
        {
            MovementId                 = movementId,
            TriggeredBy                = triggeredBy,
            TriggerReason              = reason,
            PreviousExtractionResultId = previousExtractionResultId,
            PreviousSuggestionId       = previousSuggestionId,
            NewExtractionResultId      = Guid.Empty,
            NewSuggestionId            = Guid.Empty,
            AnalyzerProvider           = analyzerProvider,
            PromptVersion              = promptVersion,
            StartedAt                  = DateTime.UtcNow
        };
    }

    public void Complete(
        Guid   newExtractionResultId,
        Guid   newSuggestionId,
        string diffJson,
        string? notes = null)
    {
        var finished = DateTime.UtcNow;
        NewExtractionResultId = newExtractionResultId;
        NewSuggestionId       = newSuggestionId;
        FinishedAt            = finished;
        DurationMs            = (int)(finished - StartedAt).TotalMilliseconds;
        DiffJson              = diffJson;
        Notes                 = notes;
        SetUpdatedAt();
    }

    public void MarkAccepted(bool accepted)
    {
        WasAccepted = accepted;
        SetUpdatedAt();
    }
}
