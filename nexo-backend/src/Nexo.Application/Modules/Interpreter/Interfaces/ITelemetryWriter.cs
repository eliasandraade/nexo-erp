namespace Nexo.Application.Modules.Interpreter.Interfaces;

/// <summary>
/// Fire-and-forget telemetry sink.
/// Called at the end of every AI engine invocation (Analyze, Reprocess, TestConsole).
/// Implementations must NOT throw — swallow and log errors internally.
/// </summary>
public interface ITelemetryWriter
{
    Task WriteAsync(TelemetryEntry entry, CancellationToken ct = default);
}

public sealed record TelemetryEntry(
    Guid    TenantId,
    Guid?   UserId,
    Guid?   MovementId,
    string  OperationType,
    string  Provider,
    string  PromptType,
    string  PromptVersion,
    string  PromptHash,
    int     InputTokens,
    int     OutputTokens,
    long    EstimatedCostMicros,
    int     DurationMs,
    bool    Success,
    string? ErrorMessage,
    bool    FallbackUsed,
    string? FallbackFromProvider,
    IReadOnlyList<string> AnalyzerChain,
    int     RequiresInputCount,
    double  AmountConfidence,
    double  DateConfidence,
    string? RawPrompt   = null,
    string? RawResponse = null);
